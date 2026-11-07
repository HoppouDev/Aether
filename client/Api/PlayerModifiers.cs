using Aether.Modding;
using Terraria;

namespace Aether.Api;

public sealed record PlayerStatModifier
{
    public int MaxHealthBonus { get; init; }
    public int MaxManaBonus { get; init; }
    public int DefenseBonus { get; init; }
    public float WeaponDamageMultiplier { get; init; } = 1;
    public float MeleeDamageMultiplier { get; init; } = 1;
    public float RangedDamageMultiplier { get; init; } = 1;
    public float MagicDamageMultiplier { get; init; } = 1;
    public float SummonDamageMultiplier { get; init; } = 1;

    internal float ClassFactor(Item item) => item.melee ? MeleeDamageMultiplier : item.ranged ? RangedDamageMultiplier :
        item.magic ? MagicDamageMultiplier : item.summon ? SummonDamageMultiplier : 1;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxHealthBonus);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxManaBonus);
        ArgumentOutOfRangeException.ThrowIfNegative(DefenseBonus);
        ValidateFactor(WeaponDamageMultiplier);
        ValidateFactor(MeleeDamageMultiplier);
        ValidateFactor(RangedDamageMultiplier);
        ValidateFactor(MagicDamageMultiplier);
        ValidateFactor(SummonDamageMultiplier);
    }

    private static void ValidateFactor(float factor)
    {
        if (!float.IsFinite(factor) || factor < 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Damage factors must be finite and nonnegative.");
    }
}

public sealed class PlayerModifier
{
    public string Id { get; }

    internal PlayerModifier(string id) => Id = id;

    public void Set(Player player, PlayerStatModifier modifier)
    {
        Players.RequireWritable(player);
        ArgumentNullException.ThrowIfNull(modifier);
        modifier.Validate();
        PlayerModifiers.Set(player, Id, modifier);
    }

    public bool Clear(Player player)
    {
        Players.RequireWritable(player);
        return PlayerModifiers.Clear(player, Id);
    }
}

public static partial class Players
{
    public static PlayerModifier RegisterModifier(string id) => PlayerModifiers.Register(id);
}

internal static class PlayerModifiers
{
    private static readonly Dictionary<string, PlayerModifier> registered = new(StringComparer.Ordinal);
    private static readonly Dictionary<Player, SortedDictionary<string, PlayerStatModifier>> entries = new(ReferenceEqualityComparer.Instance);

    internal static PlayerModifier Register(string id)
    {
        string owner = Registration.Owner();
        ArgumentNullException.ThrowIfNull(id);
        var modifier = new PlayerModifier(id);
        Registration.Add(registered, id, modifier, owner);
        return modifier;
    }

    internal static void Set(Player player, string id, PlayerStatModifier modifier)
    {
        if (!entries.TryGetValue(player, out var modifiers))
            entries.Add(player, modifiers = new SortedDictionary<string, PlayerStatModifier>(StringComparer.Ordinal));
        modifiers[id] = modifier;
    }

    internal static bool Clear(Player player, string id)
    {
        if (!entries.TryGetValue(player, out var modifiers) || !modifiers.Remove(id))
            return false;
        if (modifiers.Count == 0)
            entries.Remove(player);
        return true;
    }

    internal static void ClearWorld() => entries.Clear();

    internal static void Apply(Player player)
    {
        if (Main.netMode != 0 || Main.gameMenu || !entries.TryGetValue(player, out var modifiers))
            return;

        long health = 0;
        long mana = 0;
        long defense = 0;
        foreach (PlayerStatModifier modifier in modifiers.Values)
        {
            health += modifier.MaxHealthBonus;
            mana += modifier.MaxManaBonus;
            defense += modifier.DefenseBonus;
        }

        if (health != 0)
            player.statLifeMax2 = (int)Math.Clamp(player.statLifeMax2 + health, 1, 1000);
        if (mana != 0)
            player.statManaMax2 = (int)Math.Clamp(player.statManaMax2 + mana, 0, 400);
        if (defense != 0)
            player.statDefense = (int)Math.Clamp(player.statDefense + defense, 0, 1000);
    }

    internal static float WeaponFactor(Player player, Item item)
    {
        if (Main.netMode != 0 || Main.gameMenu || !entries.TryGetValue(player, out var modifiers))
            return 1;

        // Accumulate in log space so large factors can still be reduced by later factors.
        double log = 0;
        foreach (PlayerStatModifier modifier in modifiers.Values)
        {
            float classFactor = modifier.ClassFactor(item);
            if (modifier.WeaponDamageMultiplier == 0 || classFactor == 0)
                return 0;
            log += Math.Log(modifier.WeaponDamageMultiplier) + Math.Log(classFactor);
        }
        return (float)Math.Min(16, Math.Exp(log));
    }
}

[Mixin(typeof(Player))]
internal static class PlayerHooks
{
    [Inject("GetWeaponDamageMultiplier(Terraria.Item)", At.RETURN, Cancellable = true)]
    private static void ModifyDamage(Player self, Item item, CallbackInfoReturnable<float> callback)
    {
        float factor = GameBridge.GetPlayerWeaponFactor(self, item);
        if (factor != 1)
            callback.SetReturnValue(callback.ReturnValue * factor);
    }
}
