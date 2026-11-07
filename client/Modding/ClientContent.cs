using System.ComponentModel;

namespace Aether.Modding;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record ClientItemDefinition(string Id, string Name, string? Tooltip, int TextureItemId);

[EditorBrowsable(EditorBrowsableState.Never)]
public static class ClientContent
{
    private static readonly object itemTableLock = new();
    private static bool itemTablesSized;
    private static string? profile;

    public static IReadOnlyList<ClientItemDefinition> Items { get; private set; } = Array.Empty<ClientItemDefinition>();
    public static Action<int, object>? ApplyItemDefaults { get; private set; }
    public static Action<object, object>? ItemUsed { get; internal set; }
    public static Action<object, object, object, int>? ItemHitNpc { get; internal set; }
    public static Action<object, object, int>? ItemEquipped { get; internal set; }
    public static bool HasItems => Items.Count != 0;
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

    public static string SaveRoot(string vanillaRoot) => profile == null
        ? vanillaRoot : Path.Combine(vanillaRoot, "AetherContent", profile);

    internal static void Reset()
    {
        lock (itemTableLock)
        {
            Items = Array.Empty<ClientItemDefinition>();
            ApplyItemDefaults = null;
            ItemUsed = null;
            ItemHitNpc = null;
            ItemEquipped = null;
            FinalizeRegistration = null;
            profile = null;
            itemTablesSized = false;
        }
    }
}
