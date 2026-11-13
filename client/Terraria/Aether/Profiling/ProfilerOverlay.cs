using System;
using System.Collections.Generic;
using System.Diagnostics;
using Aether.Graphics.DearImGui;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace Aether.Profiling;

/// <summary>
/// The frame rate and frame times at the top left, in a Dear ImGui window (<see cref="ImGuiHost" />,
/// independent of the game's UI), from the <see cref="FrameMeter" />'s measurements over the last two
/// seconds, refreshed twice a second (see <see cref="FrameStats" />): the frame rate with its 1% and
/// 0.1% lows, a bar of the share of the frame budget the slower of CPU and GPU uses (green under half,
/// yellow under 90%, red above), a <see cref="FrameTimeGraph" />, frame, CPU and GPU times (mean, p95,
/// p99, max; GPU n/a where the driver can't time it), and the .NET runtime's allocations, heap,
/// collections and JIT compiles. Hidden at first: Right Shift + F12 toggles it (as does the MCP
/// server's overlay tool). It keeps measuring while hidden.
/// </summary>
public static class ProfilerOverlay
{
    private const double RefreshSeconds = 0.5;

    private const double WindowMs = 2000;

    private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs;

    private static readonly Vector4 Green = new Vector4(0.2f, 0.8f, 0.2f, 1f);

    private static readonly Vector4 Yellow = new Vector4(1f, 0.84f, 0f, 1f);

    private static readonly Vector4 Red = new Vector4(1f, 0.2f, 0.2f, 1f);

    private static readonly string[] TimeColumns = { "ms", "avg", "p95", "p99", "max" };

    private static readonly List<FrameSample> Window = new List<FrameSample>();

    private static readonly Stopwatch SinceRefresh = new Stopwatch();

    private static readonly FrameTimeGraph Graph = new FrameTimeGraph();

    // Rows of the times table: name, then mean, p95, p99 and max in ms.
    private static readonly string[][] Times =
    {
        new[] { "Frame", "", "", "", "" },
        new[] { "CPU", "", "", "", "" },
        new[] { "GPU", "", "", "", "" }
    };

    private static double _windowMs;

    private static float _used;

    private static double _budgetMs;

    // Formatted when the stats refresh, not every frame, so the overlay doesn't allocate per frame.
    private static string _budget = "";

    private static string _rates = "";

    private static string _runtime = "";

    private static bool _toggleHeld;

    private static bool _installed;

    private static bool _shown;

    /// <summary>Whether the overlay shows. Only while it does is it an ImGui window, so a hidden one costs no ImGui frame.</summary>
    public static bool Shown
    {
        get => _shown;
        set
        {
            if (value == _shown)
            {
                return;
            }
            _shown = value;
            if (value)
            {
                ImGuiHost.Layout += DrawWindow;
            }
            else
            {
                ImGuiHost.Layout -= DrawWindow;
            }
        }
    }

    /// <summary>Starts collecting the meter's frames (once).</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }
        _installed = true;
        FrameMeter.FrameMeasured += OnFrameMeasured;
        SinceRefresh.Start();
    }

    /// <summary>Toggles the overlay on Right Shift + F12 (once per press); called once a frame.</summary>
    public static void CheckToggleKeys()
    {
        KeyboardState keys = Keyboard.GetState();
        bool held = keys.IsKeyDown(Keys.RightShift) && keys.IsKeyDown(Keys.F12);
        if (held && !_toggleHeld)
        {
            Shown = !Shown;
        }
        _toggleHeld = held;
    }

    private static void OnFrameMeasured(FrameSample sample)
    {
        Window.Add(sample);
        _windowMs += sample.FrameMs;
        Graph.Add(sample);
        if (SinceRefresh.Elapsed.TotalSeconds < RefreshSeconds)
        {
            return;
        }
        // Only the last two seconds' frames.
        int old = 0;
        while (_windowMs > WindowMs && old < Window.Count - 1)
        {
            _windowMs -= Window[old++].FrameMs;
        }
        Window.RemoveRange(0, old);
        SinceRefresh.Restart();
        if (!Shown)
        {
            return;
        }

        FrameStats stats = FrameStats.Of(Window, FrameMeter.BudgetMs);
        _used = (float)stats.BudgetUsed.Mean;
        _budgetMs = stats.BudgetMs;
        _budget = $"{_used:0%} of {stats.BudgetMs:0.00} ms";
        _rates = $"{stats.Fps:0} FPS   1% {stats.Fps1Low:0}   0.1% {stats.Fps01Low:0}";
        Row(Times[0], stats.FrameMs);
        Row(Times[1], stats.CpuMs);
        Row(Times[2], stats.GpuMs);
        FrameStats.RuntimeStats runtime = stats.Runtime;
        _runtime = $"Alloc  {Bytes(runtime.AllocatedBytes.Mean)}/frame (max {Bytes(runtime.AllocatedBytes.Max)})\n" +
            $"Heap   {Bytes(runtime.HeapBytes.Max)}\n" +
            $"GC     {runtime.GcCollections.Gen0}/{runtime.GcCollections.Gen1}/{runtime.GcCollections.Gen2} (pause {runtime.GcPauseMs.Max:0.00} ms)\n" +
            $"JIT    {runtime.JitMethods} methods";
    }

    private static void Row(string[] row, FrameStats.Summary times)
    {
        double?[] values = { times?.Mean, times?.P95, times?.P99, times?.Max };
        for (int i = 0; i < values.Length; i++)
        {
            row[i + 1] = values[i] is double ms ? $"{ms:0.00}" : "n/a";
        }
    }

    private static string Bytes(double bytes)
    {
        if (bytes >= 1 << 20)
        {
            return $"{bytes / (1 << 20):0.0} MB";
        }
        return bytes >= 1 << 10 ? $"{bytes / (1 << 10):0.0} KB" : $"{bytes:0} B";
    }

    private static void DrawWindow()
    {
        if (_rates.Length == 0)
        {
            return;
        }
        ImGui.SetNextWindowPos(new Vector2(2f, 2f));
        ImGui.SetNextWindowBgAlpha(0.5f);
        if (ImGui.Begin("Profiler", OverlayFlags))
        {
            // A fixed width: the default follows the window's, which an auto-sized window takes from it.
            float width = ImGui.CalcTextSize("Frame  00.00  00.00  00.00  00.00").X;
            ImGui.TextUnformatted(_rates);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, _used < 0.5f ? Green : _used < 0.9f ? Yellow : Red);
            ImGui.ProgressBar(MathF.Min(1f, _used), new Vector2(width, 0f), _budget);
            ImGui.PopStyleColor();
            Graph.Draw(_budgetMs, new Vector2(width, 40f));
            if (ImGui.BeginTable("times", TimeColumns.Length, ImGuiTableFlags.SizingFixedFit))
            {
                foreach (string column in TimeColumns)
                {
                    ImGui.TableSetupColumn(column);
                }
                // Headers without their opaque background, which would cover the window's translucency.
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Vector4.Zero);
                ImGui.TableHeadersRow();
                ImGui.PopStyleColor();
                foreach (string[] row in Times)
                {
                    foreach (string cell in row)
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(cell);
                    }
                }
                ImGui.EndTable();
            }
            ImGui.SeparatorText(".NET");
            ImGui.TextUnformatted(_runtime);
        }
        ImGui.End();
    }
}
