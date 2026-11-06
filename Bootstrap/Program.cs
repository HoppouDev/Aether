using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
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
            Version version = AssemblyName.GetAssemblyName(clientPath).Version!;
            foreach (string archive in archives)
            {
                phase = archive;
                packages.Add(ModPackage.Read(archive, Path.Combine(temporary, packages.Count.ToString()), version));
            }

            phase = "dependency resolution";
            packages = ModPackage.Sort(packages);
            var contexts = new Dictionary<string, (ModPackage Package, ModContext Context)>(StringComparer.OrdinalIgnoreCase);
            foreach (ModPackage package in packages)
            {
                phase = $"mod '{package.Manifest.Id}' in '{package.Archive}'";
                string name = AssemblyName.GetAssemblyName(package.EntryPath).Name!;
                if (name is "Terraria" or "Aether.Modding" or "FNA" or "ReLogic" || !contexts.TryAdd(name, (package, new ModContext(package))))
                    throw new InvalidDataException($"Reserved or duplicate entry assembly identity '{name}'.");
            }

            Assembly? ResolveMod(AssemblyLoadContext _, AssemblyName name)
            {
                return name.Name != null && contexts.TryGetValue(name.Name, out var mod)
                    ? mod.Context.LoadFromAssemblyPath(mod.Package.EntryPath) : null;
            }

            phase = "assembly weaving";
            using var image = new MemoryStream();
            Weave(clientPath, packages, image, parameters.ContainsKey("-mixindebug"));
            image.Position = 0;
            AssemblyLoadContext.Default.Resolving += ResolveMod;
            Assembly game = AssemblyLoadContext.Default.LoadFromStream(image);

            ClientStartup.InitializeMods = () =>
            {
                var initializers = new List<(ModPackage Package, Type Type)>();
                foreach (var (package, context) in contexts.Values)
                {
                    phase = $"mod '{package.Manifest.Id}' in '{package.Archive}'";
                    if (package.Manifest.Entrypoint is not { } entrypoint)
                        continue;
                    Assembly assembly = context.LoadFromAssemblyPath(package.EntryPath);
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
            if (temporary != null)
            {
                try { Directory.Delete(temporary, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static int Launch(Assembly game, string[] args)
    {
        try
        {
            game.EntryPoint!.Invoke(null, [args]);
            return Environment.ExitCode;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Weave(string path, IReadOnlyList<ModPackage> packages, Stream output, bool debug)
    {
        foreach (ModPackage package in packages)
            if (package.Manifest.Mixins is { Length: > 0 })
                throw new InvalidDataException($"Mixin weaving unavailable for '{package.Manifest.Id}' in '{package.Archive}'.");
        using FileStream source = File.OpenRead(path);
        source.CopyTo(output);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MessageBox(uint flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message, nint window);

    private static void ShowError(string message)
    {
        if (Console.IsErrorRedirected)
            return;
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
