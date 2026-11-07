using System.Diagnostics.CodeAnalysis;
using Terraria;
using Terraria.IO;

namespace Aether.Api;

public enum ContainerSlotGroup
{
    Hotbar, Backpack, Coins, Ammo, Cursor, Trash, Armor, Vanity, Dye, Equipment, EquipmentDye,
    Storage, Guide, Reforge, Research, Display, Shop
}

public readonly record struct ContainerSlotId(ContainerSlotGroup Group, int Index = 0, int Loadout = -1);

internal readonly record struct SlotAddress(object Storage, int Index);

internal sealed record SlotLocation(SlotAddress Address, Func<Item> Read, Action<Item> Write)
{
    internal static SlotLocation At(Item[] items, int index) =>
        new(new SlotAddress(items, index), () => items[index], item => items[index] = item);
}

public abstract class ItemContainer
{
    private readonly WorldFileData world = Main.ActiveWorldFileData;
    private readonly long revision = GameBridge.WorldRevision;
    private readonly Dictionary<ContainerSlotId, ContainerSlot> slots = [];
    private IReadOnlyList<ContainerSlot>? view;

    internal Player Owner { get; }

    internal ItemContainer(Player owner) => Owner = owner;

    internal virtual bool IsOpen => true;
    internal virtual bool IsReadOnly => false;

    internal bool IsCurrent => GameBridge.WorldReady && revision == GameBridge.WorldRevision &&
        ReferenceEquals(world, Main.ActiveWorldFileData) && Players.TryGet(Owner.whoAmI, out var current) &&
        ReferenceEquals(Owner, current) && IsOpen;

    internal bool CanWrite => IsCurrent && !IsReadOnly && Main.netMode == 0 && Players.IsLocal(Owner) &&
        !Owner.dead && !Owner.ghost && !Owner.HasLockedInventory();

    public IReadOnlyList<ContainerSlot> Slots
    {
        get
        {
            RequireCurrent();
            return view ??= Array.AsReadOnly(slots.Values
                .OrderBy(slot => slot.Id.Group)
                .ThenBy(slot => slot.Id.Loadout)
                .ThenBy(slot => slot.Id.Index)
                .ToArray());
        }
    }

    public bool TryGetSlot(ContainerSlotId id, [NotNullWhen(true)] out ContainerSlot? slot)
    {
        RequireCurrent();
        return slots.TryGetValue(id, out slot);
    }

    public Item? Insert(Item item, IReadOnlyList<ContainerSlotId> targets)
    {
        RequireWritable();
        ContainerSlot.ValidateInput(item);
        ArgumentNullException.ThrowIfNull(targets);

        var selected = new List<(ContainerSlot Slot, SlotLocation Location)>();
        var seen = new HashSet<SlotAddress>();
        foreach (ContainerSlotId id in targets)
        {
            if (!slots.TryGetValue(id, out ContainerSlot? slot))
                throw new ArgumentException($"Unknown container slot '{id}'.", nameof(targets));
            SlotLocation location = slot.Location();
            if (seen.Add(location.Address))
                selected.Add((slot, location));
        }

        var changes = new SlotChanges();
        Item remainder = item.AetherDetachedCopy();
        foreach (var (slot, location) in selected.OrderBy(entry => entry.Location.Read().IsAir))
        {
            int count = slot.StageInsert(location, remainder, changes);
            remainder.stack -= count;
            if (remainder.stack == 0)
                break;
        }

        changes.Commit();
        return remainder.stack == 0 ? null : remainder;
    }

    internal void AddSlot(ContainerSlotId id, Func<SlotLocation> location, Func<bool>? readable = null,
        Func<bool>? insertable = null, Func<Item, SlotLocation, SlotChanges, bool>? accepts = null,
        bool keepFavorite = false, int capacity = int.MaxValue) =>
        slots.Add(id, new ContainerSlot(this, id, location, readable, insertable, accepts, keepFavorite, capacity));

    internal void RequireCurrent()
    {
        Registration.RequireGameThread();
        if (!IsCurrent)
            throw new InvalidOperationException("The container is unavailable in the current world.");
    }

    internal void RequireWritable()
    {
        RequireCurrent();
        if (!CanWrite)
            throw new InvalidOperationException("This container cannot be changed in the current context.");
    }
}

public sealed class ContainerSlot
{
    private readonly ItemContainer container;
    private readonly Func<bool>? readable;
    private readonly Func<bool>? insertable;
    private readonly Func<Item, SlotLocation, SlotChanges, bool>? accepts;
    private readonly bool keepFavorite;
    private readonly int capacity;

    public ContainerSlotId Id { get; }
    internal Func<SlotLocation> Location { get; }

    internal ContainerSlot(ItemContainer container, ContainerSlotId id, Func<SlotLocation> location, Func<bool>? readable,
        Func<bool>? insertable, Func<Item, SlotLocation, SlotChanges, bool>? accepts, bool keepFavorite, int capacity)
    {
        this.container = container;
        Id = id;
        Location = location;
        this.readable = readable;
        this.insertable = insertable;
        this.accepts = accepts;
        this.keepFavorite = keepFavorite;
        this.capacity = capacity;
    }

    public bool CanRead
    {
        get
        {
            Registration.RequireGameThread();
            return container.IsCurrent && (readable?.Invoke() ?? true);
        }
    }

    public bool CanInsert => CanRead && container.CanWrite && (insertable?.Invoke() ?? true);
    public bool CanExtract => CanRead && container.CanWrite;

    public Item? GetItem()
    {
        RequireReadable();
        return CopyOrNull(Location().Read());
    }

    public bool CanAccept(Item item)
    {
        ValidateInput(item);
        if (!CanInsert)
            return false;
        return StageInsert(Location(), item, new SlotChanges()) != 0;
    }

    public bool TryReplace(Item? item, out Item? previous)
    {
        RequireReadable();
        container.RequireWritable();
        if (item != null)
            ValidateInput(item);

        previous = null;
        SlotLocation location = Location();
        Item current = location.Read();
        var changes = new SlotChanges();
        Item replacement = Prepare(item);
        changes.Set(location, replacement);
        if (!Allows(location, replacement, changes))
            return false;
        previous = CopyOrNull(current);
        changes.Commit();
        return true;
    }

    public Item? Insert(Item item) => container.Insert(item, [Id]);

    public Item? Extract(int count)
    {
        RequireReadable();
        container.RequireWritable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        SlotLocation location = Location();
        Item current = location.Read();
        if (current.IsAir)
            return null;
        Item removed = CopyStack(current, Math.Min(count, current.stack));
        var changes = new SlotChanges();
        changes.Set(location, CopyStack(current, current.stack - removed.stack));
        changes.Commit();
        return removed;
    }

    public int MoveTo(ContainerSlot target, int count)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        RequireReadable();
        target.RequireReadable();
        container.RequireWritable();
        target.container.RequireWritable();

        SlotLocation source = Location();
        SlotLocation destination = target.Location();
        Item current = source.Read();
        if (source.Address == destination.Address || current.IsAir)
            return 0;

        var changes = new SlotChanges();
        int proposed = Math.Min(count, current.stack);
        changes.Set(source, CopyStack(current, current.stack - proposed));
        int moved = target.StageInsert(destination, CopyStack(current, proposed), changes);
        if (moved == 0)
            return 0;
        changes.Set(source, CopyStack(current, current.stack - moved));
        if (!target.Allows(destination, changes.Read(destination), changes))
            return 0;
        changes.Commit();
        return moved;
    }

    public bool TrySwapWith(ContainerSlot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireReadable();
        target.RequireReadable();
        container.RequireWritable();
        target.container.RequireWritable();

        SlotLocation source = Location();
        SlotLocation destination = target.Location();
        if (source.Address == destination.Address)
            return true;

        var changes = new SlotChanges();
        changes.Set(source, Prepare(destination.Read()));
        changes.Set(destination, target.Prepare(source.Read()));
        if (!Allows(source, changes.Read(source), changes) || !target.Allows(destination, changes.Read(destination), changes))
            return false;
        changes.Commit();
        return true;
    }

    internal int StageInsert(SlotLocation location, Item item, SlotChanges changes)
    {
        if (!CanInsert)
            return 0;
        Item current = changes.Read(location);
        if (!current.IsAir && !Item.CanStack(current, item))
            return 0;
        int existing = current.IsAir ? 0 : current.stack;
        Item result = Prepare(current.IsAir ? item : current);
        int moved = Math.Min(item.stack, Math.Min(capacity, result.maxStack) - existing);
        if (moved <= 0)
            return 0;
        result.stack = existing + moved;
        result.favorited = keepFavorite && (result.favorited || item.favorited);
        if (!Allows(location, result, changes))
            return 0;
        changes.Set(location, result);
        return moved;
    }

    private bool Allows(SlotLocation location, Item item, SlotChanges changes) =>
        item.IsAir || (CanInsert && item.stack <= Math.Min(capacity, item.maxStack) &&
            (accepts?.Invoke(item, location, changes) ?? true));

    private void RequireReadable()
    {
        container.RequireCurrent();
        if (!CanRead)
            throw new InvalidOperationException("The slot contents are unavailable.");
    }

    private Item Prepare(Item? item)
    {
        Item copy = CopyOrNull(item) ?? new Item();
        copy.favorited &= keepFavorite;
        return copy;
    }

    private static Item? CopyOrNull(Item? item) => item == null || item.IsAir ? null : item.AetherDetachedCopy();
    private static Item CopyStack(Item item, int count) =>
        count == 0 ? new Item() : CopyWithCount(item, count);

    private static Item CopyWithCount(Item item, int count)
    {
        Item copy = item.AetherDetachedCopy();
        copy.stack = count;
        return copy;
    }

    internal static void ValidateInput(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsAir || item.maxStack <= 0 || item.stack > item.maxStack ||
            !ItemRegistry.TryGetKey(item.type, out _))
            throw new ArgumentException("Provide a valid nonempty item stack.", nameof(item));
    }
}

internal sealed class SlotChanges
{
    private readonly Dictionary<SlotAddress, (SlotLocation Location, Item Item)> changes = [];

    internal Item Read(SlotLocation slot) => changes.TryGetValue(slot.Address, out var staged) ? staged.Item : slot.Read();

    internal Item[] EquipmentWithChanges(Item[] armor, int excluded)
    {
        var projected = (Item[])armor.Clone();
        foreach (var (address, change) in changes)
            if (ReferenceEquals(address.Storage, armor))
                projected[address.Index] = change.Item;
        projected[excluded] = new Item();
        return projected;
    }

    internal void Set(SlotLocation slot, Item item) => changes[slot.Address] = (slot, item);

    internal void Commit()
    {
        foreach (var (slot, item) in changes.Values)
            slot.Write(item);
        if (changes.Count != 0 && Main.playerInventory)
            Recipe.UpdateRecipeList();
    }
}
