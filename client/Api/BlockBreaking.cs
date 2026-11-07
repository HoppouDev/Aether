using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.ObjectData;

namespace Aether.Api;

public readonly record struct BlockBreakOptions(bool SuppressBlockDrop);

public enum BlockBreakabilityStatus
{
    Ineligible,
    Unsupported,
    Eligible
}

public enum BlockBreakOutcome
{
    Rejected,
    Removed,
    Changed,
    PartialHit
}

public enum BlockBreakReason
{
    None,
    WorldUnavailable,
    UnsupportedContext,
    WorldBoundary,
    Unloaded,
    Empty,
    Protected,
    NonemptyContainer,
    UnsupportedBlock,
    UnsupportedDropBehavior,
    IncompleteObject,
    Unchanged,
    PlayerUnavailable,
    BuildingDisabled,
    UnsuitableTool,
    OutOfReach,
    Cooldown,
    InsufficientPickaxePower
}

public readonly record struct BlockBreakability(BlockBreakabilityStatus Status, BlockBreakReason Reason, Rectangle? Footprint);

public readonly record struct BlockBreakResult(BlockBreakOutcome Outcome, BlockBreakReason Reason,
    TileSnapshot? Before, TileSnapshot? After, Rectangle? Footprint, bool TileEntityRemoved, int MiningDamage = 0);

public static partial class Tiles
{
    public static BlockBreakability GetBlockBreakability(TilePosition position, BlockBreakOptions options = default)
    {
        Registration.RequireGameThread();
        return CheckBlockHit(position, options);
    }

    public static BlockBreakResult BreakBlock(TilePosition position, BlockBreakOptions options = default)
    {
        Registration.RequireGameThread();
        var check = CheckBlockHit(position, options);
        var before = Query(position).Tile;
        if (check.Status != BlockBreakabilityStatus.Eligible)
            return Rejected(check.Reason, before, check.Footprint);
        return ApplyBlockHit(position, check.Footprint!.Value, before, options);
    }

    public static BlockBreakResult MineBlock(Player player, TilePosition position)
    {
        Registration.RequireGameThread();
        ArgumentNullException.ThrowIfNull(player);
        var before = Query(position).Tile;
        if (Main.netMode != 0)
            return Rejected(BlockBreakReason.UnsupportedContext, before);
        if (!GameBridge.WorldReady)
            return Rejected(BlockBreakReason.WorldUnavailable, before);
        if (!Players.TryGetLocal(out var local) || !ReferenceEquals(player, local) || player.dead || player.ghost)
            return Rejected(BlockBreakReason.PlayerUnavailable, before);
        if (player.noBuilding)
            return Rejected(BlockBreakReason.BuildingDisabled, before);

        Item tool = player.HeldItem;
        if (tool.IsAir || tool.pick <= 0 || !player.IsAllowedToHoldItems)
            return Rejected(BlockBreakReason.UnsuitableTool, before);
        if (!player.ItemTimeIsZero || player.toolTime > 0 || player.reuseDelay > 0)
            return Rejected(BlockBreakReason.Cooldown, before);

        var check = CheckBlockHit(position, default, mining: true);
        if (check.Status != BlockBreakabilityStatus.Eligible)
            return Rejected(check.Reason, before, check.Footprint);
        var bounds = check.Footprint!.Value;
        if (!player.IsInTileInteractionRange(position.X, position.Y, TileReachCheckSettings.Simple, tool.tileBoost))
            return Rejected(BlockBreakReason.OutOfReach, before, bounds);
        int type = before!.Value.BlockType!.Value;
        if (Main.tileAxe[type] || Main.tileHammer[type] || player.AetherGetPickaxeDamage(position.X, position.Y, tool.pick) <= 0)
            return Rejected(BlockBreakReason.InsufficientPickaxePower, before, bounds);

        return ApplyBlockHit(position, bounds, before, default, player);
    }

    private static BlockBreakResult ApplyBlockHit(TilePosition position, Rectangle bounds, TileSnapshot? before,
        BlockBreakOptions options, Player? miner = null)
    {
        var anchor = new Point16(bounds.X, bounds.Y);
        TileEntity.ByPosition.TryGetValue(anchor, out var entity);
        int stack = DisplayedStack(entity);
        int damage = 0;
        if (miner == null)
        {
            WorldGen.KillTile(bounds.X, bounds.Y, noItem: options.SuppressBlockDrop);
        }
        else
        {
            Item tool = miner.HeldItem;
            miner.IntentionGuesser.Track(miner, position.X, position.Y, GuessedPlayerIntention.HarvestTreasure);
            miner.PickTile(position.X, position.Y, tool.pick);
            miner.itemTime = Math.Max(1, (int)(tool.useTime * miner.pickSpeed));
            int slot = miner.hitTile.TryFinding(position.X, position.Y, 1);
            damage = miner.hitTile.AddDamage(slot, 0, updateAmount: false);
        }

        var after = Query(position).Tile;
        TileEntity.ByPosition.TryGetValue(anchor, out var remaining);
        bool entityRemoved = entity != null && !ReferenceEquals(entity, remaining);
        var outcome = after?.BlockType == null ? BlockBreakOutcome.Removed
            : before?.BlockType != after?.BlockType || entityRemoved || stack != DisplayedStack(remaining) ? BlockBreakOutcome.Changed
            : damage > 0 ? BlockBreakOutcome.PartialHit
            : before != after ? BlockBreakOutcome.Changed
            : BlockBreakOutcome.Rejected;
        var reason = outcome == BlockBreakOutcome.Rejected ? BlockBreakReason.Unchanged : BlockBreakReason.None;
        return new(outcome, reason, before, after, bounds, entityRemoved, damage);
    }

    private static BlockBreakability CheckBlockHit(TilePosition position, BlockBreakOptions options, bool mining = false)
    {
        var query = Query(position);
        if (query.Status == TileQueryStatus.WorldUnavailable)
            return Ineligible(BlockBreakReason.WorldUnavailable);
        if (query.Status == TileQueryStatus.Unloaded)
            return Ineligible(BlockBreakReason.Unloaded);
        if (Main.netMode != 0)
            return Unsupported(BlockBreakReason.UnsupportedContext);
        if (!WorldGen.InWorld(position.X, position.Y, 2))
            return Ineligible(BlockBreakReason.WorldBoundary);
        if (query.Tile?.BlockType is not int type)
            return Ineligible(BlockBreakReason.Empty);

        bool simple = type is TileID.Dirt or TileID.Stone or TileID.WoodBlock or TileID.ClayBlock;
        bool terrain = simple || type is TileID.Grass or TileID.CorruptGrass or TileID.HallowedGrass or TileID.CrimsonGrass
            or TileID.JungleGrass or TileID.MushroomGrass or TileID.Mud or TileID.SnowBlock or TileID.IceBlock or TileID.Sand;
        terrain |= mining && !Main.tileFrameImportant[type] && type != TileID.MysticSnakeRope;
        bool chest = type is TileID.Containers or TileID.Containers2 or TileID.Dressers;
        bool display = type is TileID.ItemFrame or TileID.WeaponsRack2 or TileID.FoodPlatter or TileID.DisplayDoll or TileID.HatRack;
        if (!terrain && !chest && !display)
            return Unsupported(BlockBreakReason.UnsupportedBlock);
        if (options.SuppressBlockDrop && !simple)
            return Unsupported(BlockBreakReason.UnsupportedDropBehavior);

        var bounds = new Rectangle(position.X, position.Y, 1, 1);
        if (!terrain && !TileObjectData.TryGetTileBounds(position.X, position.Y, out bounds))
            return Unsupported(BlockBreakReason.IncompleteObject);
        if (!WorldGen.InWorld(bounds.X, bounds.Y, 2) || !WorldGen.InWorld(bounds.Right - 1, bounds.Bottom - 1, 2))
            return Ineligible(BlockBreakReason.WorldBoundary, bounds);
        if (chest && !Chest.CanDestroyChest(bounds.X, bounds.Y))
            return Ineligible(BlockBreakReason.NonemptyContainer, bounds);

        Tile origin = Main.tile[bounds.X, bounds.Y];
        var layout = terrain ? null : TileObjectData.GetTileData(origin);
        if (!terrain && (layout == null || origin.frameX < 0 || origin.frameY < 0))
            return Unsupported(BlockBreakReason.IncompleteObject, bounds);
        for (int x = bounds.X; x < bounds.Right; x++)
        {
            for (int y = bounds.Y; y < bounds.Bottom; y++)
            {
                Tile? tile = Main.tile[x, y];
                if (tile == null || !tile.active() || tile.type != type ||
                    (layout != null && (tile.frameX < 0 || tile.frameY < 0 ||
                        tile.frameX / layout.CoordinateFullWidth != origin.frameX / layout.CoordinateFullWidth ||
                        tile.frameY / layout.CoordinateFullHeight != origin.frameY / layout.CoordinateFullHeight ||
                        !TileObjectData.TryGetTileBounds(x, y, out var part) || part != bounds)))
                    return Unsupported(BlockBreakReason.IncompleteObject, bounds);
                if (FixExploitManEaters.SpotProtected(x, y) || !WorldGen.CanKillTile(x, y) ||
                    WorldGen.AetherCheckTileBreakability(x, y) != 0)
                    return Ineligible(BlockBreakReason.Protected, bounds);
            }
        }

        if ((type == TileID.DisplayDoll && !TEDisplayDoll.IsBreakable(bounds.X, bounds.Y)) ||
            (type == TileID.HatRack && !TEHatRack.IsBreakable(bounds.X, bounds.Y)))
            return Ineligible(BlockBreakReason.NonemptyContainer, bounds);

        return new(BlockBreakabilityStatus.Eligible, BlockBreakReason.None, bounds);
    }

    private static int DisplayedStack(TileEntity? entity) => entity switch
    {
        TEItemFrame frame => frame.item.stack,
        TEWeaponsRack rack => rack.item.stack,
        TEFoodPlatter platter => platter.item.stack,
        _ => 0
    };

    private static BlockBreakability Ineligible(BlockBreakReason reason, Rectangle? footprint = null) =>
        new(BlockBreakabilityStatus.Ineligible, reason, footprint);

    private static BlockBreakability Unsupported(BlockBreakReason reason, Rectangle? footprint = null) =>
        new(BlockBreakabilityStatus.Unsupported, reason, footprint);

    private static BlockBreakResult Rejected(BlockBreakReason reason, TileSnapshot? tile, Rectangle? footprint = null) =>
        new(BlockBreakOutcome.Rejected, reason, tile, tile, footprint, false);
}
