using Microsoft.Xna.Framework;
using Terraria;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using Terraria.Graphics.Capture;
using Terraria.Map;
using Point = Microsoft.Xna.Framework.Point;

namespace Aether.Graphics;

// Tile framing mutates world data and uses shared WorldGen scratch state. Keep
// those mutations on the game thread, but yield instead of framing 30,000 tiles
// in one draw. The budget is shared by every tile layer drawn in a frame.
public static class SectionFramingScheduler
{
    private const int MaxTilesPerFrame = 4096;
    private static readonly long FrameBudgetTicks = Math.Max(1, Stopwatch.Frequency / 1000);
    private static readonly Dictionary<Point, SectionWork> pending = new();
    private static readonly List<SectionWork> visible = new();
    private static readonly ConcurrentQueue<SectionInvalidation> invalidations = new();
    private sealed record SectionInvalidation(WorldSections World, Point Key);
    private static WorldSections? world;
    private static long spentTicks;
    private static int processedTiles;

    // Section packets can arrive while a partially framed section is pending.
    public static void InvalidateSection(WorldSections owner, int x, int y)
    {
        invalidations.Enqueue(new SectionInvalidation(owner, new Point(x, y)));
    }

    private static void ApplyInvalidations()
    {
        while (invalidations.TryDequeue(out var item))
            if (ReferenceEquals(item.World, world))
                pending.Remove(item.Key);
    }

    public static void BeginFrame()
    {
        spentTicks = 0;
        Aether.Graphics.Terrain.TerrainPreparationBudget.BeginFrame();
        processedTiles = 0;
        if (Main.gameMenu || !ReferenceEquals(world, Main.sectionManager))
        {
            pending.Clear();
            world = Main.sectionManager;
        }
    }

    public static void ProcessVisibleArea(int left, int top, int right, int bottom)
    {
        if (Main.dedServ || WorldGen.generatingWorld || CaptureManager.Instance.IsCapturing)
        {
            WorldGen.SectionTileFrameWithCheck(left, top, right, bottom);
            return;
        }
        if (!ReferenceEquals(world, Main.sectionManager))
        {
            pending.Clear();
            world = Main.sectionManager;
        }
        ApplyInvalidations();
        if (!Aether.Graphics.Terrain.TerrainPreparationBudget.HasTime || world is null || spentTicks >= FrameBudgetTicks || processedTiles >= MaxTilesPerFrame)
            return;

        long start = Stopwatch.GetTimestamp();
        long sharedRemaining = Aether.Graphics.Terrain.TerrainPreparationBudget.RemainingTicks;
        visible.Clear();
        int firstX = Math.Max(0, Netplay.GetSectionX(left));
        int firstY = Math.Max(0, Netplay.GetSectionY(top));
        int lastX = Math.Min((Main.maxTilesX - 1) / 200, Netplay.GetSectionX(right));
        int lastY = Math.Min((Main.maxTilesY - 1) / 150, Netplay.GetSectionY(bottom));
        for (int x = firstX; x <= lastX; x++)
        {
            for (int y = firstY; y <= lastY; y++)
            {
                Point key = new(x, y);
                if (!world.SectionLoaded(x, y) || world.SectionFramed(x, y))
                {
                    pending.Remove(key);
                    continue;
                }
                if (!pending.TryGetValue(key, out SectionWork? work))
                {
                    work = new SectionWork(key);
                    pending.Add(key, work);
                    Main.mapTime = Main.mapTimeMax + 10;
                }
                visible.Add(work);
            }
        }

        // Finish sections nearest the camera first when flying or teleporting.
        int centerX = (left + right) / 2;
        int centerY = (top + bottom) / 2;
        visible.Sort((a, b) => Distance(a.Key).CompareTo(Distance(b.Key)));
        long Distance(Point key)
        {
            long x = key.X * 200L + 100 - centerX;
            long y = key.Y * 150L + 75 - centerY;
            return x * x + y * y;
        }
        foreach (SectionWork work in visible)
        {
            while (processedTiles < MaxTilesPerFrame)
            {
                if (spentTicks + Stopwatch.GetTimestamp() - start >= FrameBudgetTicks || Stopwatch.GetTimestamp() - start >= sharedRemaining)
                    goto Finished;
                bool done = work.Step();
                processedTiles++;
                if (done)
                {
                    // Publish completion only after every tile and wall is framed.
                    MapUpdateQueue.Add(work.MapArea);
                    world.SetSectionFramed(work.Key.X, work.Key.Y);
                    pending.Remove(work.Key);
                    break;
                }
            }
            if (processedTiles >= MaxTilesPerFrame)
                break;
        }
    Finished:
        long elapsed = Stopwatch.GetTimestamp() - start;
        spentTicks += elapsed;
        Aether.Graphics.Terrain.TerrainPreparationBudget.Charge(elapsed);
    }

    private sealed class SectionWork
    {
        public readonly Point Key;
        public readonly Rectangle MapArea;
        private readonly int firstY, endX, endY;
        private int x, y;

        public SectionWork(Point key)
        {
            Key = key;
            WorldGen.GetSectionBounds(key.X, key.Y, key.X, key.Y,
                out int startX, out int lastX, out int startY, out int lastY);
            x = startX - 1;
            y = firstY = startY - 1;
            endX = lastX + 1;
            endY = lastY + 1;
            MapArea = new Rectangle(startX - 2, startY - 2,
                lastX - startX + 4, lastY - startY + 4);
        }

        public bool Step()
        {
            WorldGen.Reframe(x, y, resetFrame: true);
            if (++y >= endY)
            {
                y = firstY;
                x++;
            }
            return x >= endX;
        }
    }
}
