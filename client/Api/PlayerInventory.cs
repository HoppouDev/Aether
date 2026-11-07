using System.Diagnostics.CodeAnalysis;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace Aether.Api;

public sealed class PlayerInventory : ItemContainer
{
    internal PlayerInventory(Player player) : base(player)
    {
        AddInventoryGroup(ContainerSlotGroup.Hotbar, 0, 10);
        AddInventoryGroup(ContainerSlotGroup.Backpack, 10, 40);
        AddInventoryGroup(ContainerSlotGroup.Coins, 50, 4, item => item.IsACoin);
        AddInventoryGroup(ContainerSlotGroup.Ammo, 54, 4, item => item.FitsAmmoSlot());
        AddSlot(new(ContainerSlotGroup.Cursor), CursorLocation, () => CanReadSlot(PlayerItemSlotID.InventoryMouseItem),
            keepFavorite: true);
        AddSlot(new(ContainerSlotGroup.Trash), () => new(new SlotAddress(Owner, PlayerItemSlotID.TrashItem),
            () => Owner.trashItem, item => Owner.trashItem = item), () => CanReadSlot(PlayerItemSlotID.TrashItem));

        for (int loadout = 0; loadout < player.Loadouts.Length; loadout++)
        {
            AddLoadoutGroup(ContainerSlotGroup.Armor, loadout, 0, player.armor.Length / 2);
            AddLoadoutGroup(ContainerSlotGroup.Vanity, loadout, player.armor.Length / 2, player.armor.Length / 2);
            AddLoadoutGroup(ContainerSlotGroup.Dye, loadout, 0, player.dye.Length);
        }

        for (int index = 0; index < player.miscEquips.Length; index++)
        {
            int slot = index;
            AddSlot(new(ContainerSlotGroup.Equipment, slot), () => SlotLocation.At(Owner.miscEquips, slot),
                () => CanReadSlot(PlayerItemSlotID.Misc0 + slot),
                accepts: (item, _, _) => AcceptsEquipment(slot, item), capacity: 1);
            AddSlot(new(ContainerSlotGroup.EquipmentDye, slot), () => SlotLocation.At(Owner.miscDyes, slot),
                () => CanReadSlot(PlayerItemSlotID.MiscDye0 + slot),
                accepts: (item, _, _) => item.dye > 0, capacity: 1);
        }
    }

    public ContainerSlot? SelectedSlot
    {
        get
        {
            RequireCurrent();
            int index = Owner.selectedItem;
            ContainerSlotId id = index switch
            {
                >= 0 and < 10 => new(ContainerSlotGroup.Hotbar, index),
                >= 10 and < 50 => new(ContainerSlotGroup.Backpack, index - 10),
                >= 50 and < 54 => new(ContainerSlotGroup.Coins, index - 50),
                >= 54 and < 58 => new(ContainerSlotGroup.Ammo, index - 54),
                58 => new(ContainerSlotGroup.Cursor),
                _ => new(ContainerSlotGroup.Backpack, -1)
            };
            return TryGetSlot(id, out ContainerSlot? slot) && slot.CanRead ? slot : null;
        }
    }

    public Item? Insert(Item item)
    {
        ContainerSlotId[] targets = Slots
            .Where(slot => slot.Id.Group is ContainerSlotGroup.Hotbar or ContainerSlotGroup.Backpack)
            .Select(slot => slot.Id)
            .ToArray();
        return Insert(item, targets);
    }

    public bool TryGetEffectiveEquipmentSlot(ContainerSlotGroup group, int index, [NotNullWhen(true)] out ContainerSlot? slot)
    {
        RequireCurrent();
        slot = null;
        if (!Players.IsLocal(Owner) || group is not (ContainerSlotGroup.Armor or ContainerSlotGroup.Vanity or ContainerSlotGroup.Dye) ||
            (uint)index >= (uint)Owner.dye.Length)
            return false;
        int? shared;
        if (group == ContainerSlotGroup.Dye)
            Owner.GetEffectiveDye(index, out shared);
        else
            Owner.GetEffectiveArmor(index + (group == ContainerSlotGroup.Vanity ? Owner.armor.Length / 2 : 0), out shared);
        return TryGetSlot(new ContainerSlotId(group, index, shared ?? Owner.CurrentLoadoutIndex), out slot);
    }

    internal bool CanReadSlot(int netSlot) => Players.IsLocal(Owner) || Owner.AetherHasObservedSlot(netSlot);

    private SlotLocation CursorLocation() => Players.IsLocal(Owner)
        ? new(new SlotAddress(Owner.inventory, 58), () => Main.mouseItem, item =>
        {
            Owner.inventory[58] = item.AetherDetachedCopy();
            Main.mouseItem = item;
        })
        : SlotLocation.At(Owner.inventory, 58);

    private void AddInventoryGroup(ContainerSlotGroup group, int start, int count, Func<Item, bool>? accepts = null)
    {
        for (int index = 0; index < count; index++)
        {
            int slot = start + index;
            AddSlot(new(group, index), () => SlotLocation.At(Owner.inventory, slot),
                () => CanReadSlot(PlayerItemSlotID.Inventory0 + slot), () => !Owner.inventoryChestStack[slot],
                accepts == null ? null : (item, _, _) => accepts(item), keepFavorite: true);
        }
    }

    private void AddLoadoutGroup(ContainerSlotGroup group, int loadout, int start, int count)
    {
        bool dye = group == ContainerSlotGroup.Dye;
        for (int index = 0; index < count; index++)
        {
            int slot = start + index;
            AddSlot(new(group, index, loadout), () => SlotLocation.At(Equipment(loadout, dye), slot),
                () => CanReadSlot(EquipmentNetSlot(loadout, dye) + slot),
                () => Owner.IsItemSlotUnlockedAndUsable(slot),
                (item, location, changes) => dye ? item.dye > 0 :
                    ItemSlot.CanEquipInArmorSlot(changes.EquipmentWithChanges((Item[])location.Address.Storage, slot), item, slot),
                keepFavorite: true, capacity: 1);
        }
    }

    private Item[] Equipment(int loadout, bool dye)
    {
        if (loadout == Owner.CurrentLoadoutIndex)
            return dye ? Owner.dye : Owner.armor;
        EquipmentLoadout equipment = Owner.Loadouts[loadout];
        return dye ? equipment.Dye : equipment.Armor;
    }

    private int EquipmentNetSlot(int loadout, bool dye)
    {
        if (loadout == Owner.CurrentLoadoutIndex)
            return dye ? PlayerItemSlotID.Dye0 : PlayerItemSlotID.Armor0;
        return (loadout, dye) switch
        {
            (0, false) => PlayerItemSlotID.Loadout1_Armor_0,
            (0, true) => PlayerItemSlotID.Loadout1_Dye_0,
            (1, false) => PlayerItemSlotID.Loadout2_Armor_0,
            (1, true) => PlayerItemSlotID.Loadout2_Dye_0,
            (2, false) => PlayerItemSlotID.Loadout3_Armor_0,
            _ => PlayerItemSlotID.Loadout3_Dye_0
        };
    }

    private static bool AcceptsEquipment(int slot, Item item) => slot switch
    {
        0 => (uint)item.buffType < (uint)Main.vanityPet.Length && Main.vanityPet[item.buffType] && !Main.lightPet[item.buffType],
        1 => (uint)item.buffType < (uint)Main.lightPet.Length && Main.lightPet[item.buffType],
        2 => (uint)item.mountType < (uint)MountID.Sets.Cart.Length && MountID.Sets.Cart[item.mountType],
        3 => (uint)item.mountType < (uint)MountID.Sets.Cart.Length && !MountID.Sets.Cart[item.mountType],
        4 => (uint)item.shoot < (uint)Main.projHook.Length && Main.projHook[item.shoot],
        _ => false
    };
}

public static partial class Players
{
    public static PlayerInventory GetInventory(Player player)
    {
        RequireActive(player);
        return new PlayerInventory(player);
    }
}
