using System.ComponentModel;

namespace Aether.Modding;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record ClientItemDefinition(string Id, string Name, string? Tooltip, int TextureItemId);

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record ClientEntitySprite(int VanillaType, string? ResourcePath, int Frames, int TicksPerFrame);

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record ClientNpcDefinition(string Id, string Name, ClientEntitySprite Sprite, int SaveSchema);

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record ClientProjectileDefinition(string Id, string Name, ClientEntitySprite Sprite);

public enum EntityRemovalReason
{
    Despawned,
    Defeated,
    Replaced,
    WorldUnloaded,
    BehaviorFailed,
    Expired,
    Hit,
    TileCollision
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record ContentModRequirement(string Id, string Version);

[EditorBrowsable(EditorBrowsableState.Never)]
public static class ClientContent
{
    public const int MaxReplicatedEntityVelocity = 4096;
    private static readonly object itemTableLock = new();
    private static bool itemTablesSized;
    private static bool npcTablesSized;
    private static bool projectileTablesSized;
    private static string? profile;

    public static IReadOnlyList<ClientItemDefinition> Items { get; private set; } = Array.Empty<ClientItemDefinition>();
    public static Action<int, object>? ApplyItemDefaults { get; private set; }
    public static Action<object, object>? ItemUsed { get; internal set; }
    public static Action<object, object, object, int>? ItemHitNpc { get; internal set; }
    public static Action<object, object, int>? ItemEquipped { get; internal set; }
    public static Action<object>? ApplyPlayerModifiers { get; internal set; }
    public static IReadOnlyList<ClientNpcDefinition> Npcs { get; private set; } = Array.Empty<ClientNpcDefinition>();
    public static Action<int, object>? ApplyNpcDefaults { get; private set; }
    public static Action<object, object>? NpcSpawned { get; internal set; }
    public static Action<object>? NpcUpdated { get; internal set; }
    public static Action<object, int, int, bool>? NpcHit { get; internal set; }
    public static Action<object, object, int>? NpcHitPlayer { get; internal set; }
    public static Action<object, EntityRemovalReason>? NpcRemoved { get; internal set; }
    public static IReadOnlyList<ClientProjectileDefinition> Projectiles { get; private set; } = Array.Empty<ClientProjectileDefinition>();
    public static Action<int, object>? ApplyProjectileDefaults { get; private set; }
    public static Action<object, object>? ProjectileSpawned { get; internal set; }
    public static Action<object>? ProjectileUpdated { get; internal set; }
    public static Action<object, object, int, bool>? ProjectileHitNpc { get; internal set; }
    public static Action<object, object, int>? ProjectileHitPlayer { get; internal set; }
    public static Action<object, EntityRemovalReason>? ProjectileRemoved { get; internal set; }
    public static bool HasItems => Items.Count != 0;
    public static bool HasEntities => Npcs.Count != 0 || Projectiles.Count != 0;
    public static bool HasPersistentNpcs { get; private set; }
    public static bool IsDedicatedServer { get; internal set; }
    public static string? ConnectionVersion { get; internal set; }
    public static Action? ServerReady { get; internal set; }
    public static Action? ServerStopped { get; internal set; }
    public static Action<int>? ServerPlayerJoined { get; internal set; }
    public static string? SaveIdentity => profile;
    public static string? WorldSaveIdentity => profile ?? (HasPersistentNpcs ? "entities" : null);
    public static IReadOnlyList<ContentModRequirement> SaveRequirements { get; internal set; } = Array.Empty<ContentModRequirement>();
    internal static Action? FinalizeRegistration { get; set; }

    public static int SizeItemTables()
    {
        lock (itemTableLock)
        {
            itemTablesSized = true;
            return Items.Count;
        }
    }

    internal static void ConfigureItems(ClientItemDefinition[] items, Action<int, object> applyDefaults, string saveProfile)
    {
        lock (itemTableLock)
        {
            if (itemTablesSized)
                throw new InvalidOperationException("Terraria's item tables were initialized before custom item registration finished. Read game content from ClientEvents.Ready.");
            Items = Array.AsReadOnly(items);
            ApplyItemDefaults = applyDefaults;
            profile = saveProfile;
        }
    }

    public static int SizeNpcTables()
    {
        lock (itemTableLock)
        {
            npcTablesSized = true;
            return Npcs.Count;
        }
    }

    internal static void ConfigureNpcs(ClientNpcDefinition[] npcs, Action<int, object> applyDefaults)
    {
        lock (itemTableLock)
        {
            if (npcTablesSized)
                throw new InvalidOperationException("Terraria's NPC tables were initialized before custom NPC registration finished. Read game content from ClientEvents.Ready.");
            Npcs = Array.AsReadOnly(npcs);
            HasPersistentNpcs = npcs.Any(npc => npc.SaveSchema != 0);
            ApplyNpcDefaults = applyDefaults;
        }
    }

    public static int SizeProjectileTables()
    {
        lock (itemTableLock)
        {
            projectileTablesSized = true;
            return Projectiles.Count;
        }
    }

    internal static void ConfigureProjectiles(ClientProjectileDefinition[] projectiles, Action<int, object> applyDefaults)
    {
        lock (itemTableLock)
        {
            if (projectileTablesSized)
                throw new InvalidOperationException("Terraria's projectile tables were initialized before custom projectile registration finished. Read game content from ClientEvents.Ready.");
            Projectiles = Array.AsReadOnly(projectiles);
            ApplyProjectileDefaults = applyDefaults;
        }
    }

    public static string SaveRoot(string vanillaRoot) => profile == null
        ? vanillaRoot : Path.Combine(vanillaRoot, "AetherContent", profile);

    public static string WorldSaveRoot(string vanillaRoot) => WorldSaveIdentity is { } identity
        ? Path.Combine(vanillaRoot, "AetherContent", identity) : vanillaRoot;

    internal static void Reset()
    {
        lock (itemTableLock)
        {
            Items = Array.Empty<ClientItemDefinition>();
            ApplyItemDefaults = null;
            ItemUsed = null;
            ItemHitNpc = null;
            ItemEquipped = null;
            ApplyPlayerModifiers = null;
            Npcs = Array.Empty<ClientNpcDefinition>();
            HasPersistentNpcs = false;
            ApplyNpcDefaults = null;
            NpcSpawned = null;
            NpcUpdated = null;
            NpcHit = null;
            NpcHitPlayer = null;
            NpcRemoved = null;
            Projectiles = Array.Empty<ClientProjectileDefinition>();
            ApplyProjectileDefaults = null;
            ProjectileSpawned = null;
            ProjectileUpdated = null;
            ProjectileHitNpc = null;
            ProjectileHitPlayer = null;
            ProjectileRemoved = null;
            IsDedicatedServer = false;
            ConnectionVersion = null;
            ServerReady = null;
            ServerStopped = null;
            ServerPlayerJoined = null;
            FinalizeRegistration = null;
            SaveRequirements = Array.Empty<ContentModRequirement>();
            profile = null;
            itemTablesSized = false;
            npcTablesSized = false;
            projectileTablesSized = false;
        }
    }
}
