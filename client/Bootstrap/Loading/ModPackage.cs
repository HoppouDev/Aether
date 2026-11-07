using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Modding;

namespace Aether.Bootstrap;

internal sealed record ModManifest(int SchemaVersion, string Id, string Version,
    string Assembly, JsonElement Depends, string? Entrypoint, string[]? Mixins, string? Mappings,
    string? Name, string? Description, JsonElement Authors, JsonElement Contributors,
    JsonElement Source, JsonElement Homepage, JsonElement License);

internal sealed record ModPackage(string Archive, string Root, ModManifest Manifest, ModInfo Info, ModDependency[] Dependencies)
{
    internal string EntryPath => Path.Combine(Root, Manifest.Assembly);

    internal Stream OpenResource(string path)
    {
        ValidatePath(path);
        return File.OpenRead(Path.Combine(Root, "resources", path));
    }

    internal Stream OpenIcon()
    {
        string icon = Info.IconPath ?? throw new FileNotFoundException($"Mod '{Manifest.Id}' has no icon.png.");
        return File.OpenRead(Path.Combine(Root, icon));
    }

    internal static ModPackage Read(string archive, string root)
    {
        using var zip = ZipFile.OpenRead(archive);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.TrimEnd('/');
            ValidatePath(name);
            if (!paths.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Duplicate entry or symbolic link '{entry.FullName}'.");
            string directory = entry.FullName.EndsWith('/') ? name : "";
            int separator = name.LastIndexOf('/');
            if (directory.Length == 0 && separator >= 0)
                directory = name[..separator];
            while (directory.Length > 0)
            {
                if (directories.TryGetValue(directory, out string? earlier) && earlier != directory)
                    throw new InvalidDataException($"Case-colliding directory '{directory}'.");
                directories[directory] = directory;
                int parent = directory.LastIndexOf('/');
                directory = parent < 0 ? "" : directory[..parent];
            }
            total = checked(total + entry.Length);
            if (total > 512L * 1024 * 1024 || paths.Count > 4096)
                throw new InvalidDataException("Archive exceeds the size or entry count limit.");
        }

        ZipArchiveEntry metadata = zip.GetEntry("aether.mod.json")
            ?? throw new InvalidDataException("Missing root aether.mod.json.");
        if (metadata.Length > 1024 * 1024)
            throw new InvalidDataException("Manifest exceeds 1 MiB.");
        var buffer = new byte[81920];
        using var json = new MemoryStream((int)metadata.Length);
        CopyEntry(metadata, json, buffer);
        json.Position = 0;
        var manifest = JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty manifest.");
        if (manifest.SchemaVersion != 1 || !IsId(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException($"Invalid schema, ID or version for mod '{manifest.Id}'.");
        if (manifest.Id is "aether" or "terraria")
            throw new InvalidDataException($"Mod ID '{manifest.Id}' is reserved for the client.");
        ValidatePath(manifest.Assembly);
        if (!manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || zip.GetEntry(manifest.Assembly) == null)
            throw new InvalidDataException($"Mod '{manifest.Id}' has no entry assembly '{manifest.Assembly}'.");
        if (manifest.Mappings is { } mappings)
        {
            ValidatePath(mappings);
            if (zip.GetEntry(mappings) == null)
                throw new InvalidDataException($"Mod '{manifest.Id}' has no mappings file '{mappings}'.");
        }
        ModDependency[] dependencies = ModDependency.Read(manifest.Depends);

        string? iconPath = zip.GetEntry("icon.png") != null ? "icon.png" : null;
        ModInfo info = ModMetadata.Read(manifest, iconPath);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string path = Path.Combine(root, entry.FullName);
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var destination = new FileStream(path, FileMode.CreateNew);
            CopyEntry(entry, destination, buffer);
        }
        return new ModPackage(archive, root, manifest, info, dependencies);
    }

    private static void CopyEntry(ZipArchiveEntry entry, Stream destination, byte[] buffer)
    {
        // The archive-wide limit is based on declared sizes. Each stream must honor its size.
        using Stream source = entry.Open();
        long remaining = entry.Length;
        while (remaining > 0)
        {
            int count = source.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (count == 0)
                throw new InvalidDataException($"Archive entry '{entry.FullName}' ended before its declared size.");
            destination.Write(buffer, 0, count);
            remaining -= count;
        }

        if (source.ReadByte() != -1)
            throw new InvalidDataException($"Archive entry '{entry.FullName}' exceeds its declared size.");
    }

    internal static bool IsId(string? id) => id != null && Regex.IsMatch(id, @"\A[a-z][a-z0-9_.-]{0,63}\z");

    private static void ValidatePath(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':')
            || name.Split('/').Any(segment => segment is "" or "." or ".." || segment.EndsWith('.')
                || segment.EndsWith(' ') || segment.Any(c => c < 32 || "<>\"|?*".Contains(c))
                || Regex.IsMatch(segment.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Unsafe archive path '{name}'.");
    }

    internal static List<ModPackage> Sort(IEnumerable<ModPackage> packages, string aetherVersion, string terrariaVersion)
    {
        var pending = new SortedDictionary<string, ModPackage>(StringComparer.Ordinal);
        foreach (ModPackage package in packages)
        {
            if (!pending.TryAdd(package.Manifest.Id, package))
                throw new InvalidDataException($"Duplicate mod ID '{package.Manifest.Id}' in '{package.Archive}' and '{pending[package.Manifest.Id].Archive}'.");
        }
        var versions = pending.Values.ToDictionary(package => package.Manifest.Id, package => package.Manifest.Version, StringComparer.Ordinal);
        versions.Add("aether", aetherVersion);
        versions.Add("terraria", terrariaVersion);
        var parsedVersions = versions.ToDictionary(entry => entry.Key, entry => ModVersion.Parse(entry.Value), StringComparer.Ordinal);
        foreach (ModPackage package in pending.Values)
        {
            foreach (ModDependency dependency in package.Dependencies)
            {
                if (!versions.TryGetValue(dependency.Id, out string? actual))
                    throw new InvalidDataException($"Mod '{package.Manifest.Id}' in '{package.Archive}' requires missing mod '{dependency.Id}' ({dependency.Requirement}).");
                if (!dependency.Alternatives.Any(matches => matches(parsedVersions[dependency.Id])))
                    throw new InvalidDataException($"Mod '{package.Manifest.Id}' in '{package.Archive}' requires '{dependency.Id}' {dependency.Requirement}; found {actual}.");
            }
        }

        var ordered = new List<ModPackage>();
        var loaded = new HashSet<string>(StringComparer.Ordinal) { "aether", "terraria" };
        while (pending.Count > 0)
        {
            ModPackage? next = pending.Values.FirstOrDefault(package => package.Dependencies.All(dependency => loaded.Contains(dependency.Id)));
            if (next == null)
                throw new InvalidDataException($"Dependency cycle blocks: {string.Join(", ", pending.Keys)}.");
            pending.Remove(next.Manifest.Id);
            loaded.Add(next.Manifest.Id);
            ordered.Add(next);
        }
        return ordered;
    }
}

internal sealed class ModContext(ModPackage package) : AssemblyLoadContext(package.Manifest.Id)
{
    internal Assembly? Game { get; set; }
    internal Func<Assembly>? LoadApi { get; set; }

    private readonly AssemblyDependencyResolver resolver = new(package.EntryPath);
    private readonly Dictionary<string, Assembly> images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Path, ModContext Context)> wovenDependencies = new(StringComparer.OrdinalIgnoreCase);

    internal string? ResolveManagedPath(AssemblyName name)
    {
        string path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Path.GetDirectoryName(package.EntryPath)!, name.Name + ".dll");
        return File.Exists(path) ? path : null;
    }

    internal void ShareWovenDependency(string name, string path, ModContext context) =>
        wovenDependencies.Add(name, (path, context));

    internal Assembly LoadManaged(string path)
    {
        lock (images)
        {
            if (images.TryGetValue(path, out Assembly? existing))
                return existing;
            using FileStream stream = File.OpenRead(path);
            Assembly assembly = LoadFromStream(stream);
            images.Add(path, assembly);
            return assembly;
        }
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (string.Equals(name.Name, "Terraria", StringComparison.OrdinalIgnoreCase))
            return Game;
        if (name.Name != null && Program.SharedNames.Contains(name.Name))
            return Default.LoadFromAssemblyName(name);

        if (string.Equals(name.Name, "Aether.Api", StringComparison.OrdinalIgnoreCase))
            return LoadApi?.Invoke() ?? throw new InvalidDataException($"Mod '{package.Manifest.Id}' must declare a dependency on aether-api.");

        if (wovenDependencies.TryGetValue(name.FullName, out var dependency))
            return dependency.Context.LoadManaged(dependency.Path);

        string? path = ResolveManagedPath(name);
        return path == null ? null : LoadManaged(path);
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(name);
        return path == null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
