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
        Registration.RequireContentId(id);
        return new ItemKey(null, id);
    }

    public override string ToString() => CustomId ?? VanillaId?.ToString(CultureInfo.InvariantCulture) ?? "(invalid)";
}
