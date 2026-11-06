namespace Aether.Modding;

public sealed record ModInfo(
    string Id,
    string Version,
    string Name,
    string Description,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Contributors,
    string? Source,
    string? Homepage,
    IReadOnlyList<string> Licenses,
    string? IconPath);

public static class ModCatalog
{
    /// <summary>Metadata for the active mod packages, in initialization order.</summary>
    public static IReadOnlyList<ModInfo> LoadedMods { get; internal set; } = Array.Empty<ModInfo>();
}
