using Aether.Graphics.DearImGui;
using Microsoft.Xna.Framework.Graphics;

namespace Aether.Profiling;

/// <summary>
/// The frame profiler's one hook: <c>Main.Draw</c> calls <see cref="EndFrame" /> once the frame is
/// drawn (before <c>Main.OnPostDraw</c>, whose handlers take screenshots), which draws the ImGui
/// windows (the <see cref="ProfilerOverlay" />) over it and then ends the frame's measurement, so a
/// frame's time includes drawing them.
/// </summary>
public static class FrameProfiling
{
    public static void EndFrame(GraphicsDevice device)
    {
        ProfilerOverlay.Install();
        ProfilerOverlay.CheckToggleKeys();
        ImGuiHost.Draw();
        FrameMeter.Measure(device);
    }
}
