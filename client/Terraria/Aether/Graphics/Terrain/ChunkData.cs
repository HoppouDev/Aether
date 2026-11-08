using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;
using System.Runtime.InteropServices;

namespace Aether.Graphics.Terrain;

public readonly record struct ChunkKey(int X, int Y)
{
    public const int Size = 64;
    public static ChunkKey FromTile(int x, int y) => new(Floor(x), Floor(y));
    private static int Floor(int value) => (int)Math.Floor(value / (double)Size);
}
[Flags]
public enum TerrainDirtyLayers { None = 0, Solid = 1, NonSolid = 2, Walls = 4, All = 7 }
public readonly record struct ChunkVersion(long World, long Assets, long Revision);

// Own format: never upload the game's mutable pointer-backed Tile/TileData.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SnapshotTile(ushort Type, ushort Wall, short FrameX, short FrameY,
    ushort Header, byte Header1, byte Header2, byte Header3, byte Liquid, ushort Reserved = 0);

public sealed class ChunkSnapshot
{
    public const int Halo = 1;
    public const int Side = ChunkKey.Size + Halo * 2;
    public const int TileCount = Side * Side;
    private readonly SnapshotTile[] tiles;
    public ChunkKey Key { get; }
    public ChunkVersion Version { get; }
    public int Bytes => TileCount * 16;
    public SnapshotTile[] Tiles => tiles;
    public ulong Fingerprint { get; }
    public ChunkSnapshot(ChunkKey key, ChunkVersion version, SnapshotTile[] source)
    {
        if (source.Length != TileCount) throw new ArgumentException("Snapshot must include the complete chunk and one-tile halo");
        Key = key; Version = version; tiles = (SnapshotTile[])source.Clone();
        Fingerprint = Hash(tiles);
    }
    public static ulong Hash(SnapshotTile[] source)
    {
        // Whole-snapshot freshness checks remain owner-side and include every
        // packed field/halo byte. Hashes are transient, never a save/ABI format.
        unsafe
        {
            fixed (SnapshotTile* start = source)
            {
                // Two 64-bit words per packed tile, including the halo.
                ulong* words = (ulong*)start;
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < source.Length * 2; i++)
                    hash = unchecked((hash ^ words[i]) * 1099511628211UL);
                return hash;
            }
        }
    }
}
public sealed record PreparedChunk(ChunkKey Key, ChunkVersion Version, ulong Fingerprint, int ActiveTiles, int WallTiles, ChunkGeometry? Geometry = null, ChunkWallGeometry? Walls = null);
