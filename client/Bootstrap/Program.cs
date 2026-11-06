using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Aether.Modding;

namespace Aether.Bootstrap;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? temporary = null;
        string savePath = ".";
        string phase = "startup";
        try
        {
            var parameters = ClientStartup.ParseArguments(args, monoArgs: true);
            savePath = ClientStartup.ResolveSavePath(parameters);
            string clientPath = Path.Combine(AppContext.BaseDirectory, "Terraria.dll");
            string modsPath = Path.Combine(savePath, "Mods");
            string[] archives = parameters.ContainsKey("-nomods") || !Directory.Exists(modsPath)
                ? [] : Directory.GetFiles(modsPath, "*.aethermod").Order(StringComparer.Ordinal).ToArray();

            if (archives.Length == 0)
                return Launch(AssemblyLoadContext.Default.LoadFromAssemblyPath(clientPath), args);

            temporary = Directory.CreateTempSubdirectory("aether-mods-").FullName;
            var packages = new List<ModPackage>();
            string terrariaVersion = AssemblyName.GetAssemblyName(clientPath).Version!.ToString();
            string aetherVersion = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            foreach (string archive in archives)
            {
                phase = archive;
                packages.Add(ModPackage.Read(archive, Path.Combine(temporary, packages.Count.ToString())));
            }

            phase = "dependency resolution";
            packages = ModPackage.Sort(packages, aetherVersion, terrariaVersion);
            var packagesById = packages.ToDictionary(package => package.Manifest.Id, StringComparer.Ordinal);
            ModPackage FindPackage(string modId) => packagesById.TryGetValue(modId, out var package)
                ? package
                : throw new KeyNotFoundException($"Mod '{modId}' is not loaded.");
            ModResources.OpenResource = (modId, path) => FindPackage(modId).OpenResource(path);
            ModResources.OpenPackageIcon = modId => FindPackage(modId).OpenIcon();
            var contexts = new Dictionary<string, (ModPackage Package, ModContext Context)>(StringComparer.OrdinalIgnoreCase);
            foreach (ModPackage package in packages)
            {
                phase = $"mod '{package.Manifest.Id}' in '{package.Archive}'";
                string name = AssemblyName.GetAssemblyName(package.EntryPath).Name!;
                if (SharedNames.Contains(name) || !contexts.TryAdd(name, (package, new ModContext(package))))
                    throw new InvalidDataException($"Reserved or duplicate entry assembly identity '{name}'.");
            }

            phase = "assembly weaving";
            Assembly game;
            if (packages.Any(package => package.Manifest.Mixins is { Length: > 0 }))
            {
                using var image = new MemoryStream();
                var references = AssemblyWeaver.Write(clientPath, packages, image, parameters.ContainsKey("-mixindebug"));
                var dependencies = ResolveWovenDependencies(contexts.Values, references);
                var gameContext = new GameContext(dependencies);
                image.Position = 0;
                game = gameContext.LoadFromStream(image);
            }
            else
            {
                game = AssemblyLoadContext.Default.LoadFromAssemblyPath(clientPath);
            }

            foreach (var (_, context) in contexts.Values)
                context.Game = game;

            ModCatalog.LoadedMods = Array.AsReadOnly(packages.Select(package => package.Info).ToArray());
            ClientStartup.InitializeMods = () =>
            {
                var initializers = new List<(ModPackage Package, Type Type)>();
                foreach (var (package, context) in contexts.Values)
                {
                    phase = $"mod '{package.Manifest.Id}' in '{package.Archive}'";
                    if (package.Manifest.Entrypoint is not { } entrypoint)
                        continue;
                    Assembly assembly = context.LoadManaged(package.EntryPath);
                    Type type = assembly.GetType(entrypoint, throwOnError: true)!;
                    if (!typeof(IModInitializer).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                        throw new InvalidDataException($"Entrypoint '{entrypoint}' must implement IModInitializer and have a public parameterless constructor.");
                    initializers.Add((package, type));
                }

                foreach (var (package, type) in initializers)
                {
                    phase = $"initializer for mod '{package.Manifest.Id}' in '{package.Archive}'";
                    ((IModInitializer)Activator.CreateInstance(type)!).Initialize();
                }
                phase = "game startup";
            };

            return Launch(game, args);
        }
        catch (Exception exception)
        {
            string logPath = Path.GetFullPath(Path.Combine(savePath, "Logs", "mod-load-error.log"));
            string message = $"Aether could not start during {phase}.\n{exception.Message}";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"{message}\n\n{exception}\n");
                message += $"\n\nDetails: {logPath}\nUse -nomods to launch without mods.";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Console.Error.WriteLine(message);
            ShowError(message);
            return 1;
        }
        finally
        {
            ClientStartup.InitializeMods = null;
            ModResources.OpenResource = null;
            ModResources.OpenPackageIcon = null;
            ModCatalog.LoadedMods = Array.Empty<ModInfo>();
            if (temporary != null)
            {
                try { Directory.Delete(temporary, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static readonly HashSet<string> SharedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Terraria", "Aether.Modding", "FNA", "ReLogic"
    };

    private static Dictionary<string, (string Path, ModContext Context)> ResolveWovenDependencies(
        IEnumerable<(ModPackage Package, ModContext Context)> mods, Dictionary<string, HashSet<ModPackage>> references)
    {
        var contexts = mods.ToDictionary(mod => mod.Package, mod => mod.Context);
        var result = new Dictionary<string, (string Path, ModContext Context)>(StringComparer.OrdinalIgnoreCase);
        string[] platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var platformNames = platformAssemblies.Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (reference, owners) in references)
        {
            var identity = new AssemblyName(reference);
            string name = identity.Name!;
            if (SharedNames.Contains(name))
                continue;

            var candidates = new List<(ModPackage Package, string Path, ModContext Context)>();
            foreach (ModPackage owner in owners.OrderBy(owner => owner.Manifest.Id, StringComparer.Ordinal))
            {
                ModContext context = contexts[owner];
                string? path = context.ResolveManagedPath(identity);
                if (path == null)
                {
                    if (platformNames.Contains(name))
                        continue;
                    throw new InvalidDataException($"Mod '{owner.Manifest.Id}' in '{owner.Archive}' is missing woven reference '{reference}'.");
                }

                if (platformNames.Contains(name))
                    throw new InvalidDataException($"Woven reference '{reference}' in mod '{owner.Manifest.Id}' conflicts with a platform assembly.");
                if (!string.Equals(AssemblyName.GetAssemblyName(path).FullName, reference, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"'{path}' in mod '{owner.Manifest.Id}' does not match woven reference '{reference}'.");

                candidates.Add((owner, path, context));
            }

            if (candidates.Count == 0)
                continue;
            var primary = candidates.FirstOrDefault(item => item.Path == item.Package.EntryPath, candidates[0]);
            if (candidates.Count > 1)
            {
                byte[] hash = HashFile(primary.Path);
                foreach (var candidate in candidates)
                {
                    if (candidate.Context == primary.Context)
                        continue;
                    if (!HashFile(candidate.Path).AsSpan().SequenceEqual(hash))
                        throw new InvalidDataException($"Woven reference '{reference}' differs between mods '{primary.Package.Manifest.Id}' and '{candidate.Package.Manifest.Id}'.");

                    candidate.Context.ShareWovenDependency(reference, primary.Path, primary.Context);
                }
            }
            result.Add(reference, (primary.Path, primary.Context));
        }
        return result;
    }

    private static byte[] HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    // The host lists the pristine client in its dependency graph. A separate context ensures
    // the runtime uses the woven image and lets the mods share that exact assembly instance.
    private sealed class GameContext(Dictionary<string, (string Path, ModContext Context)> dependencies)
        : AssemblyLoadContext("Aether game")
    {
        protected override Assembly? Load(AssemblyName name) =>
            dependencies.TryGetValue(name.FullName, out var dependency)
                ? dependency.Context.LoadManaged(dependency.Path) : null;
    }

    private static int Launch(Assembly game, string[] args)
    {
        try
        {
            MethodInfo entrypoint = game.EntryPoint ??
                game.GetType("Terraria.LinuxLaunch", throwOnError: true)!.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("Terraria.LinuxLaunch", "Main");
            entrypoint.Invoke(null, [args]);
            return Environment.ExitCode;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MessageBox(uint flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message, nint window);

    private static void ShowError(string message)
    {
        if (Console.IsErrorRedirected)
        {
            // A Windows GUI launch has no stderr handle, which the console API reports as redirected.
            if (!OperatingSystem.IsWindows())
                return;
            using Stream stderr = Console.OpenStandardError();
            if (stderr != Stream.Null)
                return;
        }
        string name = OperatingSystem.IsWindows() ? "SDL3.dll" : OperatingSystem.IsMacOS() ? "libSDL3.0.dylib" : "libSDL3.so.0";
        if (!NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, name), out nint library))
            return;
        try
        {
            if (NativeLibrary.TryGetExport(library, "SDL_ShowSimpleMessageBox", out nint symbol))
                Marshal.GetDelegateForFunctionPointer<MessageBox>(symbol)(0x10, "Aether startup error", message, 0);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}
