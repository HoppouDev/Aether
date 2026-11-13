using System;
using Terraria;

namespace Aether.Graphics.DearImGui;

/// <summary>
/// Dear ImGui over the game: one context and renderer (<see cref="ImGuiRenderer" />), drawn over the
/// finished frame (<see cref="Aether.Profiling.FrameProfiling" /> calls <see cref="Draw" /> from
/// <c>Main.Draw</c>). Windows add themselves by handling <see cref="Layout" />, in which they make
/// their ImGui calls; with no handlers, nothing is created or drawn.
/// </summary>
public static class ImGuiHost
{
    /// <summary>Raised once a frame, between ImGui's NewFrame and Render: make ImGui calls here.</summary>
    public static event Action Layout;

    private static ImGuiRenderer _renderer;

    public static void Draw()
    {
        if (Layout == null)
        {
            return;
        }
        if (_renderer == null)
        {
            _renderer = new ImGuiRenderer(Main.instance);
            _renderer.RebuildFontAtlas();
        }
        _renderer.BeforeLayout();
        Layout();
        _renderer.AfterLayout();
    }
}
