using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Net;

namespace Aether.Modding;

internal static class ItemWire
{
    internal const int MaxStateBytes = 16 * 1024;

    internal static bool IsCustom(int type) => type >= ItemID.VanillaCount;
    internal static bool IsReadOnly(Item item) => Main.netMode == 1 && IsCustom(item.type);

    internal static Item.NewItemModifier CopyStateToDrop(Item item) => item.AetherState is { } state
        ? spawned => spawned.inner.AetherState = state : null;

    internal static void WriteState(BinaryWriter writer, Item item)
    {
        if (Main.netMode != 2 || !IsCustom(item.type) || item.IsAir)
            return;
        byte[] bytes = item.AetherState == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(item.AetherState);
        if (bytes.Length > MaxStateBytes)
            throw new InvalidDataException("Custom item state exceeds the network limit.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    internal static string ReadState(BinaryReader reader, int type, long end)
    {
        if (Main.netMode != 1 || !IsCustom(type))
            return null;
        if (type - ItemID.VanillaCount >= ClientContent.Items.Count || reader.BaseStream.Position + sizeof(ushort) > end)
            throw new InvalidDataException("Invalid custom item type or missing state.");
        int size = reader.ReadUInt16();
        if (size > MaxStateBytes || reader.BaseStream.Position + size > end)
            throw new InvalidDataException("Invalid custom item state length.");
        byte[] bytes = reader.ReadBytes(size);
        if (bytes.Length != size)
            throw new EndOfStreamException();
        return size == 0 ? null : Encoding.UTF8.GetString(bytes);
    }

    internal static void RestoreLocalInventory(Player player)
    {
        if (!ClientContent.HasItems)
            return;
        lock (player)
        {
            for (int index = 0; index < PlayerItemSlotID.Count; index++)
            {
                var slot = new PlayerItemSlotID.SlotReference(player, index);
                if (slot.IsValid && slot.Item.AetherLocalItem is Item original)
                    slot.Item = original;
            }
            Main.mouseItem = Main.mouseItem.LocalSaveItem;
        }
    }

    internal static bool HasCustomItems(TileEntity entity) =>
        entity.DisplayItems().Any(item => !item.IsAir && IsCustom(item.type));

    internal static bool CanReplaceDisplay(int x, int y, int type) =>
        !IsCustom(type) && (!ClientContent.HasItems || !TileEntity.TryGetAt<TileEntity>(x, y, out var entity) || !HasCustomItems(entity));
}

internal sealed class DisplayItemStateModule : NetModule
{
    internal static void SendForPacket(int client, int message, int number, int y, int width, int height)
    {
        if (!ClientContent.HasItems)
            return;
        if (message == MessageID.TileEntitySharing && TileEntity.TryGet<TileEntity>(number, out var entity))
        {
            Send(client, entity);
        }
        else if (message == MessageID.TileSection)
        {
            var area = new Rectangle(number, y, width, height);
            foreach (TileEntity display in TileEntity.ByID.Values)
            {
                if (area.Contains(display.Position.X, display.Position.Y))
                    Send(client, display);
            }
        }
    }

    private static void Send(int client, TileEntity entity)
    {
        int index = 0;
        foreach (Item item in entity.DisplayItems())
        {
            if (!item.IsAir && ItemWire.IsCustom(item.type) && item.AetherState != null)
            {
                NetPacket packet = CreatePacket<DisplayItemStateModule>(7 + sizeof(ushort) + ItemWire.MaxStateBytes);
                packet.Writer.Write(entity.ID);
                packet.Writer.Write((byte)index);
                packet.Writer.Write((short)item.type);
                ItemWire.WriteState(packet.Writer, item);
                NetManager.Instance.SendToClient(packet, client);
            }
            index++;
        }
    }

    public override bool Deserialize(BinaryReader reader, int userId)
    {
        if (Main.netMode != 1 || !ClientContent.HasItems)
            return false;
        int id = reader.ReadInt32();
        int index = reader.ReadByte();
        int type = reader.ReadInt16();
        if (!TileEntity.TryGet<TileEntity>(id, out var entity) ||
            entity.DisplayItems().ElementAtOrDefault(index) is not Item item || item.type != type)
            return false;
        item.AetherState = ItemWire.ReadState(reader, type, reader.BaseStream.Length);
        return true;
    }
}
