using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Modding;

namespace Aether.Bootstrap;

internal sealed record ModManifest(int SchemaVersion, string Id, string Version, string GameVersion,
    string Assembly, string[]? Depends, string? Entrypoint, string[]? Mixins, string? Mappings);

internal sealed record ModPackage(string Archive, string Root, ModManifest Manifest)
{
    internal string EntryPath => Path.Combine(Root, Manifest.Assembly);

    internal static ModPackage Read(string archive, string root, Version gameVersion)
    {
        using var zip = ZipFile.OpenRead(archive);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.TrimEnd('/');
            ValidatePath(name);
            if (!paths.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException($"Duplicate entry or symbolic link '{entry.FullName}'.");
            string directory = entry.FullName.EndsWith('/') ? name : "";
            if (directory.Length == 0 && name.LastIndexOf('/') is int separator && separator >= 0)
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
        using Stream json = metadata.Open();
        var manifest = JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty manifest.");
        if (manifest.SchemaVersion != 1 || !IsId(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException($"Invalid schema, ID or version for mod '{manifest.Id}'.");
        if (!Version.TryParse(manifest.GameVersion, out Version? version) || version != gameVersion)
            throw new InvalidDataException($"Mod '{manifest.Id}' requires game {manifest.GameVersion}; found {gameVersion}.");
        ValidatePath(manifest.Assembly);
        if (!manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || zip.GetEntry(manifest.Assembly) == null)
            throw new InvalidDataException($"Mod '{manifest.Id}' has no entry assembly '{manifest.Assembly}'.");
        if (manifest.Mappings is { } mappings)
        {
            ValidatePath(mappings);
            if (zip.GetEntry(mappings) == null)
                throw new InvalidDataException($"Mod '{manifest.Id}' has no mappings file '{mappings}'.");
        }
        if ((manifest.Depends ?? []).Any(id => !IsId(id)))
            throw new InvalidDataException($"Mod '{manifest.Id}' declares an invalid dependency ID.");

        zip.ExtractToDirectory(root);
        return new ModPackage(archive, root, manifest);
    }

    private static bool IsId(string? id) => id != null && Regex.IsMatch(id, @"\A[a-z][a-z0-9_.-]{0,63}\z");

    private static void ValidatePath(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':')
            || name.Split('/').Any(segment => segment is "" or "." or ".." || segment.EndsWith('.')
                || segment.EndsWith(' ') || segment.Any(c => c < 32 || "<>\"|?*".Contains(c))
                || Regex.IsMatch(segment.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Unsafe archive path '{name}'.");
    }

    internal static List<ModPackage> Sort(IEnumerable<ModPackage> packages)
    {
        var pending = new SortedDictionary<string, ModPackage>(StringComparer.Ordinal);
        foreach (ModPackage package in packages)
        {
            if (!pending.TryAdd(package.Manifest.Id, package))
                throw new InvalidDataException($"Duplicate mod ID '{package.Manifest.Id}' in '{package.Archive}' and '{pending[package.Manifest.Id].Archive}'.");
        }
        foreach (ModPackage package in pending.Values)
            foreach (string dependency in package.Manifest.Depends ?? [])
                if (!pending.ContainsKey(dependency))
                    throw new InvalidDataException($"Mod '{package.Manifest.Id}' in '{package.Archive}' requires missing mod '{dependency}'.");

        var ordered = new List<ModPackage>();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            ModPackage? next = pending.Values.FirstOrDefault(package => (package.Manifest.Depends ?? []).All(loaded.Contains));
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

    private readonly AssemblyDependencyResolver resolver = new(package.EntryPath);
    private readonly Dictionary<string, Assembly> images = new(StringComparer.Ordinal);

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

        string path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Path.GetDirectoryName(package.EntryPath)!, name.Name + ".dll");
        return File.Exists(path) ? LoadManaged(path) : null;
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(name);
        return path == null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
