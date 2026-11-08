using System;
using System.Runtime.InteropServices;

namespace Aether.Graphics.Terrain;

// Resolve the extension in the same Linux FNA3D library used by FNA.
internal static class NativeLibrary
{
    [DllImport("libdl.so.2")]
    private static extern IntPtr dlopen(string path, int flags);

    [DllImport("libdl.so.2")]
    private static extern IntPtr dlsym(IntPtr library, string name);

    [DllImport("libdl.so.2")]
    private static extern int dlclose(IntPtr library);

    internal static bool TryLoad(string path, out IntPtr library)
    {
        library = IntPtr.Zero;
        if (Environment.OSVersion.Platform != PlatformID.Unix || !System.IO.File.Exists(path))
            return false;
        try { library = dlopen(path, 2); }
        catch (DllNotFoundException) { return false; }
        return library != IntPtr.Zero;
    }

    internal static bool TryGetExport(IntPtr library, string name, out IntPtr entry)
    {
        entry = dlsym(library, name);
        return entry != IntPtr.Zero;
    }

    internal static IntPtr GetExport(IntPtr library, string name)
    {
        if (TryGetExport(library, name, out IntPtr entry))
            return entry;
        throw new EntryPointNotFoundException(name);
    }

    internal static void Free(IntPtr library) => dlclose(library);
}
