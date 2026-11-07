using System.Globalization;
using Terraria.ID;

namespace Aether.Api;

public readonly record struct ItemKey
{
    public int? VanillaId { get; }
    public string? CustomId { get; }

    private ItemKey(int? vanillaId, string? customId)
    {
        VanillaId = vanillaId;
        CustomId = customId;
    }

    public static ItemKey Vanilla(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, ItemID.VanillaCount);
        return new ItemKey(id, null);
    }

    public static ItemKey Custom(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int colon = id.IndexOf(':');
        if (colon is < 1 or > 64 || colon == id.Length - 1 || id.Length > 1024 || !char.IsAsciiLetterLower(id[0]))
            throw new ArgumentException("Use a namespaced item ID in the form mod-id:name.", nameof(id));
        for (int index = 0; index < id.Length; index++)
        {
            if (index != colon && !Registration.IsIdCharacter(id[index]))
                throw new ArgumentException("Use a namespaced item ID in the form mod-id:name.", nameof(id));
        }
        return new ItemKey(null, id);
    }

    public override string ToString() => CustomId ?? VanillaId?.ToString(CultureInfo.InvariantCulture) ?? "(invalid)";
}
