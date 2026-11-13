using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Aether.Profiling;

/// <summary>
/// One frame's measurements: real time since the last frame (waiting to present included), the main
/// thread's CPU time since then, the GPU time FNA3D last reported (an earlier frame's, null where the
/// driver can't time it), and what the .NET runtime did since then (its AllocatedBytes are the main
/// thread's). Times are in milliseconds.
/// </summary>
public readonly record struct FrameSample(double FrameMs, double CpuMs, double? GpuMs, RuntimeCounters Runtime);

/// <summary>
/// Measures every drawn frame (<see cref="FrameSample" />) and raises <see cref="FrameMeasured" /> with
/// it, for the <see cref="ProfilerOverlay" /> and the frame_stats tool. <c>Main.Draw</c> calls
/// <see cref="Measure" /> once a frame has drawn everything (see <see cref="FrameProfiling" />). CPU time
/// is all of a frame's work on the main thread (updates, drawing, submission), without the time it
/// sleeps waiting to present. GPU time comes from FNA3D_GetGPUFrameTimeEXT, which Aether's FNA3D and
/// SDL patches add on Linux (tools/fna3d.patch, tools/sdl.patch); elsewhere it reads null.
/// </summary>
public static class FrameMeter
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float GetGpuFrameTime(IntPtr device);

    /// <summary>Raised on the main thread after each frame is measured, at the end of its drawing.</summary>
    public static event Action<FrameSample> FrameMeasured;

    private static bool _started;

    private static IntPtr _device;

    private static GetGpuFrameTime _getGpuFrameTime;

    private static TimeSpan _lastCpuTime;

    private static long _lastFrame;

    private static RuntimeCounters _lastRuntime;

    /// <summary>The time a frame has, in milliseconds: one refresh of the window's display (60 Hz if unknown).</summary>
    public static double BudgetMs
    {
        get
        {
            IntPtr mode = SDL3.SDL.SDL_GetCurrentDisplayMode(SDL3.SDL.SDL_GetDisplayForWindow(Main.instance.Window.Handle));
            float rate = mode == IntPtr.Zero ? 0f : Marshal.PtrToStructure<SDL3.SDL.SDL_DisplayMode>(mode).refresh_rate;
            return rate > 0f ? 1000.0 / rate : 1000.0 / 60.0;
        }
    }

    /// <summary>Ends the current frame's measurement and starts the next one's.</summary>
    public static void Measure(GraphicsDevice device)
    {
        TimeSpan cpuTime = ThreadCpuTime.Current();
        long now = Stopwatch.GetTimestamp();
        RuntimeCounters runtime = RuntimeCounters.Now();
        if (!_started)
        {
            Start(device);
        }
        else
        {
            float gpu = _getGpuFrameTime?.Invoke(_device) ?? -1f;
            FrameMeasured?.Invoke(new FrameSample(
                Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds,
                (cpuTime - _lastCpuTime).TotalMilliseconds,
                gpu >= 0f ? gpu : null,
                runtime.Since(_lastRuntime)));
        }
        _lastCpuTime = cpuTime;
        _lastFrame = now;
        _lastRuntime = runtime;
    }

    // FNA's FNA3D device, and Aether's export from the FNA3D it loaded: the file names FNA.dll.config
    // maps FNA3D to (NativeLibrary.Load doesn't run FNA's mapping resolver).
    private static void Start(GraphicsDevice device)
    {
        _started = true;
        _device = (IntPtr)typeof(GraphicsDevice).GetField("GLDevice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(device);
        string fna3d = OperatingSystem.IsWindows() ? "FNA3D.dll" : OperatingSystem.IsMacOS() ? "libFNA3D.0.dylib" : "libFNA3D.so.0";
        if (NativeLibrary.TryLoad(fna3d, typeof(Game).Assembly, null, out IntPtr library) &&
            NativeLibrary.TryGetExport(library, "FNA3D_GetGPUFrameTimeEXT", out IntPtr export))
        {
            _getGpuFrameTime = Marshal.GetDelegateForFunctionPointer<GetGpuFrameTime>(export);
        }
    }
}
