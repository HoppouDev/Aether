using System.Diagnostics.CodeAnalysis;
using Aether.Modding;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Creative;

namespace Aether.Api;

public enum PlayerGameMode : byte
{
    Classic,
    Mediumcore,
    Hardcore,
    Journey
}

[Flags]
public enum PlayerStatsFields
{
    None = 0,
    Character = 1,
    Health = 2,
    Mana = 4,
    EffectiveStats = 8,
    JourneyGodMode = 16,
    All = Character | Health | Mana | EffectiveStats | JourneyGodMode
}

public readonly record struct PlayerStats(int Slot, string Name, PlayerGameMode GameMode, bool IsAlive,
    int Health, int BaseMaxHealth, int MaxHealth, int Mana, int BaseMaxMana, int MaxMana,
    int Defense, bool JourneyGodMode, PlayerStatsFields Available);

public static partial class Players
{
    public static bool TryGetLocal([NotNullWhen(true)] out Player? player)
    {
        Registration.RequireGameThread();
        player = null;
        return GameBridge.WorldReady && !Main.dedServ && TryGet(Main.myPlayer, out player);
    }

    public static bool TryGet(int slot, [NotNullWhen(true)] out Player? player)
    {
        Registration.RequireGameThread();
        player = null;
        if (!GameBridge.WorldReady || (uint)slot >= Main.maxPlayers)
            return false;
        Player candidate = Main.player[slot];
        if (!candidate.active || candidate.whoAmI != slot || (Main.netMode == 2 && Netplay.Clients[slot].State != 10))
            return false;
        player = candidate;
        return true;
    }

    public static IReadOnlyList<Player> GetActive()
    {
        Registration.RequireGameThread();
        if (!GameBridge.WorldReady)
            return Array.Empty<Player>();
        var result = new List<Player>();
        for (int slot = 0; slot < Main.maxPlayers; slot++)
            if (TryGet(slot, out Player? player))
                result.Add(player);
        return result.AsReadOnly();
    }

    public static PlayerStats GetStats(Player player)
    {
        RequireActive(player);
        PlayerStatsFields available = PlayerStatsFields.None;
        if (IsLocal(player))
        {
            available = PlayerStatsFields.All;
        }
        else
        {
            PlayerObservation observed = player.AetherObservedStats;
            if ((observed & PlayerObservation.Character) != 0)
                available |= PlayerStatsFields.Character;
            if ((observed & PlayerObservation.Health) != 0)
                available |= PlayerStatsFields.Health;
            if ((observed & PlayerObservation.Mana) != 0)
                available |= PlayerStatsFields.Mana;
        }
        return new PlayerStats(player.whoAmI, player.name, (PlayerGameMode)player.difficulty, !player.dead && !player.ghost,
            player.statLife, player.statLifeMax, player.statLifeMax2, player.statMana, player.statManaMax,
            player.statManaMax2, player.statDefense, player.creativeGodMode, available);
    }

    public static bool TryGetWeaponDamage(Player player, Item weapon, out int damage)
    {
        RequireActive(player);
        ArgumentNullException.ThrowIfNull(weapon);
        damage = 0;
        if (!IsLocal(player))
            return false;
        damage = player.GetWeaponDamage(weapon);
        return true;
    }

    public static void SetGameMode(Player player, PlayerGameMode mode)
    {
        RequireWritable(player);
        if (mode is < PlayerGameMode.Classic or > PlayerGameMode.Journey)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if ((mode == PlayerGameMode.Journey) != Main.IsJourneyMode)
            throw new InvalidOperationException("Journey mode requires a matching world.");
        player.difficulty = (byte)mode;
    }

    public static void SetJourneyGodMode(Player player, bool enabled)
    {
        RequireWritable(player);
        if (!Main.IsJourneyMode || player.difficulty != (byte)PlayerGameMode.Journey)
            throw new InvalidOperationException("Journey god mode requires a Journey character and world.");
        CreativePowerManager.Instance.GetPower<CreativePowers.GodmodePower>().SetEnabledState(player.whoAmI, enabled);
    }

    public static void SetHealth(Player player, int health)
    {
        RequireWritable(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(health);
        player.statLife = Math.Min(health, Math.Max(1, player.statLifeMax2));
    }

    public static void SetMana(Player player, int mana)
    {
        RequireWritable(player);
        ArgumentOutOfRangeException.ThrowIfNegative(mana);
        player.statMana = Math.Min(mana, Math.Max(0, player.statManaMax2));
    }

    public static void SetBaseMaxHealth(Player player, int maximum)
    {
        RequireWritable(player);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 20);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 500);
        player.statLifeMax = maximum;
    }

    public static void SetBaseMaxMana(Player player, int maximum)
    {
        RequireWritable(player);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 200);
        player.statManaMax = maximum;
    }

    public static int Heal(Player player, int amount)
    {
        RequireWritable(player);
        int restored = RestorationAmount(amount, player.statLife, player.statLifeMax2);
        if (restored != 0)
            player.Heal(restored);
        return restored;
    }

    public static int RestoreMana(Player player, int amount)
    {
        RequireWritable(player);
        int restored = RestorationAmount(amount, player.statMana, player.statManaMax2);
        if (restored != 0)
        {
            player.statMana += restored;
            player.ManaEffect(restored);
        }
        return restored;
    }

    public static int Hurt(Player player, int damage, PlayerDeathReason reason, int hitDirection = 0)
    {
        RequireWritable(player);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(damage, short.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(hitDirection, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hitDirection, 1);
        int before = player.statLife;
        player.Hurt(reason, damage, hitDirection);
        return Math.Max(0, before - Math.Max(0, player.statLife));
    }

    public static bool Kill(Player player, PlayerDeathReason reason)
    {
        RequireWritable(player);
        ArgumentNullException.ThrowIfNull(reason);
        player.KillMe(reason, player.statLife, 0);
        return player.dead;
    }

    internal static void RequireActive(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!TryGet(player.whoAmI, out Player? current) || !ReferenceEquals(player, current))
            throw new InvalidOperationException("The player is unavailable in the current world.");
    }

    internal static void RequireWritable(Player player)
    {
        RequireActive(player);
        if (Main.netMode != 0 || !IsLocal(player) || player.dead || player.ghost)
            throw new InvalidOperationException("This operation requires a living local player in single-player.");
    }

    internal static bool IsLocal(Player player) => !Main.dedServ && player.whoAmI == Main.myPlayer;

    private static int RestorationAmount(int amount, int current, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return (int)Math.Min(amount, Math.Max(0L, (long)maximum - current));
    }
}
