using Aether.Modding;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Aether.Api;

public readonly record struct NpcHandle
{
    public int Slot { get; }
    internal long WorldRevision { get; }
    internal long SpawnIdentity { get; }

    internal NpcHandle(int slot, long worldRevision, long spawnIdentity) =>
        (Slot, WorldRevision, SpawnIdentity) = (slot, worldRevision, spawnIdentity);
}

public readonly record struct ProjectileHandle
{
    public int Slot { get; }
    internal long WorldRevision { get; }
    internal long SpawnIdentity { get; }

    internal ProjectileHandle(int slot, long worldRevision, long spawnIdentity) =>
        (Slot, WorldRevision, SpawnIdentity) = (slot, worldRevision, spawnIdentity);
}

public readonly record struct NpcSpawn(Vector2 Bottom, IEntitySource Source, int Target = 255,
    float Ai0 = 0, float Ai1 = 0, float Ai2 = 0, float Ai3 = 0);

public readonly record struct ProjectileSpawn(Vector2 Center, Vector2 Velocity, int Damage, float Knockback, int Owner,
    IEntitySource Source, float Ai0 = 0, float Ai1 = 0, float Ai2 = 0);

public readonly record struct NpcSnapshot(NpcHandle Handle, NpcTypeKey Type, Vector2 Center, Vector2 Velocity,
    Rectangle Bounds, int Health, int MaxHealth, int Damage, bool Friendly, bool Boss);

public readonly record struct ProjectileSnapshot(ProjectileHandle Handle, ProjectileTypeKey Type, Vector2 Center,
    Vector2 Velocity, Rectangle Bounds, int Damage, int Owner, int TimeLeft, bool Friendly, bool Hostile);

public static partial class Entities
{
    public static bool TrySpawnNpc(NpcTypeKey type, NpcSpawn spawn, out NpcHandle handle)
    {
        Registration.RequireGameThread();
        handle = default;
        if (!CanSpawn || Main.netMode == 2 && type.CustomId == null || !NpcRegistry.TryResolve(type, out int id))
            return false;

        ArgumentNullException.ThrowIfNull(spawn.Source);
        RequireWorldPosition(spawn.Bottom);
        ArgumentOutOfRangeException.ThrowIfNegative(spawn.Target);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spawn.Target, Main.maxPlayers);
        RequireFinite(spawn.Ai0, nameof(spawn.Ai0));
        RequireFinite(spawn.Ai1, nameof(spawn.Ai1));
        RequireFinite(spawn.Ai2, nameof(spawn.Ai2));
        RequireFinite(spawn.Ai3, nameof(spawn.Ai3));

        long revision = GameBridge.WorldRevision;
        int slot = NPC.AetherNewNPC(spawn.Source, (int)spawn.Bottom.X, (int)spawn.Bottom.Y, id,
            spawn.Ai0, spawn.Ai1, spawn.Ai2, spawn.Ai3, spawn.Target, out long identity);
        if (slot == Main.maxNPCs)
            return false;
        handle = new(slot, revision, identity);
        return true;
    }

    public static bool TrySpawnProjectile(ProjectileTypeKey type, ProjectileSpawn spawn, out ProjectileHandle handle)
    {
        Registration.RequireGameThread();
        handle = default;
        if (!CanSpawn || Main.netMode == 2 && type.CustomId == null || !ProjectileRegistry.TryResolve(type, out int id))
            return false;

        ArgumentNullException.ThrowIfNull(spawn.Source);
        RequireWorldPosition(spawn.Center);
        if (spawn.Owner != Main.maxPlayers && (Main.netMode != 0 || spawn.Owner != Main.myPlayer))
            throw new ArgumentOutOfRangeException(nameof(spawn.Owner), "Use 255 for a world-owned projectile, or the local player in single-player.");
        RequireVelocity(spawn.Velocity, nameof(spawn.Velocity));
        ArgumentOutOfRangeException.ThrowIfNegative(spawn.Damage);
        if (Main.netMode == 2)
            ArgumentOutOfRangeException.ThrowIfGreaterThan(spawn.Damage, short.MaxValue);
        RequireFinite(spawn.Knockback, nameof(spawn.Knockback));
        ArgumentOutOfRangeException.ThrowIfNegative(spawn.Knockback);
        RequireFinite(spawn.Ai0, nameof(spawn.Ai0));
        RequireFinite(spawn.Ai1, nameof(spawn.Ai1));
        RequireFinite(spawn.Ai2, nameof(spawn.Ai2));

        long revision = GameBridge.WorldRevision;
        int slot = Projectile.AetherNewProjectile(spawn.Source, spawn.Center, spawn.Velocity, id,
            spawn.Damage, spawn.Knockback, spawn.Owner, spawn.Ai0, spawn.Ai1, spawn.Ai2, out long identity);
        if (slot == Main.maxProjectiles)
            return false;
        handle = new(slot, revision, identity);
        return true;
    }

    public static bool TryGetNpc(NpcHandle handle, out NpcSnapshot snapshot)
    {
        Registration.RequireGameThread();
        if (handle.SpawnIdentity != 0 && handle.WorldRevision == GameBridge.WorldRevision &&
            TryGetNpc(handle.Slot, out snapshot) && snapshot.Handle == handle)
            return true;
        snapshot = default;
        return false;
    }

    public static bool TryGetProjectile(ProjectileHandle handle, out ProjectileSnapshot snapshot)
    {
        Registration.RequireGameThread();
        if (handle.SpawnIdentity != 0 && handle.WorldRevision == GameBridge.WorldRevision &&
            TryGetProjectile(handle.Slot, out snapshot) && snapshot.Handle == handle)
            return true;
        snapshot = default;
        return false;
    }

    public static bool TryGetNpc(int slot, out NpcSnapshot snapshot)
    {
        Registration.RequireGameThread();
        snapshot = default;
        if (!GameBridge.WorldReady || (uint)slot >= Main.maxNPCs || Main.npc[slot] is not { active: true } npc ||
            !NpcRegistry.TryGetKey(npc.netID, out var type) || !IsObserved(npc.Center))
            return false;

        snapshot = SnapshotNpc(npc, new(slot, GameBridge.WorldRevision, npc.AetherSpawnIdentity), type);
        return true;
    }

    internal static NpcSnapshot SnapshotNpc(NPC npc, NpcHandle handle, NpcTypeKey type) =>
        new(handle, type, npc.Center, npc.velocity, npc.Hitbox, npc.life, npc.lifeMax, npc.damage, npc.friendly, npc.boss);

    public static bool TrySetNpcVelocity(NpcHandle handle, Vector2 velocity)
    {
        Registration.RequireGameThread();
        RequireVelocity(velocity, nameof(velocity));
        if (!CanSpawn || !TryGetNpc(handle, out _) || !NpcRegistry.IsManaged(Main.npc[handle.Slot]))
            return false;
        Main.npc[handle.Slot].velocity = velocity;
        return true;
    }

    public static bool TryDespawnNpc(NpcHandle handle)
    {
        Registration.RequireGameThread();
        if (!CanSpawn || !TryGetNpc(handle, out _) || !NpcRegistry.IsManaged(Main.npc[handle.Slot]))
            return false;
        Main.npc[handle.Slot].AetherRetire(EntityRemovalReason.Despawned);
        return true;
    }

    public static bool TryGetProjectile(int slot, out ProjectileSnapshot snapshot)
    {
        Registration.RequireGameThread();
        snapshot = default;
        if (!GameBridge.WorldReady || (uint)slot >= Main.maxProjectiles ||
            Main.projectile[slot] is not { active: true } projectile ||
            !ProjectileRegistry.TryGetKey(projectile.type, out var type) || !IsObserved(projectile.Center))
            return false;

        snapshot = SnapshotProjectile(projectile, new(slot, GameBridge.WorldRevision, projectile.AetherSpawnIdentity), type);
        return true;
    }

    internal static ProjectileSnapshot SnapshotProjectile(Projectile projectile, ProjectileHandle handle, ProjectileTypeKey type) =>
        new(handle, type, projectile.Center, projectile.velocity, projectile.Hitbox, projectile.damage,
            projectile.owner, projectile.timeLeft, projectile.friendly, projectile.hostile);

    public static bool TrySetProjectileVelocity(ProjectileHandle handle, Vector2 velocity)
    {
        Registration.RequireGameThread();
        RequireVelocity(velocity, nameof(velocity));
        if (!CanSpawn || !TryGetProjectile(handle, out _) || !ProjectileRegistry.IsManaged(Main.projectile[handle.Slot]))
            return false;
        Main.projectile[handle.Slot].velocity = velocity;
        return true;
    }

    public static bool TryDespawnProjectile(ProjectileHandle handle)
    {
        Registration.RequireGameThread();
        if (!CanSpawn || !TryGetProjectile(handle, out _) || !ProjectileRegistry.IsManaged(Main.projectile[handle.Slot]))
            return false;
        Main.projectile[handle.Slot].AetherKill(EntityRemovalReason.Despawned);
        return true;
    }

    public static IReadOnlyList<NpcSnapshot> FindNpcs(Rectangle area)
    {
        Registration.RequireGameThread();
        RequireArea(area);
        if (!GameBridge.WorldReady)
            return Array.Empty<NpcSnapshot>();

        var result = new List<NpcSnapshot>();
        for (int slot = 0; slot < Main.maxNPCs; slot++)
            if (TryGetNpc(slot, out var snapshot) && area.Intersects(snapshot.Bounds))
                result.Add(snapshot);
        return result.AsReadOnly();
    }

    public static IReadOnlyList<ProjectileSnapshot> FindProjectiles(Rectangle area)
    {
        Registration.RequireGameThread();
        RequireArea(area);
        if (!GameBridge.WorldReady)
            return Array.Empty<ProjectileSnapshot>();

        var result = new List<ProjectileSnapshot>();
        for (int slot = 0; slot < Main.maxProjectiles; slot++)
            if (TryGetProjectile(slot, out var snapshot) && area.Intersects(snapshot.Bounds))
                result.Add(snapshot);
        return result.AsReadOnly();
    }

    private static bool CanSpawn => GameBridge.AuthoritativeWorld;

    private static void RequireVelocity(Vector2 velocity, string argument)
    {
        RequireFinite(velocity.X, argument);
        RequireFinite(velocity.Y, argument);
        const int limit = ClientContent.MaxReplicatedEntityVelocity;
        if (Main.netMode == 2 && (Math.Abs(velocity.X) > limit || Math.Abs(velocity.Y) > limit))
            throw new ArgumentOutOfRangeException(argument, $"Replicated velocity must be within {limit} pixels per tick on each axis.");
    }

    private static bool IsObserved(Vector2 center)
    {
        if (Main.netMode != 1)
            return true;
        int x = (int)MathF.Floor(center.X / 16);
        int y = (int)MathF.Floor(center.Y / 16);
        return WorldGen.InWorld(x, y) && (Main.sectionManager?.TileLoaded(x, y) ?? false);
    }

    private static void RequireWorldPosition(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || position.X < 0 || position.Y < 0 ||
            position.X >= Main.maxTilesX * 16 || position.Y >= Main.maxTilesY * 16)
            throw new ArgumentOutOfRangeException(nameof(position), "Use a finite world position in pixels.");
    }

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, "Use a finite value.");
    }

    private static void RequireArea(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0 ||
            (long)area.X + area.Width > int.MaxValue || (long)area.Y + area.Height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(area));
    }
}
