namespace Aether.Modding;

public static class ModResources
{
    internal static Func<string, string, Stream>? OpenResource { get; set; }
    internal static Func<string, Stream>? OpenPackageIcon { get; set; }

    /// <summary>Opens a read-only stream for a file relative to the named mod's resources directory.</summary>
    /// <param name="modId">The exact mod ID in aether.mod.json.</param>
    /// <param name="path">A relative file path with forward slashes.</param>
    /// <returns>A stream that the caller must dispose.</returns>
    public static Stream OpenRead(string modId, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(modId);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var open = OpenResource ?? throw new InvalidOperationException("Mod resources are unavailable.");
        return open(modId, path);
    }

    /// <summary>Opens the named mod's root icon.png. The caller must dispose the stream.</summary>
    public static Stream OpenIcon(string modId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modId);
        var open = OpenPackageIcon ?? throw new InvalidOperationException("Mod resources are unavailable.");
        return open(modId);
    }
}
