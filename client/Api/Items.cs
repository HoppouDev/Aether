using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.Modding;
using Terraria;
using Terraria.ID;

namespace Aether.Api;

public readonly record struct ItemUseContext(Player Player, Item Item);
public readonly record struct ItemHitNpcContext(Player Player, Item Item, NPC Target, int Damage);
public readonly record struct ItemEquipContext(Player Player, Item Item, int Slot);

public sealed record ItemDefinition(string Name, int TextureItemId)
{
    public string? Tooltip { get; init; }
    public int Width { get; init; } = 20;
    public int Height { get; init; } = 20;
    public int MaxStack { get; init; } = 1;
    public int Value { get; init; }
    public int Damage { get; init; }
    public int Defense { get; init; }
    public int UseTime { get; init; } = 20;
    public float Knockback { get; init; }
    public bool Accessory { get; init; }
    public Action<ItemUseContext>? OnUse { get; init; }
    public Action<ItemHitNpcContext>? OnHitNpc { get; init; }
    public Action<ItemEquipContext>? OnEquippedTick { get; init; }
}

public static class ItemRegistry
{
    private readonly record struct Callbacks(Listener<ItemUseContext>? Use, Listener<ItemHitNpcContext>? HitNpc,
        Listener<ItemEquipContext>? EquippedTick);

    private static readonly Dictionary<string, ItemDefinition> entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> types = new(StringComparer.Ordinal);
    private static KeyValuePair<string, ItemDefinition>[] ordered = [];
    private static Callbacks[] callbacks = [];

    public static void Register(string id, ItemDefinition definition)
    {
        string owner = Registration.Owner();
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(definition);
        if (id.Length > 1024 || string.IsNullOrWhiteSpace(definition.Name) || definition.TextureItemId is <= 0 or >= ItemID.VanillaCount ||
            definition.Width is < 1 or > 512 || definition.Height is < 1 or > 512 ||
            definition.MaxStack is < 1 or > 9999 || definition.Value < 0 || definition.Damage < 0 ||
            definition.Defense < 0 || definition.UseTime < 1 || !float.IsFinite(definition.Knockback) ||
            definition.Knockback < 0 || (definition.Accessory && definition.Damage > 0))
            throw new ArgumentException($"Invalid item definition for '{id}'.", nameof(definition));
        if (entries.Count >= 1024)
            throw new InvalidOperationException("The custom item limit is 1,024 definitions.");
        Registration.Add(entries, id, definition, owner);
    }

    public static bool TryGetType(string id, out int type) => types.TryGetValue(id, out type);

    public static string? GetId(int type)
    {
        int index = type - ItemID.VanillaCount;
        return (uint)index < (uint)ordered.Length ? ordered[index].Key : null;
    }

    public static bool TryGetKey(int type, out ItemKey key)
    {
        if (type is > 0 and < ItemID.VanillaCount)
        {
            key = ItemKey.Vanilla(type);
            return true;
        }
        if (GetId(type) is string id)
        {
            key = ItemKey.Custom(id);
            return true;
        }
        key = default;
        return false;
    }

    public static bool TryResolve(ItemKey key, out int type)
    {
        if (key.VanillaId is int id)
        {
            type = id;
            return true;
        }
        type = 0;
        return key.CustomId is string customId && TryGetType(customId, out type);
    }

    public static Item Create(string id, int stack = 1)
    {
        if (!GameBridge.IsReady)
            throw new InvalidOperationException("Create items after ClientEvents.Ready or ServerEvents.Ready.");
        if (!TryGetType(id, out int type))
            throw new KeyNotFoundException($"No item is registered as '{id}'.");
        int maxStack = ordered[type - ItemID.VanillaCount].Value.MaxStack;
        if (stack < 1 || stack > maxStack)
            throw new ArgumentOutOfRangeException(nameof(stack));
        return new Item(type) { stack = stack };
    }

    public static Item Create(ItemKey key, int stack = 1)
    {
        if (key.CustomId is string customId)
            return Create(customId, stack);
        if (!GameBridge.IsReady)
            throw new InvalidOperationException("Create items after ClientEvents.Ready or ServerEvents.Ready.");
        if (!TryResolve(key, out int type))
            throw new KeyNotFoundException($"No item is registered as '{key}'.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stack);
        var item = new Item(type);
        if (item.IsAir)
            throw new KeyNotFoundException($"No item is available as '{key}'.");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stack, item.maxStack);
        item.stack = stack;
        return item;
    }

    public static void SetState(Item item, JsonElement? state)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Main.netMode == 1)
            throw new InvalidOperationException("Custom item state is owned by the server in multiplayer.");
        if (GetId(item.type) == null || item.IsAir)
            throw new ArgumentException("State belongs to a custom item instance.", nameof(item));
        string? json = state?.GetRawText();
        if (json != null)
        {
            if (Encoding.UTF8.GetByteCount(json) > 16 * 1024)
                throw new ArgumentException("Item state cannot exceed 16 KiB of JSON.", nameof(state));
            try
            {
                using var document = JsonDocument.Parse(json);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Item state must be valid JSON with at most 64 levels of nesting.", nameof(state), exception);
            }
        }
        item.AetherState = json;
    }

    public static JsonElement? GetState(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.AetherState is not { } json)
            return null;
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static void Freeze()
    {
        if (entries.Count == 0)
            return;
        ordered = entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var metadata = new ClientItemDefinition[ordered.Length];
        callbacks = new Callbacks[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            var (id, definition) = ordered[index];
            types.Add(id, ItemID.VanillaCount + index);
            metadata[index] = new ClientItemDefinition(id, definition.Name, definition.Tooltip, definition.TextureItemId);
            string owner = id[..id.IndexOf(':')];
            callbacks[index] = new Callbacks(
                definition.OnUse == null ? null : new Listener<ItemUseContext>(owner, definition.OnUse),
                definition.OnHitNpc == null ? null : new Listener<ItemHitNpcContext>(owner, definition.OnHitNpc),
                definition.OnEquippedTick == null ? null : new Listener<ItemEquipContext>(owner, definition.OnEquippedTick));
        }
        var versions = ModCatalog.LoadedMods.ToDictionary(mod => mod.Id, mod => mod.Version, StringComparer.Ordinal);
        byte[] mapping = JsonSerializer.SerializeToUtf8Bytes(new
        {
            gameVersion = typeof(Item).Assembly.GetName().Version?.ToString(),
            firstType = ItemID.VanillaCount,
            items = ordered.Select(entry => new
            {
                id = entry.Key,
                version = versions[entry.Key[..entry.Key.IndexOf(':')]]
            })
        });
        string profile = Convert.ToHexStringLower(SHA256.HashData(mapping));
        ClientContent.ConfigureItems(metadata, ApplyDefaults, profile);
        ClientContent.ItemUsed = (player, item) => NotifyUse((Player)player, (Item)item);
        ClientContent.ItemHitNpc = (player, item, npc, damage) => NotifyHitNpc((Player)player, (Item)item, (NPC)npc, damage);
        ClientContent.ItemEquipped = (player, item, slot) => NotifyEquipped((Player)player, (Item)item, slot);
    }

    private static void ApplyDefaults(int index, object target)
    {
        var item = (Item)target;
        ItemDefinition definition = ordered[index].Value;
        item.width = definition.Width;
        item.height = definition.Height;
        item.maxStack = definition.MaxStack;
        item.value = definition.Value;
        item.defense = definition.Defense;
        item.accessory = definition.Accessory;
        if (definition.Damage > 0)
        {
            item.melee = true;
            item.damage = definition.Damage;
            item.knockBack = definition.Knockback;
            item.UseSound = SoundID.Item1;
        }
        if (definition.Damage > 0 || definition.OnUse != null)
        {
            item.useTime = item.useAnimation = definition.UseTime;
            item.useStyle = ItemUseStyleID.Swing;
        }
    }

    private static Callbacks GetCallbacks(Player player, Item item)
    {
        int index = item.type - ItemID.VanillaCount;
        return (uint)index < (uint)callbacks.Length && !Main.gameMenu && Main.netMode == 0 && player == Main.LocalPlayer
            ? callbacks[index] : default;
    }

    private static void NotifyUse(Player player, Item item) =>
        GetCallbacks(player, item).Use?.Invoke(new ItemUseContext(player, item));

    private static void NotifyHitNpc(Player player, Item item, NPC npc, int damage)
    {
        if (damage > 0)
            GetCallbacks(player, item).HitNpc?.Invoke(new ItemHitNpcContext(player, item, npc, damage));
    }

    private static void NotifyEquipped(Player player, Item item, int slot) =>
        GetCallbacks(player, item).EquippedTick?.Invoke(new ItemEquipContext(player, item, slot));
}
