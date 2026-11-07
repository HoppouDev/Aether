using System.Text;
using System.Text.Json;
using Aether.Modding;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Aether.Api;

public readonly record struct NpcBehaviorContext(NpcSnapshot Npc, EntityBehaviorState State);
public readonly record struct NpcSpawnContext(NpcBehaviorContext Behavior, IEntitySource Source);
public readonly record struct NpcHitContext(NpcBehaviorContext Behavior, int? AttackerSlot, int Damage, bool Critical);
public readonly record struct NpcPlayerHitContext(NpcBehaviorContext Behavior, Player Player, int Damage);
public readonly record struct NpcRemovalContext(NpcBehaviorContext Behavior, EntityRemovalReason Reason);
public readonly record struct NpcRestoredContext(NpcBehaviorContext Behavior, int SchemaVersion, JsonElement? SavedState);

public sealed record NpcPersistence(int SchemaVersion)
{
    public Action<NpcRestoredContext>? OnRestored { get; init; }
}

public sealed record NpcDefinition(string Name, EntitySprite Sprite)
{
    public int Width { get; init; } = 24;
    public int Height { get; init; } = 24;
    public int MaxHealth { get; init; } = 50;
    public int Damage { get; init; } = 10;
    public int Defense { get; init; }
    public float KnockbackResistance { get; init; } = 1f;
    public bool HasGravity { get; init; } = true;
    public bool CollidesWithTiles { get; init; } = true;
    public NpcPersistence? Persistence { get; init; }
    public Action<NpcSpawnContext>? OnSpawn { get; init; }
    public Action<NpcBehaviorContext>? OnTick { get; init; }
    public Action<NpcHitContext>? OnHit { get; init; }
    public Action<NpcPlayerHitContext>? OnHitPlayer { get; init; }
    public Action<NpcRemovalContext>? OnRemoved { get; init; }
}

public static class NpcRegistry
{
    private sealed record RegistrationEntry(NpcDefinition Definition, string Owner);
    private sealed record NpcCallbacks(string Subject, Listener<NpcSpawnContext>? Spawn,
        Listener<NpcBehaviorContext>? Tick, Listener<NpcHitContext>? Hit,
        Listener<NpcPlayerHitContext>? HitPlayer, Listener<NpcRemovalContext>? Removed,
        Listener<NpcRestoredContext>? Restored);
    private sealed record Instance(NpcHandle Handle, NpcTypeKey Type, NpcCallbacks Callbacks, EntityBehaviorState State);

    private static readonly Dictionary<string, RegistrationEntry> entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> types = new(StringComparer.Ordinal);
    private static readonly Dictionary<NPC, Instance> live = [];
    private static KeyValuePair<string, RegistrationEntry>[] ordered = [];
    private static NpcCallbacks[] callbacks = [];

    public static void Register(string id, NpcDefinition definition)
    {
        string owner = Registration.Owner();
        Registration.RequireContentId(id);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Sprite == null ||
            definition.Width is < 1 or > 512 || definition.Height is < 1 or > 512 ||
            definition.MaxHealth is < 1 or > 1_000_000 ||
            definition.Damage is < 0 or > short.MaxValue || definition.Defense < 0 ||
            !float.IsFinite(definition.KnockbackResistance) || definition.KnockbackResistance is < 0 or > 1 ||
            definition.Persistence is { SchemaVersion: <= 0 })
            throw new ArgumentException($"Invalid NPC definition for '{id}'.", nameof(definition));
        if (entries.Count >= 1024)
            throw new InvalidOperationException("The custom NPC limit is 1,024 definitions.");
        Registration.Add(entries, id, new RegistrationEntry(definition, owner), owner);
    }

    public static bool TryGetType(string id, out int type) => types.TryGetValue(id, out type);

    public static string? GetId(int type)
    {
        int index = type - NPCID.VanillaCount;
        return (uint)index < (uint)ordered.Length ? ordered[index].Key : null;
    }

    public static bool TryResolve(NpcTypeKey key, out int type)
    {
        type = key.VanillaId ?? 0;
        return type != 0 || key.CustomId is string id && TryGetType(id, out type);
    }

    public static bool TryGetKey(int type, out NpcTypeKey key)
    {
        if (type != 0 && type > NPCID.NegativeIDCount && type < NPCID.VanillaCount)
        {
            key = NpcTypeKey.Vanilla(type);
            return true;
        }
        if (GetId(type) is { } id)
        {
            key = NpcTypeKey.Custom(id);
            return true;
        }
        key = default;
        return false;
    }

    public static JsonElement? GetSavedState(NpcHandle handle) => DecodeState(RequirePersistent(handle).AetherSavedState);

    public static void SetSavedState(NpcHandle handle, JsonElement? state)
    {
        NPC npc = RequirePersistent(handle);
        string? json = state?.GetRawText();
        if (json != null && Encoding.UTF8.GetByteCount(json) > 16 * 1024)
            throw new ArgumentException("NPC state cannot exceed 16 KiB of JSON.", nameof(state));
        if (json != null)
        {
            using var document = JsonDocument.Parse(json);
        }
        npc.AetherSavedState = json;
    }

    private static NPC RequirePersistent(NpcHandle handle)
    {
        Registration.RequireGameThread();
        if (!GameBridge.AuthoritativeWorld || !Entities.TryGetNpc(handle, out _) ||
            Main.npc[handle.Slot] is not { } npc || !IsManaged(npc) ||
            ClientContent.Npcs[npc.type - NPCID.VanillaCount].SaveSchema == 0)
            throw new InvalidOperationException("Saved state belongs to a live persistent custom NPC on the simulation owner.");
        return npc;
    }

    private static JsonElement? DecodeState(string? json)
    {
        if (json == null)
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static void Freeze()
    {
        if (entries.Count == 0)
            return;
        ordered = entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var metadata = new ClientNpcDefinition[ordered.Length];
        callbacks = new NpcCallbacks[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            var (id, entry) = ordered[index];
            var definition = entry.Definition;
            types.Add(id, NPCID.VanillaCount + index);
            metadata[index] = new(id, definition.Name, definition.Sprite.Resolve(id, entry.Owner, NPCID.VanillaCount),
                definition.Persistence?.SchemaVersion ?? 0);
            callbacks[index] = new($"NPC '{id}'", Listen(definition.OnSpawn, entry.Owner), Listen(definition.OnTick, entry.Owner),
                Listen(definition.OnHit, entry.Owner), Listen(definition.OnHitPlayer, entry.Owner),
                Listen(definition.OnRemoved, entry.Owner), Listen(definition.Persistence?.OnRestored, entry.Owner));
        }
        ClientContent.ConfigureNpcs(metadata, (type, npc) => ApplyDefaults(type, (NPC)npc));
        ClientContent.NpcSpawned = (npc, source) => OnSpawn((NPC)npc, (IEntitySource)source);
        ClientContent.NpcUpdated = npc => OnTick((NPC)npc);
        ClientContent.NpcHit = (npc, owner, damage, critical) => OnHit((NPC)npc, owner, damage, critical);
        ClientContent.NpcHitPlayer = (npc, player, damage) => OnHitPlayer((NPC)npc, (Player)player, damage);
        ClientContent.NpcRemoved = (npc, reason) =>
        {
            if (Registration.IsGameThread)
                OnRemoved((NPC)npc, reason);
            else
                Main.QueueMainThreadAction(() => OnRemoved((NPC)npc, EntityRemovalReason.WorldUnloaded));
        };
    }

    internal static void EndWorld()
    {
        foreach (NPC npc in live.Keys.ToArray())
        {
            npc.AetherRetire(EntityRemovalReason.WorldUnloaded);
            OnRemoved(npc, EntityRemovalReason.WorldUnloaded);
        }
    }

    internal static bool IsManaged(NPC npc) => live.ContainsKey(npc);

    internal static void PublishRestored()
    {
        if (!ClientContent.HasPersistentNpcs || !GameBridge.AuthoritativeWorld)
            return;
        var pending = new List<NPC>();
        for (int slot = 0; slot < Main.maxNPCs; slot++)
        {
            NPC npc = Main.npc[slot];
            if (!npc.active || !npc.AetherPendingRestore)
                continue;
            npc.AetherPendingRestore = false;
            Attach(npc);
            pending.Add(npc);
        }
        foreach (NPC npc in pending)
            if (live.TryGetValue(npc, out var instance))
                instance.Callbacks.Restored?.TryInvoke(new(Context(npc, instance),
                    ClientContent.Npcs[npc.type - NPCID.VanillaCount].SaveSchema, DecodeState(npc.AetherSavedState)),
                    instance.Callbacks.Subject);
    }

    private static Listener<T>? Listen<T>(Action<T>? action, string owner) => action == null ? null : new(owner, action);

    private static void ApplyDefaults(int type, NPC npc)
    {
        var definition = ordered[type - NPCID.VanillaCount].Value.Definition;
        npc.width = definition.Width;
        npc.height = definition.Height;
        npc.lifeMax = definition.MaxHealth;
        npc.damage = definition.Damage;
        npc.defense = definition.Defense;
        npc.knockBackResist = definition.KnockbackResistance;
        npc.noGravity = !definition.HasGravity;
        npc.noTileCollide = !definition.CollidesWithTiles;
        npc.aiStyle = 0;
    }

    private static void OnSpawn(NPC npc, IEntitySource source)
    {
        if (!Registration.IsGameThread || !GameBridge.AuthoritativeWorld)
            return;
        Instance instance = Attach(npc);
        var behavior = instance.Callbacks;
        if (behavior.Spawn?.TryInvoke(new(Context(npc, instance), source), behavior.Subject) == false)
            npc.AetherRetire(EntityRemovalReason.BehaviorFailed);
    }

    private static Instance Attach(NPC npc)
    {
        var behavior = callbacks[npc.type - NPCID.VanillaCount];
        var handle = new NpcHandle(npc.whoAmI, GameBridge.WorldRevision, npc.AetherSpawnIdentity);
        var instance = new Instance(handle, NpcTypeKey.Custom(GetId(npc.type)!), behavior, new EntityBehaviorState());
        live.Add(npc, instance);
        return instance;
    }

    private static void OnTick(NPC npc)
    {
        if (live.TryGetValue(npc, out var instance) && instance.Callbacks.Tick is { } tick &&
            !tick.TryInvoke(Context(npc, instance), instance.Callbacks.Subject))
            npc.AetherRetire(EntityRemovalReason.BehaviorFailed);
    }

    private static void OnHit(NPC npc, int owner, int damage, bool critical)
    {
        if (live.TryGetValue(npc, out var instance) && instance.Callbacks.Hit is { } hit)
            hit.TryInvoke(new(Context(npc, instance), (uint)owner < Main.maxPlayers ? owner : null, damage, critical),
                instance.Callbacks.Subject);
    }

    private static void OnHitPlayer(NPC npc, Player player, int damage)
    {
        if (live.TryGetValue(npc, out var instance) && instance.Callbacks.HitPlayer is { } hit)
            hit.TryInvoke(new(Context(npc, instance), player, damage), instance.Callbacks.Subject);
    }

    private static void OnRemoved(NPC npc, EntityRemovalReason reason)
    {
        if (live.Remove(npc, out var instance) && instance.Callbacks.Removed is { } removed)
            removed.TryInvoke(new(Context(npc, instance), reason), instance.Callbacks.Subject);
    }

    private static NpcBehaviorContext Context(NPC npc, Instance instance) =>
        new(Entities.SnapshotNpc(npc, instance.Handle, instance.Type), instance.State);
}
