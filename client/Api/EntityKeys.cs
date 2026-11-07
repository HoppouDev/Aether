using System.Globalization;
using Terraria.ID;

namespace Aether.Api;

public readonly record struct NpcTypeKey
{
    public int? VanillaId { get; }
    public string? CustomId { get; }

    private NpcTypeKey(int? vanillaId, string? customId) => (VanillaId, CustomId) = (vanillaId, customId);

    public static NpcTypeKey Vanilla(int id)
    {
        if (id == 0 || id <= NPCID.NegativeIDCount || id >= NPCID.Count)
            throw new ArgumentOutOfRangeException(nameof(id));
        return new(id, null);
    }

    public static NpcTypeKey Custom(string id)
    {
        Registration.RequireContentId(id);
        return new(null, id);
    }

    public override string ToString() => CustomId ?? VanillaId?.ToString(CultureInfo.InvariantCulture) ?? "(invalid)";
}

public readonly record struct ProjectileTypeKey
{
    public int? VanillaId { get; }
    public string? CustomId { get; }

    private ProjectileTypeKey(int? vanillaId, string? customId) => (VanillaId, CustomId) = (vanillaId, customId);

    public static ProjectileTypeKey Vanilla(int id)
    {
        if (id <= 0 || id >= ProjectileID.Count)
            throw new ArgumentOutOfRangeException(nameof(id));
        return new(id, null);
    }

    public static ProjectileTypeKey Custom(string id)
    {
        Registration.RequireContentId(id);
        return new(null, id);
    }

    public override string ToString() => CustomId ?? VanillaId?.ToString(CultureInfo.InvariantCulture) ?? "(invalid)";
}
