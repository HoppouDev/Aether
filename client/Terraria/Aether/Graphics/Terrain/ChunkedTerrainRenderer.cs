using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Graphics;
using Terraria.Graphics.Capture;
using NumMatrix = Microsoft.Xna.Framework.Matrix;
using Vector4 = Microsoft.Xna.Framework.Vector4;
using GameColor = Microsoft.Xna.Framework.Color;
using GameVector = Microsoft.Xna.Framework.Vector2;
using GameMatrix = Microsoft.Xna.Framework.Matrix;
using GameRect = Microsoft.Xna.Framework.Rectangle;

namespace Aether.Graphics.Terrain;

// Graphics-owner adapter. Workers produce immutable geometry; only this class
// borrows textures, uploads buffers, and queues drawing in TileBatch sort order.
public sealed partial class ChunkedTerrainRenderer : ITerrainRenderer
{
    private readonly record struct TileRunKey(ChunkKey Chunk, int Material, int Paint);

    private readonly TerrainGpuContext gpu;
    private readonly TerrainGpuPipeline pipeline,fastPipeline;
    private readonly TerrainGpuComputePipeline? expandPipeline;
    public bool ComputeGeometry => expandPipeline is not null;
    private readonly SpriteBatch fallback;
    public const int FallbackReservation = 1024 * 1024;
    private readonly ChunkFrameUploadBudget uploads = new();
    private readonly Dictionary<ChunkKey, Slot> slots = new();
    private readonly List<Slot> retired = new();
    private readonly List<TerrainGpuImage> pages = new();
    private readonly Dictionary<int, Material> materials = new();
    private readonly Dictionary<TileRunKey, Command> queued = new();
    private ChunkAtlasLayout layout = new();
    private ChunkGeometryCache? boundCache;
    private readonly PropertyInfo textureHandle;
    private long world = long.MinValue, assets = long.MinValue;
    private int copies, publications;
    private bool disposed, passDisabled = true, lightReady, lightUploaded;
    private int left, top, width, height;
    private GameColor high, medium;
    private bool smooth;
    private float lightingBrightness;
    private TerrainGpuImage? light;
    private Vector4[] lightPixels = new Vector4[0];
    public RendererMode Mode => RendererMode.Chunked;
    public string Failure { get; private set; } = "";

    public ChunkedTerrainRenderer(TerrainGpuContext gpu, GraphicsDevice device, string shaderDirectory)
    {
        try
        {
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(shaderDirectory, "manifest.json")));
            if ((int?)manifest["terrain_record_version"] != ChunkGeometry.RecordVersion ||
                (ChunkComputeMode.Requested && (int?)manifest["terrain_compute_record_version"] != ChunkComputeMode.RecordVersion))
                throw new NotSupportedException("Incompatible terrain shader format");
        }
        catch (Newtonsoft.Json.JsonException error)
        {
            throw new NotSupportedException("Invalid terrain shader manifest", error);
        }
        if ((gpu.Capabilities & TerrainGpuCapabilities.Streamed) == 0)
            throw new NotSupportedException("Streamed terrain bridge unavailable");
        textureHandle = typeof(Texture).GetProperty("TerrainTextureHandleEXT") ?? throw new NotSupportedException("Borrowed texture bridge unavailable");
        this.gpu = gpu;
        TerrainGpuPipeline CreatePipeline(string vertex, string fragment)
        {
            var v = File.ReadAllBytes(Path.Combine(shaderDirectory, vertex));
            var f = File.ReadAllBytes(Path.Combine(shaderDirectory, fragment));
            CheckGpuRoom(65536L + v.Length + f.Length);
            return gpu.CreateDrawPipeline(v, f);
        }
        pipeline = CreatePipeline("terrain-cache.vert.spv", "terrain-cache.frag.spv");
        try
        {
            fastPipeline=CreatePipeline("terrain-cache-fast.vert.spv","terrain-cache-fast.frag.spv");
            wallPipeline=CreatePipeline("terrain.vert.spv","terrain-wall.frag.spv");
            if(ChunkComputeMode.Requested)
            {
                var shader=File.ReadAllBytes(Path.Combine(shaderDirectory,"terrain-expand.comp.spv"));
                CheckGpuRoom(65536L+shader.Length);
                expandPipeline=gpu.CreateExpandPipeline(shader);
            }
            fallback=new SpriteBatch(device);wallFallback=new TileBatch(device);
        }
        catch
        {
            expandPipeline?.Dispose();fallback?.Dispose();wallFallback?.Dispose();
            fastPipeline?.Dispose();wallPipeline?.Dispose();pipeline.Dispose();throw;
        }
    }
    public void BeginFrame(in TerrainFrame frame)
    {
        uploads.BeginFrame(); copies = publications = 0; queued.Clear(); wallRuns.Clear(); ResetFastBindings(); DisposeRetired();
    }
    public bool TryDraw(TerrainLayer layer) => false; // compatibility pass continues; ordinary quads are intercepted below
    public void BeginTilePass(bool solid, int firstX, int lastX, int firstY, int lastY, GameColor high, GameColor medium)
    {
        queued.Clear(); ResetFastBindings(); lightReady = lightUploaded = false;
        passDisabled = disposed || Failure.Length != 0 || !solid || Main.gameMenu || WorldGen.generatingWorld || CaptureManager.Instance.Active ||
            !gpu.CanDrawCurrentTarget();
        var transform = Main.tileBatch.TerrainTransform;
        if (transform.M14 != 0 || transform.M24 != 0 || transform.M44 != 1 || transform.M11 * transform.M22 - transform.M12 * transform.M21 <= 0) passDisabled = true;
        this.high = high; this.medium = medium; smooth = Lighting.NotRetro;
        left = Math.Max(0, firstX - 3); top = Math.Max(0, firstY - 1);
        width = Math.Min(Main.maxTilesX, lastX + 3) - left;
        height = Math.Min(Main.maxTilesY, lastY + 5) - top;
        if (width < 1 || height < 1 || (long)width * height * 16 > ChunkFrameUploadBudget.Limit) passDisabled = true;
    }
    public bool TryQueueTile(int x, int y, ref TileDrawInfo data, GameRect rectangle, GameVector position)
    {
        int materialSlot = ChunkMaterials.Index(data.typeCache);
        if (passDisabled || x < 1 || y < 1 || x >= Main.maxTilesX - 1 || y >= Main.maxTilesY - 1 || materialSlot < 0 || !IsOrdinaryType(data.typeCache) || data.tileTop != 0 || data.addFrX != 0 || data.addFrY != 0 || rectangle.Width != 16 || rectangle.Height != (data.tileCache.halfBrick()?8:16) || data.tileSpriteEffect != SpriteEffects.None ||
            data.colorTint != GameColor.White || data.finalColor != data.tileLight || Main.shimmerAlpha > 0) return false;
        var tile = data.tileCache;
        if (tile.actuator() || tile.inActive() ||
            (!tile.halfBrick() && tile.slope()==0 && (Main.tile[x - 1, y].halfBrick() || Main.tile[x + 1, y].halfBrick()))) return false;
        if(tile.slope()>0 && (Terraria.ID.TileID.Sets.DontDrawTileSlopes[data.typeCache] || Terraria.ID.TileID.Sets.HasSlopeFrames[data.typeCache]))return false;
        int shape=tile.slope()==0?ChunkTileShape.Normal:tile.slope();
        if(tile.halfBrick())
        {var below=Main.tile[x,y+1];shape=!below.active() || !Main.tileSolid[below.type] || below.halfBrick()?ChunkTileShape.HalfEdge:ChunkTileShape.Half;}
        int paint=tile.color(), sourceKey=materialSlot+paint*PaintStride;
        if(!materials.TryGetValue(sourceKey,out var material) || !ReferenceEquals(material.Source,data.drawTexture))
        {RequestMaterial(sourceKey,data.drawTexture);return false;}
        ChunkKey key = ChunkKey.FromTile(x, y);
        if (!TerrainSnapshotPump.TryGetValidated(key, out var prepared) || prepared?.Geometry is null ||
            !slots.TryGetValue(key, out var slot) || slot.Result.Version != prepared.Version ||
            !prepared.Geometry.Matches(x - key.X * 64, y - key.Y * 64, data.typeCache, rectangle.X, rectangle.Y) ||
            prepared.Geometry.Shape(x-key.X*64,y-key.Y*64)!=shape ||
            !slot.Ready(materialSlot))
        { return false; }
        var extent=prepared.Geometry.TextureExtent(materialSlot);
        if(extent.X>data.drawTexture.Width || extent.Y>data.drawTexture.Height)return false;
        int lx = x - left, ly = y - top;
        if (lx < 1 || ly < 1 || lx >= width - 1 || ly >= height - 1) return false;
        if (!lightReady && !PrepareLighting()) { passDisabled = true; return false; }
        int index = ly * width + lx;
        GameVector delta = position - new GameVector(x * 16, y * 16+(tile.halfBrick()?8:0));
        var command = GetTileCommand(slot, materialSlot, paint, material, delta);
        if (command is null) return false;
        if (command.Delta != delta) return false;
        lightPixels[index].W = -(float)((data.tileLight.PackedValue & 0xffffffu) + 1u);
        command.Cells.Add(new Cell(x, y, data.tileLight.PackedValue));
        if(paint==0 && !tile.fullbrightBlock())command.Fast=true;else command.Advanced=true;


        return true;
    }
    private Command? GetTileCommand(Slot slot, int materialSlot, int paint, Material material, GameVector delta)
    {
        var previous = lastTileCommand;
        if (ChunkFastDraw.Enabled && previous is not null && ReferenceEquals(previous.Slot, slot) && previous.Type == materialSlot &&
            previous.Uniforms.Reserved.Z == paint && ReferenceEquals(previous.Material, material))
            return previous.Delta == delta ? previous : null;
        var key = slot.Result.Key;
        if (!queued.TryGetValue(new TileRunKey(key, materialSlot, paint), out var command))
        {
            var viewport = Main.instance.GraphicsDevice.Viewport;
            if (viewport.Width < 1 || viewport.Height < 1) return null;
            GameMatrix transform = Main.tileBatch.TerrainTransform;
            var matrix = new NumMatrix(transform.M11, transform.M12, transform.M13, transform.M14, transform.M21, transform.M22, transform.M23, transform.M24,
                transform.M31, transform.M32, transform.M33, transform.M34, transform.M41, transform.M42, transform.M43, transform.M44);
            var uniforms = new TerrainDrawUniforms
            {
                Transform = NumMatrix.CreateTranslation(delta.X, delta.Y, 0) * matrix * NumMatrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1),
                LightRect = new Vector4(material.Region.X, material.Region.Y, left, top),
                Options = new Vector4(high.R, high.G, high.B, smooth ? 1 : 0),
                Tint = new Vector4(medium.R, medium.G, medium.B, lightingBrightness),
                Reserved = new Vector4(width, height, paint, 0)
            };
            command = new Command(slot, materialSlot, material, uniforms, transform, delta);
            queued[new TileRunKey(key, materialSlot, paint)] = command;
            Main.tileBatch.QueueTerrainDraw(material.Source, () => DrawQueued(command));
        }
        if (command.Delta != delta) return null;
        if (ChunkFastDraw.Enabled) lastTileCommand = command;
        return command;
    }
    private bool PrepareLighting()
    {
        if (!TerrainPreparationBudget.HasTime || (long)width * height * 16 > uploads.Remaining) return false;
        long start = Stopwatch.GetTimestamp();
        try
        {
            int count = width * height;
            if (lightPixels.Length < count)
            {
                lightPixels = new Vector4[count];
            }
            int targetWidth = PowerOfTwo(width), targetHeight = PowerOfTwo(height);
            if (light is null || light.Width < targetWidth || light.Height < targetHeight)
            { CheckGpuRoom((long)targetWidth * targetHeight * 16); light?.Dispose(); light = gpu.CreateImage(targetWidth, targetHeight, TerrainImageFormat.Rgba32Float); }
            var engine = Lighting._activeEngine; lightingBrightness = Lighting.GlobalBrightness;
            for (int yy = 0; yy < height; yy++) for (int xx = 0; xx < width; xx++)
            {
                int index = yy * width + xx;
                if ((index & 63) == 0 && Stopwatch.GetTimestamp() - start >= TerrainPreparationBudget.RemainingTicks) return false;
                var value = smooth ? engine.GetColor(left + xx, top + yy) : default;
                lightPixels[index] = new Vector4(value.X, value.Y, value.Z, 0);
            }
            if (!uploads.TryReserve(count * 16)) return false;
            lightReady = true; return true;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException or OutOfMemoryException)
        { Failure = error.Message; return false; }
        finally { TerrainPreparationBudget.Charge(Stopwatch.GetTimestamp() - start); }
    }
    private int DrawQueued(Command command)
    {
        // A layered restart can flush before the tile pass returns. Once any
        // mask has been uploaded, further tiles stay on the compatibility path.
        passDisabled = true;
        if (Failure.Length != 0) return DrawFallback(command);
        bool fastDrawn=false;
        try
        {
            if (!lightUploaded)
            {
                long start = Stopwatch.GetTimestamp();
                try { gpu.Upload(light!, width, height, lightPixels); lightUploaded = true; }
                finally { TerrainPreparationBudget.Charge(Stopwatch.GetTimestamp() - start); }
            }
            int draws=0;
            if(command.Fast)
            {
                if(!gpu.Draw(fastPipeline,command.Slot.Buffers[command.Type]!,0,command.Slot.Result.Geometry!.VertexCount(command.Type),pages[command.Material.Region.Page],light!,command.Uniforms))
                    throw new InvalidOperationException("Terrain target changed during fast flush");
                fastDrawn=true;draws++;
            }
            if(command.Advanced)
            {
                var uniforms=command.Uniforms;uniforms.Reserved.W=1;
                if(!gpu.Draw(pipeline,command.Slot.Buffers[command.Type]!,0,command.Slot.Result.Geometry!.VertexCount(command.Type),pages[command.Material.Region.Page],light!,uniforms))
                    throw new InvalidOperationException("Terrain target changed during appearance flush");
                draws++;
            }
            return draws;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException or OutOfMemoryException)
        { Failure = error.Message; return (fastDrawn?1:0)+DrawFallback(command,fastDrawn); }
    }
    private unsafe int DrawFallback(Command command,bool skipFast=false)
    {
        // Only the pure drawing operation is replayed. All tile side effects and
        // RNG-consuming work already ran exactly once before the interception.
        fallback.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None,
            Main.instance.GraphicsDevice.RasterizerState, null, command.Transform);
        int sprites = 0;
        ShapeQuad* quads=stackalloc ShapeQuad[9];
        foreach (var cell in command.Cells)
        {
            var geometry = command.Slot.Result.Geometry!;
            int localX = cell.X - command.Slot.Result.Key.X * 64, localY = cell.Y - command.Slot.Result.Key.Y * 64;
            if(skipFast && command.Uniforms.Reserved.Z==0 && !geometry.Fullbright(localX,localY))continue;
            var uv = geometry.TileUV(localX, localY);
            var position = new GameVector(cell.X * 16, cell.Y * 16) + command.Delta;
            var baseColor = new GameColor { PackedValue = cell.Color };
            int shape=geometry.Shape(localX,localY);

            {
                int count=ChunkTileShape.WriteQuads(shape,(int)uv.X,(int)uv.Y,quads);
                for(int i=0;i<count;i++)
                {var q=quads[i];fallback.Draw(command.Material.Source,position+new GameVector(q.X,q.Y),new GameRect(q.U,q.V,q.Width,q.Height),baseColor);sprites++;}
                continue;
            }
            int columns = 1;
            bool fullbright=geometry.Fullbright(localX,localY);
            if (!fullbright && smooth && (baseColor.R > high.R || baseColor.G > high.G || baseColor.B > high.B)) columns = 3;
            else if (!fullbright && smooth && (baseColor.R > medium.R || baseColor.G > medium.G || baseColor.B > medium.B)) columns = 2;
            for (int yy = 0; yy < columns; yy++) for (int xx = 0; xx < columns; xx++)
            {
                int dx = columns == 3 ? (xx == 0 ? 0 : xx == 1 ? 4 : 12) : xx * 8, dy = columns == 3 ? (yy == 0 ? 0 : yy == 1 ? 4 : 12) : yy * 8;
                int w = columns == 1 ? 16 : columns == 3 ? (xx == 1 ? 8 : 4) : 8, h = columns == 1 ? 16 : columns == 3 ? (yy == 1 ? 8 : 4) : 8;
                var lit = baseColor.ToVector3();
                if (columns > 1)
                {
                    Vector4 neighbor;
                    if (columns == 3) neighbor = LightAt(cell.X + xx - 1, cell.Y + yy - 1);
                    else { var a = LightAt(cell.X, cell.Y + (yy == 0 ? -1 : 1)); var b = LightAt(cell.X + (xx == 0 ? -1 : 1), cell.Y); neighbor = a.X + a.Y + a.Z >= b.X + b.Y + b.Z ? b : a; }
                    neighbor *= lightingBrightness;
                    lit.X = (lit.X + neighbor.X) * .5f; lit.Y = (lit.Y + neighbor.Y) * .5f; lit.Z = (lit.Z + neighbor.Z) * .5f;
                }
                var color = new GameColor(Math.Min((int)(lit.X * 255), 255), Math.Min((int)(lit.Y * 255), 255), Math.Min((int)(lit.Z * 255), 255));
                fallback.Draw(command.Material.Source, position + new GameVector(dx, dy), new GameRect((int)uv.X + dx, (int)uv.Y + dy, w, h), color);
                sprites++;
            }
        }
        fallback.End(); Main.tileBatch.RebindAfterTerrainFallback(); return (sprites + 2047) / 2048;
    }
    private Vector4 LightAt(int x, int y) => lightPixels[(y - top) * width + x - left];
    public void PrepareUploads()
    {
        if (disposed || Failure.Length != 0) return;
        if (Main.gameMenu || WorldGen.generatingWorld) { ClearSlots(); return; }
        var cache = TerrainSnapshotPump.GeometryCache;
        if (!ReferenceEquals(cache, boundCache))
        { if (boundCache is not null) boundCache.Evicted -= Evict; ClearSlots(); boundCache = cache; if (cache is not null) cache.Evicted += Evict; }
        var epoch = TerrainSnapshotPump.CurrentEpoch;
        if (world != epoch.World || assets != epoch.Assets)
        { ClearSlots(); if (assets != epoch.Assets) ClearAtlas(); world = epoch.World; assets = epoch.Assets; }
        DisposeRetired();
        long start = Stopwatch.GetTimestamp();
        try
        {
            if (!HasTime(start)) return;
            PrepareRequestedMaterials(start);
            if (cache is null) return;
            foreach (var result in cache.CurrentResults())
            {
                if (!HasTime(start) || uploads.Remaining < 32 || publications >= 4) break;
                PrepareWallUploads(result,start);
                if (publications >= 4) break;
                if (result.Geometry is null || result.Geometry.TileCount == 0) continue;
                if (!slots.TryGetValue(result.Key, out var slot) || slot.Result.Version != result.Version)
                { Evict(result.Key); slot = new Slot(result); slots[result.Key] = slot; }
                if(result.Geometry.Packed!=ComputeGeometry)throw new InvalidOperationException("Terrain preparation mode mismatch");
                bool wasReady = slot.AnyReady;
                foreach (int type in result.Geometry.UsedMaterials)
                {
                    if (!HasTime(start)) break;
                    int bytes = result.Geometry.VertexCount(type) * 32;
                    if (bytes == 0 || slot.Uploaded[type] == bytes || !readyTiles.Contains(type)) continue;
                    if(ComputeGeometry)
                    {
                        if(PrepareComputedBuffer(result.Geometry.UploadData(type),ref slot.Inputs[type],ref slot.Buffers[type]))slot.Uploaded[type]=bytes;
                        continue;
                    }
                    if (slot.Buffers[type] is null) { CheckGpuRoom(bytes); slot.Buffers[type] = gpu.CreateBuffer(bytes, TerrainBufferUsage.Vertex); }
                    int amount = Math.Min(bytes - slot.Uploaded[type], uploads.Remaining) / 32 * 32;
                    if (amount == 0) continue;
                    uploads.TryReserve(amount);
                    gpu.Upload(slot.Buffers[type]!, slot.Uploaded[type], result.Geometry.UploadData(type), slot.Uploaded[type] / 32, amount / 32);
                    slot.Uploaded[type] += amount; }
                if (!wasReady && slot.AnyReady) publications++;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException or OutOfMemoryException)
        { Failure = error.Message; ClearSlots(); ClearAtlas(); }
        finally { TerrainPreparationBudget.Charge(Stopwatch.GetTimestamp() - start); DisposeRetired(); }
    }
    private bool PrepareComputedBuffer(TerrainVertex[] records,ref TerrainGpuBuffer? input,ref TerrainGpuBuffer? output)
    {
        int inputBytes=checked(records.Length*32),outputBytes=checked(records.Length*6*32);
        if(inputBytes>uploads.Remaining)return false;
        CheckGpuRoom((input is null?inputBytes:0L)+(output is null?outputBytes:0L));
        input??=gpu.CreateBuffer(inputBytes,TerrainBufferUsage.ComputeRead);
        output??=gpu.CreateBuffer(outputBytes,TerrainBufferUsage.Vertex|TerrainBufferUsage.ComputeWrite);
        uploads.TryReserve(inputBytes);
        gpu.Upload(input,0,records,0,records.Length);
        gpu.Expand(expandPipeline!,input,output,0,records.Length,0);
        return true;
    }
    private static bool IsOrdinaryType(ushort type) => Main.tileSolid[type] && !Main.tileSolidTop[type] &&
        !Main.tileFrameImportant[type] && !Main.tileShine2[type] && !Main.tileFlame[type];
    private const int PaintStride=1024;
    private readonly Dictionary<int,Texture2D> requestedMaterials=new();
    private readonly HashSet<int> readyWalls=new(),readyTiles=new();
    private void RequestMaterial(int key,Texture2D source)
    {
        if(requestedMaterials.ContainsKey(key) || requestedMaterials.Count<256)requestedMaterials[key]=source;
    }
    private void PrepareRequestedMaterials(long start)
    {
        foreach(var pair in requestedMaterials)
        {if(!HasTime(start))break;PrepareAtlasMaterial(pair.Key,pair.Value);}
    }
    private void PrepareAtlasMaterial(int type,Texture2D source)
    {
        if(source.IsDisposed || (materials.TryGetValue(type,out var old) && ReferenceEquals(source,old.Source)))return;
        int cost = (source.Width + 4) * (source.Height + 4) * 4;
        if (cost > ChunkFrameUploadBudget.Limit - copies) return;
        if (!layout.TryAllocate(source.Width, source.Height, out var region)) return;
        while (pages.Count <= region.Page) { CheckGpuRoom((long)ChunkAtlasLayout.DefaultPageSize * ChunkAtlasLayout.DefaultPageSize * 4); pages.Add(gpu.CreateImage(ChunkAtlasLayout.DefaultPageSize, ChunkAtlasLayout.DefaultPageSize, renderTarget: true)); }
        bool clear = true; foreach (var prior in materials.Values) if (prior.Region.Page == region.Page) { clear = false; break; }
        nint handle = (nint)textureHandle.GetValue(source, null)!;
        if (!gpu.CopyFnaImage(handle, source.Width, source.Height, pages[region.Page], region.X, region.Y, 2, clear)) throw new InvalidOperationException("Atlas source copy rejected");
        materials[type] = new Material(source, region); copies += cost;
        int raw=type%PaintStride;if(raw<ChunkMaterials.Count)readyTiles.Add(raw);else if(raw>ChunkMaterials.Count)readyWalls.Add(raw-ChunkMaterials.Count);
    }
    private void CheckGpuRoom(long bytes) { if (bytes > TerrainGpuContext.MaxResidentBytes - gpu.ResidentBytes - FallbackReservation) throw new InvalidOperationException("Terrain VRAM budget exceeded"); }
    private bool HasTime(long start) => Stopwatch.GetTimestamp() - start < TerrainPreparationBudget.RemainingTicks;
    private void Evict(ChunkKey key) { EvictWalls(key); if (slots.TryGetValue(key, out var slot)) { slots.Remove(key); retired.Add(slot); } }
    private void ClearSlots() { ClearWallSlots(); foreach (var slot in slots.Values) retired.Add(slot); slots.Clear(); }
    private void ClearAtlas() { foreach (var page in pages) page.Dispose(); pages.Clear(); materials.Clear(); requestedMaterials.Clear(); readyWalls.Clear(); readyTiles.Clear(); layout = new(); }
    private void DisposeRetired() { DisposeWallRetired(); foreach (var slot in retired) { foreach (var buffer in slot.Buffers) buffer?.Dispose(); foreach(var input in slot.Inputs)input?.Dispose(); } retired.Clear(); }
    private static int PowerOfTwo(int n) { int result = 1; while (result < n) result *= 2; return result; }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (boundCache is not null) boundCache.Evicted -= Evict;
        ClearSlots(); DisposeRetired(); ClearAtlas(); light?.Dispose(); wallLight?.Dispose(); wallPipeline.Dispose(); wallFallback.Dispose(); expandPipeline?.Dispose(); fastPipeline.Dispose(); pipeline.Dispose(); fallback.Dispose(); queued.Clear(); wallRuns.Clear();
        lightPixels = new Vector4[0];
    }
    private readonly record struct Cell(int X, int Y, uint Color);
    private sealed class Command(Slot slot, int type, Material material, TerrainDrawUniforms uniforms, GameMatrix transform, GameVector delta)
    {
        public readonly Slot Slot = slot; public readonly int Type = type; public readonly Material Material = material;
        public readonly TerrainDrawUniforms Uniforms = uniforms; public readonly GameMatrix Transform = transform; public readonly GameVector Delta = delta;
        public bool Fast,Advanced;
        public readonly List<Cell> Cells = new();
    }
    private sealed record Material(Texture2D Source, AtlasRegion Region);
    private sealed class Slot(PreparedChunk result)
    {
        public readonly PreparedChunk Result = result;
        public readonly TerrainGpuBuffer?[] Buffers = new TerrainGpuBuffer?[ChunkMaterials.Count];
        public readonly TerrainGpuBuffer?[] Inputs = new TerrainGpuBuffer?[ChunkMaterials.Count];
        public readonly int[] Uploaded = new int[ChunkMaterials.Count];
        public bool Ready(int type) => Buffers[type] is not null && Uploaded[type] == Result.Geometry!.VertexCount(type) * 32;
        public bool AnyReady
        {
            get { foreach (int material in Result.Geometry!.UsedMaterials) if (Ready(material)) return true; return false; }
        }
    }
}
