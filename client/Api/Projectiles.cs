using Aether.Modding;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Aether.Api;

public enum ProjectileTargets
{
    None,
    NpcEnemies,
    Players
}

public readonly record struct ProjectileBehaviorContext(ProjectileSnapshot Projectile, EntityBehaviorState State);
public readonly record struct ProjectileSpawnContext(ProjectileBehaviorContext Behavior, IEntitySource Source);
public readonly record struct ProjectileNpcHitContext(ProjectileBehaviorContext Behavior, NpcSnapshot Target, int Damage, bool Critical);
public readonly record struct ProjectilePlayerHitContext(ProjectileBehaviorContext Behavior, Player Player, int Damage);
public readonly record struct ProjectileRemovalContext(ProjectileBehaviorContext Behavior, EntityRemovalReason Reason);

public sealed record ProjectileDefinition(string Name, EntitySprite Sprite)
{
    public int Width { get; init; } = 12;
    public int Height { get; init; } = 12;
    public int Lifetime { get; init; } = 180;
    public int Penetration { get; init; } = 1;
    public bool CollidesWithTiles { get; init; } = true;
    public ProjectileTargets Targets { get; init; }
    public Action<ProjectileSpawnContext>? OnSpawn { get; init; }
    public Action<ProjectileBehaviorContext>? OnTick { get; init; }
    public Action<ProjectileNpcHitContext>? OnHitNpc { get; init; }
    public Action<ProjectilePlayerHitContext>? OnHitPlayer { get; init; }
    public Action<ProjectileRemovalContext>? OnRemoved { get; init; }
}

public static class ProjectileRegistry
{
    private sealed record RegistrationEntry(ProjectileDefinition Definition, string Owner);
    private sealed record ProjectileCallbacks(string Subject, Listener<ProjectileSpawnContext>? Spawn,
        Listener<ProjectileBehaviorContext>? Tick, Listener<ProjectileNpcHitContext>? HitNpc,
        Listener<ProjectilePlayerHitContext>? HitPlayer, Listener<ProjectileRemovalContext>? Removed);
    private sealed record Instance(ProjectileHandle Handle, ProjectileTypeKey Type, ProjectileCallbacks Callbacks,
        EntityBehaviorState State);

    private static readonly Dictionary<string, RegistrationEntry> entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> types = new(StringComparer.Ordinal);
    private static readonly Dictionary<Projectile, Instance> live = [];
    private static KeyValuePair<string, RegistrationEntry>[] ordered = [];
    private static ProjectileCallbacks[] callbacks = [];

    public static void Register(string id, ProjectileDefinition definition)
    {
        string owner = Registration.Owner();
        Registration.RequireContentId(id);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Sprite == null ||
            definition.Width is < 1 or > 512 || definition.Height is < 1 or > 512 ||
            definition.Lifetime is < 1 or > 36_000 || definition.Penetration is 0 or < -1 or > 100 ||
            !Enum.IsDefined(definition.Targets))
            throw new ArgumentException($"Invalid projectile definition for '{id}'.", nameof(definition));
        if (entries.Count >= 1024)
            throw new InvalidOperationException("The custom projectile limit is 1,024 definitions.");
        Registration.Add(entries, id, new RegistrationEntry(definition, owner), owner);
    }

    public static bool TryGetType(string id, out int type) => types.TryGetValue(id, out type);

    public static string? GetId(int type)
    {
        int index = type - ProjectileID.VanillaCount;
        return (uint)index < (uint)ordered.Length ? ordered[index].Key : null;
    }

    public static bool TryResolve(ProjectileTypeKey key, out int type)
    {
        type = key.VanillaId ?? 0;
        return type != 0 || key.CustomId is string id && TryGetType(id, out type);
    }

    public static bool TryGetKey(int type, out ProjectileTypeKey key)
    {
        if (type is > 0 and < ProjectileID.VanillaCount)
        {
            key = ProjectileTypeKey.Vanilla(type);
            return true;
        }
        if (GetId(type) is { } id)
        {
            key = ProjectileTypeKey.Custom(id);
            return true;
        }
        key = default;
        return false;
    }

    internal static void Freeze()
    {
        if (entries.Count == 0)
            return;
        ordered = entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var metadata = new ClientProjectileDefinition[ordered.Length];
        callbacks = new ProjectileCallbacks[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            var (id, entry) = ordered[index];
            var definition = entry.Definition;
            types.Add(id, ProjectileID.VanillaCount + index);
            metadata[index] = new(id, definition.Name, definition.Sprite.Resolve(id, entry.Owner, ProjectileID.VanillaCount));
            callbacks[index] = new($"projectile '{id}'", Listen(definition.OnSpawn, entry.Owner),
                Listen(definition.OnTick, entry.Owner), Listen(definition.OnHitNpc, entry.Owner),
                Listen(definition.OnHitPlayer, entry.Owner), Listen(definition.OnRemoved, entry.Owner));
        }
        ClientContent.ConfigureProjectiles(metadata, (type, projectile) => ApplyDefaults(type, (Projectile)projectile));
        ClientContent.ProjectileSpawned = (projectile, source) => OnSpawn((Projectile)projectile, (IEntitySource)source);
        ClientContent.ProjectileUpdated = projectile => OnTick((Projectile)projectile);
        ClientContent.ProjectileHitNpc = (projectile, npc, damage, critical) =>
            OnHitNpc((Projectile)projectile, (NPC)npc, damage, critical);
        ClientContent.ProjectileHitPlayer = (projectile, player, damage) =>
            OnHitPlayer((Projectile)projectile, (Player)player, damage);
        ClientContent.ProjectileRemoved = (projectile, reason) =>
        {
            if (Registration.IsGameThread)
                OnRemoved((Projectile)projectile, reason);
            else
                Main.QueueMainThreadAction(() => OnRemoved((Projectile)projectile, EntityRemovalReason.WorldUnloaded));
        };
    }

    internal static void EndWorld()
    {
        foreach (Projectile projectile in live.Keys.ToArray())
        {
            projectile.AetherRetire(EntityRemovalReason.WorldUnloaded);
            OnRemoved(projectile, EntityRemovalReason.WorldUnloaded);
        }
    }

    internal static bool IsManaged(Projectile projectile) => live.ContainsKey(projectile);

    private static Listener<T>? Listen<T>(Action<T>? action, string owner) => action == null ? null : new(owner, action);

    private static void ApplyDefaults(int type, Projectile projectile)
    {
        var definition = ordered[type - ProjectileID.VanillaCount].Value.Definition;
        projectile.width = definition.Width;
        projectile.height = definition.Height;
        projectile.timeLeft = definition.Lifetime;
        projectile.penetrate = definition.Penetration;
        projectile.tileCollide = definition.CollidesWithTiles;
        projectile.friendly = definition.Targets == ProjectileTargets.NpcEnemies;
        projectile.hostile = definition.Targets == ProjectileTargets.Players;
        projectile.npcProj = projectile.hostile;
        projectile.noEnchantments = true;
        projectile.noEnchantmentVisuals = true;
    }

    private static void OnSpawn(Projectile projectile, IEntitySource source)
    {
        if (!Registration.IsGameThread || !GameBridge.AuthoritativeWorld)
            return;
        var behavior = callbacks[projectile.type - ProjectileID.VanillaCount];
        var handle = new ProjectileHandle(projectile.whoAmI, GameBridge.WorldRevision, projectile.AetherSpawnIdentity);
        var instance = new Instance(handle, ProjectileTypeKey.Custom(GetId(projectile.type)!), behavior, new EntityBehaviorState());
        live.Add(projectile, instance);
        if (behavior.Spawn?.TryInvoke(new(Context(projectile, instance), source), behavior.Subject) == false)
            projectile.AetherKill(EntityRemovalReason.BehaviorFailed);
    }

    private static void OnTick(Projectile projectile)
    {
        if (live.TryGetValue(projectile, out var instance) && instance.Callbacks.Tick is { } tick &&
            !tick.TryInvoke(Context(projectile, instance), instance.Callbacks.Subject))
            projectile.AetherKill(EntityRemovalReason.BehaviorFailed);
    }

    private static void OnHitNpc(Projectile projectile, NPC npc, int damage, bool critical)
    {
        if (live.TryGetValue(projectile, out var instance) && instance.Callbacks.HitNpc is { } hit &&
            NpcRegistry.TryGetKey(npc.netID, out var type))
        {
            var handle = new NpcHandle(npc.whoAmI, GameBridge.WorldRevision, npc.AetherSpawnIdentity);
            hit.TryInvoke(new(Context(projectile, instance), Entities.SnapshotNpc(npc, handle, type), damage, critical),
                instance.Callbacks.Subject);
        }
    }

    private static void OnHitPlayer(Projectile projectile, Player player, int damage)
    {
        if (live.TryGetValue(projectile, out var instance) && instance.Callbacks.HitPlayer is { } hit)
            hit.TryInvoke(new(Context(projectile, instance), player, damage), instance.Callbacks.Subject);
    }

    private static void OnRemoved(Projectile projectile, EntityRemovalReason reason)
    {
        if (live.Remove(projectile, out var instance) && instance.Callbacks.Removed is { } removed)
            removed.TryInvoke(new(Context(projectile, instance), reason), instance.Callbacks.Subject);
    }

    private static ProjectileBehaviorContext Context(Projectile projectile, Instance instance) =>
        new(Entities.SnapshotProjectile(projectile, instance.Handle, instance.Type), instance.State);
}
