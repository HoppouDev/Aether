using Terraria;

namespace Aether.Api;

public readonly record struct TilePosition(int X, int Y);

public enum TileQueryStatus
{
    WorldUnavailable,
    OutOfBounds,
    Unloaded,
    Available
}

public enum TileLiquid : byte
{
    Water,
    Lava,
    Honey,
    Shimmer
}

[Flags]
public enum TileWires
{
    None = 0,
    Red = 1,
    Blue = 2,
    Green = 4,
    Yellow = 8
}

public readonly record struct TileSnapshot(int? BlockType, int? WallType, TileLiquid? Liquid, byte LiquidAmount,
    short FrameX, short FrameY, byte Slope, bool IsHalfBlock, bool HasActuator, bool IsActuated, TileWires Wires);

public readonly record struct TileQueryResult(TileQueryStatus Status, TileSnapshot? Tile);

public static partial class Tiles
{
    public static TileQueryResult Query(TilePosition position)
    {
        Registration.RequireGameThread();
        if (!GameBridge.WorldReady)
            return new(TileQueryStatus.WorldUnavailable, null);
        if ((uint)position.X >= (uint)Main.maxTilesX || (uint)position.Y >= (uint)Main.maxTilesY)
            return new(TileQueryStatus.OutOfBounds, null);
        if (Main.netMode == 1 && !(Main.sectionManager?.TileLoaded(position.X, position.Y) ?? false))
            return new(TileQueryStatus.Unloaded, null);

        Tile? tile = Main.tile[position.X, position.Y];
        if (tile == null)
            return new(TileQueryStatus.Available, default(TileSnapshot));
        TileWires wires = TileWires.None;
        if (tile.wire()) wires |= TileWires.Red;
        if (tile.wire2()) wires |= TileWires.Blue;
        if (tile.wire3()) wires |= TileWires.Green;
        if (tile.wire4()) wires |= TileWires.Yellow;
        var snapshot = new TileSnapshot(tile.active() ? tile.type : null, tile.wall != 0 ? tile.wall : null,
            tile.liquid != 0 ? (TileLiquid)tile.liquidType() : null, tile.liquid, tile.frameX, tile.frameY,
            tile.slope(), tile.halfBrick(), tile.actuator(), tile.inActive(), wires);
        return new(TileQueryStatus.Available, snapshot);
    }
}
