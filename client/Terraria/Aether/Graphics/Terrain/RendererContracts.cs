using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;

namespace Aether.Graphics.Terrain;

public enum RendererMode { Legacy, Chunked, Auto }
public enum RendererQuality { Standard, Performance }
public enum TerrainLayer { Walls, SolidTiles, NonSolidTiles, Water, Background }

public readonly record struct TerrainFrame(long WorldGeneration, long Frame, float CameraX, float CameraY, int Width, int Height);

public interface ITerrainRenderer : IDisposable
{
    RendererMode Mode { get; }
    void BeginFrame(in TerrainFrame frame);
    // False leaves the entire layer on the compatibility path.
    bool TryDraw(TerrainLayer layer);
}

public sealed class LegacyTerrainRenderer : ITerrainRenderer
{
    public RendererMode Mode => RendererMode.Legacy;
    public void BeginFrame(in TerrainFrame frame) { }
    public bool TryDraw(TerrainLayer layer) => false;
    public void Dispose() { }
}
