using System.Diagnostics.CodeAnalysis;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.UI;

namespace Aether.Api;

public enum PlayerBank
{
    PiggyBank,
    Safe,
    DefendersForge,
    VoidVault
}

public static class Containers
{
    public static bool TryGetOpen(Player player, [NotNullWhen(true)] out ItemContainer? container)
    {
        Players.RequireActive(player);
        container = null;
        int openChest = player.chest;
        if (openChest is >= -5 and <= -2)
        {
            long revision = player.AetherChestRevision;
            PlayerBank bank = (PlayerBank)(-openChest - 2);
            container = CreateBank(player, bank, () => player.AetherChestRevision == revision);
            return true;
        }
        if ((uint)openChest < (uint)Main.chest.Length && Main.chest[openChest] is Chest chest)
        {
            long revision = player.AetherChestRevision;
            long capacityRevision = chest.AetherCapacityRevision;
            container = CreateStorage(player, chest,
                () => player.AetherChestRevision == revision && chest.AetherCapacityRevision == capacityRevision &&
                    ReferenceEquals(Main.chest[openChest], chest),
                slot => Main.netMode != 1 || chest.AetherHasObservedSlot(slot),
                readOnly: () => Chest.IsLocked(chest.x, chest.y));
            return true;
        }
        if (player.tileEntityAnchor.GetTileEntity() is TileEntity entity)
        {
            long revision = player.tileEntityAnchor.AetherRevision;
            return TryCreateDisplay(player, entity, out container, () => player.tileEntityAnchor.AetherRevision == revision &&
                ReferenceEquals(player.tileEntityAnchor.GetTileEntity(), entity));
        }
        return false;
    }

    public static bool TryGetDisplay(Player player, Point16 position, [NotNullWhen(true)] out ItemContainer? container)
    {
        Players.RequireActive(player);
        container = null;
        return WorldGen.InWorld(position.X, position.Y, 0) &&
            TileEntity.TryGetAt(position.X, position.Y, out TileEntity entity) &&
            TryCreateDisplay(player, entity, out container);
    }

    public static bool TryGetOpenShop(Player player, [NotNullWhen(true)] out ItemContainer? container)
    {
        Players.RequireActive(player);
        container = null;
        int shop = Main.npcShop;
        int merchant = player.talkNPC;
        if (!Players.IsLocal(player) || !Main.playerInventory || shop <= 0 ||
            shop >= Main.instance.shop.Length || Main.instance.shop[shop] is not Chest stock ||
            (uint)merchant >= Main.maxNPCs || !Main.npc[merchant].active)
            return false;
        NPC npc = Main.npc[merchant];
        long revision = Main.AetherShopRevision;
        var view = new ContainerView(player, () => Main.playerInventory && Main.AetherShopRevision == revision &&
            player.talkNPC == merchant && npc.active && ReferenceEquals(Main.npc[merchant], npc) &&
            ReferenceEquals(Main.instance.shop[shop], stock), () => true);
        for (int index = 0; index < stock.maxItems; index++)
        {
            int slot = index;
            view.AddSlot(new(ContainerSlotGroup.Shop, slot), () => SlotLocation.At(stock.item, slot));
        }
        container = view;
        return true;
    }

    internal static ItemContainer CreateBank(Player player, PlayerBank bank, Func<bool>? isOpen = null)
    {
        (Chest storage, int netSlot) = bank switch
        {
            PlayerBank.PiggyBank => (player.bank, PlayerItemSlotID.Bank1_0),
            PlayerBank.Safe => (player.bank2, PlayerItemSlotID.Bank2_0),
            PlayerBank.DefendersForge => (player.bank3, PlayerItemSlotID.Bank3_0),
            PlayerBank.VoidVault => (player.bank4, PlayerItemSlotID.Bank4_0),
            _ => throw new ArgumentOutOfRangeException(nameof(bank))
        };
        return CreateStorage(player, storage, isOpen, slot => Players.IsLocal(player) || player.AetherHasObservedSlot(netSlot + slot),
            accepts: item => !ChestUI.IsBlockedFromTransferIntoChest(item, storage.item), keepFavorite: bank == PlayerBank.VoidVault);
    }

    private static ItemContainer CreateStorage(Player player, Chest chest, Func<bool>? isOpen, Func<int, bool> readable,
        Func<Item, bool>? accepts = null, bool keepFavorite = false, Func<bool>? readOnly = null)
    {
        var view = new ContainerView(player, isOpen, readOnly);
        for (int index = 0; index < chest.maxItems; index++)
        {
            int slot = index;
            view.AddSlot(new(ContainerSlotGroup.Storage, slot), () => SlotLocation.At(chest.item, slot),
                () => readable(slot), accepts: accepts == null ? null : (item, _, _) => accepts(item), keepFavorite: keepFavorite);
        }
        return view;
    }

    private static bool TryCreateDisplay(Player player, TileEntity entity, [NotNullWhen(true)] out ItemContainer? container, Func<bool>? isOpen = null)
    {
        var view = new ContainerView(player, () => TileEntity.TryGet<TileEntity>(entity.ID, out var current) &&
            ReferenceEquals(current, entity) && (isOpen?.Invoke() ?? true));
        container = view;
        switch (entity)
        {
            case TEDisplayDoll doll:
                AddDoll(view, doll);
                return true;
            case TEHatRack rack:
                var (hats, dyes) = rack.AetherDisplaySlots();
                AddDisplayArray(view, hats, 0, (item, _, _) => item.headSlot > 0);
                AddDisplayArray(view, dyes, hats.Length, (item, _, _) => item.dye > 0);
                return true;
            case TEItemFrame frame:
                AddDisplayItem(view, frame, () => frame.item, item => frame.item = item, TEItemFrame.FitsItemFrame);
                return true;
            case TEWeaponsRack weapons:
                AddDisplayItem(view, weapons, () => weapons.item, item => weapons.item = item, TEWeaponsRack.FitsWeaponFrame);
                return true;
            case TEFoodPlatter food:
                AddDisplayItem(view, food, () => food.item, item => food.item = item, TEFoodPlatter.FitsFoodPlatter);
                return true;
            case TEDeadCellsDisplayJar jar:
                AddDisplayItem(view, jar, () => jar.item, item => jar.item = item, TEDeadCellsDisplayJar.FitsJar);
                return true;
            default:
                container = null;
                return false;
        }
    }

    private static void AddDoll(ContainerView view, TEDisplayDoll doll)
    {
        var (equipment, dyes, held) = doll.AetherDisplaySlots();
        AddDisplayArray(view, equipment, 0, (item, location, changes) => location.Address.Index switch
        {
            0 => item.headSlot > 0,
            1 => item.bodySlot > 0,
            2 => item.legSlot > 0,
            8 => item.mountType != -1,
            int slot => item.accessory && ItemSlot.CanEquipAccessoryInSlot(item, true,
                new ArraySegment<Item>(changes.EquipmentWithChanges(equipment, slot), 3, 5), slot)
        });
        AddDisplayArray(view, dyes, equipment.Length, (item, _, _) => item.dye > 0);
        AddDisplayArray(view, held, equipment.Length + dyes.Length, (item, _, _) => TEDisplayDoll.AcceptedInWeaponSlot(item));
    }

    private static void AddDisplayArray(ContainerView view, Item[] items, int offset,
        Func<Item, SlotLocation, SlotChanges, bool> accepts)
    {
        for (int index = 0; index < items.Length; index++)
        {
            int slot = index;
            view.AddSlot(new(ContainerSlotGroup.Display, offset + slot), () => SlotLocation.At(items, slot),
                accepts: accepts, capacity: 1);
        }
    }

    private static void AddDisplayItem(ContainerView view, TileEntity entity, Func<Item> read, Action<Item> write,
        Func<Item, bool> accepts) =>
        view.AddSlot(new(ContainerSlotGroup.Display), () => new SlotLocation(new SlotAddress(entity, 0), read, write),
            accepts: (item, _, _) => accepts(item), capacity: 1);

    private sealed class ContainerView(Player owner, Func<bool>? isOpen, Func<bool>? readOnly = null) : ItemContainer(owner)
    {
        internal override bool IsOpen => isOpen?.Invoke() ?? true;
        internal override bool IsReadOnly => readOnly?.Invoke() ?? false;
    }
}
