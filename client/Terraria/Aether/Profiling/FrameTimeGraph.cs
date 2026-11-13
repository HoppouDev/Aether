using System;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace Aether.Profiling;

/// <summary>
/// An ImGui graph of the last frames' times (<see cref="FrameSample.FrameMs"/>), from 0 to two frame
/// budgets, with a line at one budget: frames above it missed a refresh. Drawn on whole pixels, one
/// column per frame filled from the previous frame's time to its own, so it doesn't shimmer as it
/// scrolls (ImGui's PlotLines places antialiased segments at fractional pixels). Draws without allocating.
/// </summary>
public sealed class FrameTimeGraph(int frames = 1024)
{
    private const uint BudgetLineColor = 0x59FFFFFF; // white at 35% (ImGui packs ABGR)

    private readonly float[] _times = new float[frames];
    private int _next;

    public void Add(FrameSample sample)
    {
        _times[_next] = (float)sample.FrameMs;
        _next = (_next + 1) % _times.Length;
    }

    public void Draw(double budgetMs, Vector2 size)
    {
        ImGui.Dummy(size);
        Vector2 min = Floor(ImGui.GetItemRectMin()), max = Floor(ImGui.GetItemRectMax());
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg));

        int height = (int)(max.Y - min.Y), count = Math.Min((int)(max.X - min.X), _times.Length);
        float top = (float)(2 * budgetMs);
        int Y(float ms) => (int)max.Y - 1 - (int)MathF.Round(Math.Clamp(ms / top, 0, 1) * (height - 1));
        uint color = ImGui.GetColorU32(ImGuiCol.PlotLines);
        int previous = Y(_times[(_next - count + _times.Length) % _times.Length]);
        for (int i = 0; i < count; i++)
        {
            int y = Y(_times[(_next - count + i + _times.Length) % _times.Length]);
            float x = max.X - count + i;
            draw.AddRectFilled(new Vector2(x, Math.Min(previous, y)), new Vector2(x + 1, Math.Max(previous, y) + 1), color);
            previous = y;
        }

        float budgetY = Y((float)budgetMs);
        draw.AddRectFilled(new Vector2(min.X, budgetY), new Vector2(max.X, budgetY + 1), BudgetLineColor);
    }

    private static Vector2 Floor(Vector2 v) => new(MathF.Floor(v.X), MathF.Floor(v.Y));
}
