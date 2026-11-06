using System.Text.Json;

namespace Aether.Bootstrap;

internal sealed record ModDependency(string Id, string Requirement, Func<ModVersion?, bool>[] Alternatives)
{
    internal static ModDependency[] Read(JsonElement depends)
    {
        if (depends.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return [];
        if (depends.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Manifest 'depends' must be an object mapping IDs to version constraints.");

        var dependencies = new Dictionary<string, ModDependency>(StringComparer.Ordinal);
        foreach (JsonProperty entry in depends.EnumerateObject())
        {
            if (!ModPackage.IsId(entry.Name) || dependencies.ContainsKey(entry.Name))
                throw new InvalidDataException($"Invalid or duplicate dependency ID '{entry.Name}'.");

            try
            {
                string[] constraints = entry.Value.ValueKind == JsonValueKind.Array
                    ? entry.Value.EnumerateArray().Select(ReadConstraint).ToArray()
                    : [ReadConstraint(entry.Value)];
                if (constraints.Length == 0)
                    throw new FormatException("Expected at least one version constraint.");

                dependencies.Add(entry.Name, new ModDependency(entry.Name, string.Join(" or ", constraints),
                    constraints.Select(ModVersion.ParseConstraint).ToArray()));
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Invalid 'depends' constraint for '{entry.Name}'. {exception.Message}", exception);
            }
        }
        return dependencies.Values.ToArray();
    }

    private static string ReadConstraint(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text ? text
            : throw new FormatException("Expected a string version constraint.");
}
