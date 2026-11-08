using System;
using System.Diagnostics;
using Terraria;
using Terraria.Graphics.Capture;

namespace Aether.Graphics;

/// <summary>Presentation time expressed in Terraria's original 60 Hz animation units.</summary>
internal static class VisualAnimationTime
{
    private const double TicksPerSecond = 60.0;
    private static long lastFrame;
    private static long lastWorldUpdate;
    private static double remainder;

    internal static float FrameScale { get; private set; } = 1f;
    internal static int WholeTicks { get; private set; } = 1;
    internal static float Fraction => (float)remainder;
    internal static float WorldFraction { get; private set; }

    internal static void BeginFrame()
    {
        long now = Stopwatch.GetTimestamp();
        double ticks = lastFrame == 0 ? 1.0 : (now - lastFrame) * TicksPerSecond / Stopwatch.Frequency;
        lastFrame = now;

        // Do not replay a burst of visual side effects after a long stall.
        double elapsed = Math.Max(0.0, Math.Min(ticks, 15.0));
        FrameScale = (float)elapsed;
        remainder += elapsed;
        WholeTicks = (int)remainder;
        remainder -= WholeTicks;

        WorldFraction = Main.gameMenu || Main.gamePaused || lastWorldUpdate == 0 || CaptureManager.Instance.IsCapturing
            ? 0f
            : Math.Min(1f, (float)((now - lastWorldUpdate) * TicksPerSecond / Stopwatch.Frequency));
    }

    internal static void WorldUpdated() => lastWorldUpdate = Stopwatch.GetTimestamp();
}
