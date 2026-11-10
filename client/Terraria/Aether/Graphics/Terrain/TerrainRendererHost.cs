using Aether.Modding;
using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace Aether.Graphics.Terrain;

public static class TerrainRendererHost
{
    private static ITerrainRenderer current = new LegacyTerrainRenderer();
    private static GraphicsDevice? boundDevice;
    private static RendererMode previousRequest = RendererMode.Legacy;
    private static bool initializationFailed;
    private static bool previousCompute;
    private static bool previousFast;
    public static TerrainGpuContext? Gpu { get; private set; }
    public static string GpuReason { get; private set; } = "Not initialized";
    private static void OnDeviceReset(object? sender, EventArgs args)
    {
        RetireRenderer(); initializationFailed = false;
        Gpu?.Dispose(); Gpu = null;
        if (boundDevice is not null)
        {
            boundDevice.Disposing -= OnDeviceReset;
            boundDevice.DeviceResetting -= OnDeviceReset;
        }
        boundDevice = null;
    }
    private static void EnsureGpu()
    {
        GraphicsDevice? device = Main.instance?.GraphicsDevice;
        if (device is null || device.IsDisposed || ReferenceEquals(device, boundDevice)) return;
        OnDeviceReset(null, EventArgs.Empty);
        boundDevice = device;
        device.Disposing += OnDeviceReset;
        device.DeviceResetting += OnDeviceReset;
        var property = typeof(GraphicsDevice).GetProperty("TerrainDeviceHandleEXT");
        if (property is null) { GpuReason = "FNA terrain bridge is not present"; return; }
        nint handle = (nint)property.GetValue(device, null)!;
        string library = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib64/libFNA3D.so.0");
        if (TerrainGpuContext.TryCreate(handle, library, out var context, out string reason))
        { Gpu = context; GpuReason = ""; }
        else GpuReason = reason;
    }
    public static RendererMode ActiveMode => current.Mode;
    public static bool CanPreparePlainTiles() => current is ChunkedTerrainRenderer c && c.CanPreparePlainTiles;
    public static bool TryPreparePlainTile(int x,int y,Tile tile,Microsoft.Xna.Framework.Color light,Microsoft.Xna.Framework.Vector2 delta,out Texture2D texture)
    {
        texture=null!;
        return current is ChunkedTerrainRenderer chunked && chunked.TryPreparePlainTile(x,y,tile,light,delta,out texture);
    }
    public static bool TryQueuePreparedPlainTile(int x,int y,Tile tile,ushort type,Microsoft.Xna.Framework.Color light,Microsoft.Xna.Framework.Color finalColor)
        => current is ChunkedTerrainRenderer chunked && chunked.TryQueuePreparedPlainTile(x,y,tile,type,light,finalColor);
    public static string FallbackReason { get; private set; } = "";
    public static void BeginFrame()
    {
        TerrainSnapshotPump.BeginRenderFrame();
        try { EnsureGpu(); Gpu?.CollectRetired(); }
        catch(Exception error) when(IsRecoverable(error)) { Recover(error); }
        // Auto is intentionally not promoted until the benchmark gate passes.
        RendererMode requested = GraphicsSettings.TerrainMode;
        if (requested != previousRequest || previousCompute != GraphicsSettings.ComputeTerrain || previousFast != GraphicsSettings.FastTerrain)
        {
            RetireRenderer();
            TerrainSnapshotPump.BeginFrame(false);
            previousCompute = GraphicsSettings.ComputeTerrain;
            previousFast = GraphicsSettings.FastTerrain;
            initializationFailed = false; previousRequest = requested; FallbackReason = "";
            if(Gpu is null)
            {
                // Retry a quarantined bridge only after an explicit mode change.
                OnDeviceReset(null,EventArgs.Empty);
                try { EnsureGpu(); } catch(Exception error) when(IsRecoverable(error)) { Recover(error); }
            }
        }
        if (requested == RendererMode.Auto) FallbackReason = "Auto uses Legacy";
        else if (requested == RendererMode.Legacy) FallbackReason = "";
        if (requested == RendererMode.Chunked && !Main.gameMenu && current is LegacyTerrainRenderer && !initializationFailed)
        {
            try
            {
                if (Gpu is null || boundDevice is null) throw new NotSupportedException(GpuReason);
                current = new ChunkedTerrainRenderer(Gpu, boundDevice, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AetherTerrain"));
                FallbackReason = "";
                AetherLog.For("Aether.Terrain").Information("Chunked Vulkan (geometry: {Geometry}, fast cache: {FastCache})", previousCompute ? "compute" : "CPU", previousFast);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or ArgumentException or OutOfMemoryException)
            { initializationFailed = true; FallbackReason = error.Message; AetherLog.For("Aether.Terrain").Warning("Fell back to Legacy: {Reason}", FallbackReason); }
        }
        if (current is ChunkedTerrainRenderer chunked && chunked.Failure.Length != 0)
        {
            FallbackReason = chunked.Failure;
            AetherLog.For("Aether.Terrain").Warning("Fell back to Legacy: {Reason}", FallbackReason);
            RetireRenderer();
            initializationFailed = true;
        }
        current.BeginFrame(default);
    }
    public static void EndFrame()
    {
        // Framing runs first. Snapshot work uses only the budget left after the
        // visible layers, so it cannot starve preparation of newly entered tiles.
        // Until promotion, only explicit Chunked requests opt into prototype CPU work.
        if (current is ChunkedTerrainRenderer chunked) chunked.PrepareUploads();
        TerrainSnapshotPump.BeginFrame(current is ChunkedTerrainRenderer);
        try { Gpu?.EndFrame(); }
        catch(Exception error) when(IsRecoverable(error)) { Recover(error); }
    }
    private static void RetireRenderer()
    {
        current.Dispose(); current=new LegacyTerrainRenderer();
    }
    private static bool IsRecoverable(Exception error) => error is IOException or InvalidOperationException or NotSupportedException or ArgumentException or OutOfMemoryException;
    private static void Recover(Exception error)
    {
        FallbackReason = error.Message;
        AetherLog.For("Aether.Terrain").Warning("Fell back to Legacy: {Reason}", FallbackReason);
        initializationFailed = true;
        // SDL retains submitted references until the device is finished with them.
        // Dispose while this FNA device still exists; never reuse a failed context.
        RetireRenderer();
        TerrainSnapshotPump.BeginFrame(false);
        Gpu?.Dispose(); Gpu = null; GpuReason=error.Message;
        }
    public static void BeginTilePass(bool solid, int firstX, int lastX, int firstY, int lastY, Microsoft.Xna.Framework.Color high, Microsoft.Xna.Framework.Color medium)
    { if (current is ChunkedTerrainRenderer chunked) chunked.BeginTilePass(solid, firstX, lastX, firstY, lastY, high, medium); }
    public static bool TryQueueTile(int x, int y, ref Terraria.DataStructures.TileDrawInfo data, Microsoft.Xna.Framework.Rectangle rectangle, Microsoft.Xna.Framework.Vector2 position)
        => current is ChunkedTerrainRenderer chunked && chunked.TryQueueTile(x, y, ref data, rectangle, position);
    public static void BeginWallPass(int firstX,int lastX,int firstY,int lastY)
    { if(current is ChunkedTerrainRenderer chunked)chunked.BeginWallPass(firstX,lastX,firstY,lastY); }
    public static bool TryQueueWall(int x,int y,Tile tile,Texture2D texture,Microsoft.Xna.Framework.Rectangle rectangle,Microsoft.Xna.Framework.Vector2 position,Terraria.Graphics.VertexColors colors)
        => current is ChunkedTerrainRenderer chunked && chunked.TryQueueWall(x,y,tile,texture,rectangle,position,colors);
    public static LayerScope MeasureLayer(TerrainLayer layer) => new(layer switch
    {
        TerrainLayer.Walls => "Terrain walls",
        TerrainLayer.SolidTiles => "Terrain solid tiles",
        TerrainLayer.NonSolidTiles => "Terrain non-solid tiles",
        TerrainLayer.Water => "Terrain water",
        _ => "Terrain background"
    });
    public readonly struct LayerScope : IDisposable
    {
        private readonly TerrainGpuContext.MarkerScope gpu;
        internal LayerScope(string name)
        {
            gpu = Gpu?.Marker(name) ?? default;
        }
        public void Dispose() { gpu.Dispose(); }
    }
}
