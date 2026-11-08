using Terraria;
using System.Threading;
using System;
using Microsoft.Xna.Framework;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace Aether.Graphics.Terrain;

// Coverage is deliberately strict: allowlisted ordinary blocks. The
// compatibility renderer still owns every unsupported shape/material/behavior.
// This worker format contains texture-pixel UVs, independent of atlas placement.
public sealed class ChunkGeometry
{
    public const int RecordVersion = 3;
    public static readonly int MaximumBytes = ChunkKey.Size * ChunkKey.Size * (6 * 32 + 3) + ChunkMaterials.Count * 64 + 1024;
    private readonly TerrainVertex[][] vertices;
    private readonly byte[] covered;
    private readonly ushort[] starts;
    private readonly Vector2[] textureExtents;
    private readonly int[] usedMaterials;
    public int[] UsedMaterials => usedMaterials;
    public bool Packed { get; }
    public int TileCount { get; }
    public int Bytes { get; }
    public Vector2 TextureExtent(int material) => textureExtents[material];
    public int VertexCount(int material) => vertices[material].Length * (Packed ? 6 : 1);
    public TerrainVertex[] Vertices(int material)
    { if(Packed)throw new InvalidOperationException("Packed records require compute expansion");return vertices[material]; }
    private TerrainVertex Anchor(int material,int first) => vertices[material][Packed?first/6:first];
    public bool Covers(int localX, int localY) => localX >= 0 && localX < 64 && localY >= 0 && localY < 64 && covered[localX * 64 + localY] != 0;
    private ChunkGeometry(TerrainVertex[][] vertices, byte[] covered, ushort[] starts, Vector2[] textureExtents, int tileCount,bool packed)
    {
        this.vertices = vertices; this.covered = covered; this.starts = starts; this.textureExtents = textureExtents; TileCount = tileCount; Packed=packed;
        int bytes = covered.Length + starts.Length * 2 + 256 + ChunkMaterials.Count * 64;
        var used = new System.Collections.Generic.List<int>();
        for (int i = 0; i < vertices.Length; i++)
        { bytes += vertices[i].Length * 32; if (vertices[i].Length != 0) used.Add(i); }
        usedMaterials = used.ToArray(); Bytes = bytes + usedMaterials.Length * 4;
    }
    public Vector2 TileUV(int localX, int localY)
    {
        if (!Covers(localX, localY)) throw new ArgumentOutOfRangeException();
        int index = localX * 64 + localY; return Anchor(covered[index]-1,starts[index]).TextureUV;
    }
    public int Shape(int localX,int localY)
    {
        if(!Covers(localX,localY))return 0;
        int index=localX*64+localY;return (int)(Anchor(covered[index]-1,starts[index]).Color>>24);
    }
    public bool Fullbright(int localX,int localY)
    {
        if(!Covers(localX,localY))return false;
        int index=localX*64+localY;return (Anchor(covered[index]-1,starts[index]).Color&256)!=0;
    }
    public bool Matches(int localX, int localY, int material, int frameX, int frameY)
    {
        if (!Covers(localX, localY)) return false;
        int index = localX * 64 + localY;
        int slot = covered[index] - 1;
        if (ChunkMaterials.TileType(slot) != material) return false;
        var uv = Anchor(slot,starts[index]).TextureUV;
        return uv.X == frameX && uv.Y == frameY;
    }
    public bool MatchesPlain(int localX,int localY,int type,int frameX,int frameY)
    {
        if (!Matches(localX,localY,type,frameX,frameY)) return false;
        int index=localX*64+localY;
        return Anchor(covered[index]-1,starts[index]).Color == (uint)ChunkTileShape.Normal << 24;
    }
    // Owner-thread upload borrows the immutable array only; it must never modify it.
    internal TerrainVertex[] UploadData(int material) => vertices[material];
    public static ChunkGeometry Build(ChunkSnapshot snapshot, CancellationToken token,bool packed=false)
    {
        var tiles = snapshot.Tiles;
        byte[] covered = new byte[64 * 64];
        ushort[] starts = new ushort[64 * 64];
        int[] counts = new int[ChunkMaterials.Count];
        Vector2[] extents = new Vector2[ChunkMaterials.Count];
        for (int x = 1; x <= 64; x++)
        {
            token.ThrowIfCancellationRequested();
            for (int y = 1; y <= 64; y++)
            {
                int index = x * ChunkSnapshot.Side + y;
                var tile = tiles[index];
                // Require active, non-actuated terrain. Paint/coating metadata
                // stays separate from geometry; owner-side visibility still runs.
                int material = ChunkMaterials.Index(tile.Type);
                int shape=ChunkTileShape.FromSnapshot(tile,tiles[index+1]);
                bool ordinary = material >= 0 && shape!=0 && (tile.Header & 0x0860) == 0x20 &&
                    tile.FrameX >= 0 && tile.FrameY >= 0 && tile.FrameX % 18 == 0 && tile.FrameY % 18 == 0 &&
                    (shape!=ChunkTileShape.Normal || ((tiles[index - ChunkSnapshot.Side].Header & 0x400) == 0 &&
                    (tiles[index + ChunkSnapshot.Side].Header & 0x400) == 0));
                if (!ordinary) continue;
                covered[(x - 1) * 64 + y - 1] = (byte)(material + 1); counts[material]++;
                extents[material] = Vector2.Max(extents[material], new Vector2(tile.FrameX + 16, tile.FrameY + 16));
                if(shape==ChunkTileShape.HalfEdge)extents[material]=Vector2.Max(extents[material],new Vector2(160,70));
            }
        }
        TerrainVertex[][] result = new TerrainVertex[ChunkMaterials.Count][];
        int total = 0;
        for (int i = 0; i < result.Length; i++) { result[i] = counts[i] == 0 ? new TerrainVertex[0] : new TerrainVertex[counts[i] * (packed?1:6)]; total += counts[i]; }
        int[] offsets = new int[ChunkMaterials.Count];
        for (int x = 0; x < 64; x++)
        {
            token.ThrowIfCancellationRequested();
            for (int y = 0; y < 64; y++)
            {
                if (covered[x * 64 + y] == 0) continue;
                var tile = tiles[(x + 1) * ChunkSnapshot.Side + y + 1];
                int worldX = snapshot.Key.X * 64 + x, worldY = snapshot.Key.Y * 64 + y;
                Vector2 position = new(worldX * 16, worldY * 16), uv = new(tile.FrameX, tile.FrameY), light = new(worldX, worldY);
                int material = covered[x * 64 + y] - 1;
                var output = result[material]; int at = offsets[material]; starts[x * 64 + y] = (ushort)at;
                uint descriptor=(uint)(tile.Header&31)|((tile.Header3&128)!=0?256u:0u)|((uint)ChunkTileShape.FromSnapshot(tile,tiles[(x+1)*ChunkSnapshot.Side+y+2])<<24);
                if(packed)
                {output[at/6]=new(position,uv,light,descriptor,16);offsets[material]+=6;continue;}
                output[at] = new(position, uv, light, descriptor);
                output[at + 1] = new(position + new Vector2(16, 0), uv + new Vector2(16, 0), light, descriptor);
                output[at + 2] = new(position + new Vector2(16, 16), uv + new Vector2(16, 16), light, descriptor);
                output[at + 3] = output[at]; output[at + 4] = output[at + 2];
                output[at + 5] = new(position + new Vector2(0, 16), uv + new Vector2(0, 16), light, descriptor);
                offsets[material] += 6;
            }
        }
        return new ChunkGeometry(result, covered, starts, extents, total,packed);
    }
}
