using Aether.Modding;
using Terraria;
using Terraria.ID;

namespace Aether.Api;

public enum PlayerEffectTiming
{
    Unavailable,
    Timed,
    Untimed
}

public readonly record struct PlayerEffect(int Id, bool IsDebuff, PlayerEffectTiming Timing, int? RemainingTicks);

public static partial class Players
{
    public static bool TryGetEffects(Player player, out IReadOnlyList<PlayerEffect> effects)
    {
        RequireActive(player);
        effects = Array.Empty<PlayerEffect>();
        if (!CanReadEffects(player))
            return false;
        var active = new List<PlayerEffect>();
        for (int slot = 0; slot < player.buffType.Length; slot++)
        {
            if (IsActiveEffect(player, slot))
                active.Add(ReadEffect(player, slot));
        }
        effects = active.AsReadOnly();
        return true;
    }

    public static bool TryGetEffect(Player player, int effectId, out PlayerEffect effect)
    {
        RequireActive(player);
        ValidateEffect(effectId);
        return TryReadEffect(player, effectId, out effect);
    }

    public static bool TryApplyEffect(Player player, int effectId, int durationTicks)
    {
        RequireWritable(player);
        ValidateEffect(effectId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(durationTicks, 5_184_000);
        if (player.buffImmune[effectId])
            return false;
        player.AddBuff(effectId, durationTicks);
        return TryReadEffect(player, effectId, out _);
    }

    public static bool RemoveEffect(Player player, int effectId)
    {
        RequireWritable(player);
        ValidateEffect(effectId);
        if (!TryReadEffect(player, effectId, out _))
            return false;
        player.ClearBuff(effectId);
        return true;
    }

    private static bool CanReadEffects(Player player) =>
        IsLocal(player) || (player.AetherObservedStats & PlayerObservation.Effects) != 0;

    private static void ValidateEffect(int effectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effectId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(effectId, BuffID.Count);
    }

    private static bool TryReadEffect(Player player, int effectId, out PlayerEffect effect)
    {
        effect = default;
        if (!CanReadEffects(player))
            return false;
        for (int slot = 0; slot < player.buffType.Length; slot++)
        {
            if (player.buffType[slot] == effectId && IsActiveEffect(player, slot))
            {
                effect = ReadEffect(player, slot);
                return true;
            }
        }
        return false;
    }

    private static bool IsActiveEffect(Player player, int slot) =>
        player.buffType[slot] > 0 && player.buffType[slot] < BuffID.Count && player.buffTime[slot] > 0;

    private static PlayerEffect ReadEffect(Player player, int slot)
    {
        int id = player.buffType[slot];
        PlayerEffectTiming timing = !IsLocal(player) ? PlayerEffectTiming.Unavailable :
            BuffID.Sets.TimeLeftDoesNotDecrease[id] ? PlayerEffectTiming.Untimed : PlayerEffectTiming.Timed;
        return new PlayerEffect(id, Main.debuff[id], timing, timing == PlayerEffectTiming.Timed ? player.buffTime[slot] : null);
    }
}
