using System.Security.Cryptography;
using System.Text.Json;
using Aether.Modding;
using Terraria;
using Terraria.ID;

namespace Aether.Api;

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
}

public static class ItemRegistry
{
    private static readonly Dictionary<string, ItemDefinition> entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> types = new(StringComparer.Ordinal);
    private static KeyValuePair<string, ItemDefinition>[] ordered = [];

    public static void Register(string id, ItemDefinition definition)
    {
        string owner = Registration.Owner();
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.TextureItemId is <= 0 or >= ItemID.VanillaCount ||
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

    public static Item Create(string id, int stack = 1)
    {
        if (!GameBridge.IsReady)
            throw new InvalidOperationException("Create items after ClientEvents.Ready.");
        if (!TryGetType(id, out int type))
            throw new KeyNotFoundException($"No item is registered as '{id}'.");
        int maxStack = ordered[type - ItemID.VanillaCount].Value.MaxStack;
        if (stack < 1 || stack > maxStack)
            throw new ArgumentOutOfRangeException(nameof(stack));
        return new Item(type) { stack = stack };
    }

    internal static void Freeze()
    {
        if (entries.Count == 0)
            return;
        ordered = entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var metadata = new ClientItemDefinition[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            var (id, definition) = ordered[index];
            types.Add(id, ItemID.VanillaCount + index);
            metadata[index] = new ClientItemDefinition(id, definition.Name, definition.Tooltip, definition.TextureItemId);
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
            item.useTime = item.useAnimation = definition.UseTime;
            item.useStyle = ItemUseStyleID.Swing;
            item.UseSound = SoundID.Item1;
        }
    }
}
