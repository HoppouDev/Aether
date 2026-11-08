using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;
using System.Diagnostics;

namespace Aether.Graphics.Terrain;

// Counts CPU time spent preparing terrain, rather than wall time since frame start.
public static class TerrainPreparationBudget
{
    public static readonly long LimitTicks = Math.Max(1, Stopwatch.Frequency / 1000);
    private static long spent;
    public static long RemainingTicks => Math.Max(0, LimitTicks - spent);
    public static bool HasTime => RemainingTicks > 0;
    public static void BeginFrame() => spent = 0;
    public static void Charge(long ticks) => spent = Math.Min(LimitTicks, spent + Math.Max(0, ticks));
}
