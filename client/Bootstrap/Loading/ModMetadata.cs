using System.Text.Json;
using Aether.Modding;

namespace Aether.Bootstrap;

internal static class ModMetadata
{
    internal static ModInfo Read(ModManifest manifest, string? iconPath) => new(
        manifest.Id, manifest.Version,
        string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
        manifest.Description ?? "",
        ReadTextArray(manifest.Authors, "authors"),
        ReadTextArray(manifest.Contributors, "contributors"),
        ReadOptionalText(manifest.Source, "source"),
        ReadOptionalText(manifest.Homepage, "homepage"),
        ReadLicenses(manifest.License),
        iconPath);

    private static IReadOnlyList<string> ReadTextArray(JsonElement value, string key)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Manifest '{key}' must be an array of strings.");

        return Array.AsReadOnly(value.EnumerateArray().Select(item => RequiredText(item, key)).ToArray());
    }

    private static string? ReadOptionalText(JsonElement value, string key) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : RequiredText(value, key);

    private static IReadOnlyList<string> ReadLicenses(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => Array.Empty<string>(),
        JsonValueKind.String => Array.AsReadOnly(new[] { RequiredText(value, "license") }),
        JsonValueKind.Array => ReadTextArray(value, "license"),
        _ => throw new InvalidDataException("Manifest 'license' must be a string or an array of strings.")
    };

    private static string RequiredText(JsonElement value, string key)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
            return text;
        throw new InvalidDataException($"Manifest '{key}' must contain nonempty text.");
    }
}
