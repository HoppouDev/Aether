using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Profiling;

/// <summary>
/// Statistics over frames' measurements (<see cref="FrameSample"/>): what the <see cref="ProfilerOverlay"/>
/// overlay shows and the frame_stats tool returns. Like MangoHud's summary, Fps1Low and Fps01Low are
/// the frame rates of the 99th and 99.9th percentile frame times. BudgetUsed is the share of the frame
/// budget the slower of CPU and GPU used, per frame; LongFrames counts frames over 1.5 budgets
/// (hitches, or with vsync, missed refreshes). GpuMs is null when the driver timed none of them.
/// </summary>
public sealed record FrameStats(
    int Frames,
    double Fps,
    double Fps1Low,
    double Fps01Low,
    double BudgetMs,
    FrameStats.Summary BudgetUsed,
    FrameStats.Summary FrameMs,
    FrameStats.Summary CpuMs,
    FrameStats.Summary GpuMs,
    int LongFrames,
    FrameStats.RuntimeStats Runtime)
{
    public sealed record Summary(double Mean, double Min, double P50, double P95, double P99, double Max)
    {
        public static Summary Of(IEnumerable<double> values)
        {
            double[] sorted = values.Order().ToArray();
            return new(Round(sorted.Average()), Round(sorted[0]), Round(Percentile(sorted, 0.5)),
                Round(Percentile(sorted, 0.95)), Round(Percentile(sorted, 0.99)), Round(sorted[^1]));
        }
    }

    /// <summary>
    /// The .NET runtime over the frames: managed bytes allocated by the main thread per frame, GC pause
    /// per frame, the heap's size, and in all, bytes allocated by all threads (to within the runtime's
    /// allocation chunks), garbage collections and JIT-compiled methods.
    /// </summary>
    public sealed record RuntimeStats(
        Summary AllocatedBytes,
        long AllThreadsAllocatedBytes,
        Summary GcPauseMs,
        Summary HeapBytes,
        GcCounts GcCollections,
        long JitMethods);

    /// <summary>The statistics of <paramref name="samples"/> (at least one), against a budget in milliseconds.</summary>
    public static FrameStats Of(IReadOnlyCollection<FrameSample> samples, double budgetMs)
    {
        double[] gpu = samples.Where(s => s.GpuMs != null).Select(s => s.GpuMs!.Value).ToArray();
        double[] frameMs = samples.Select(s => s.FrameMs).Order().ToArray();
        return new(
            samples.Count,
            Round(1000 * samples.Count / frameMs.Sum()),
            Round(1000 / Percentile(frameMs, 0.99)),
            Round(1000 / Percentile(frameMs, 0.999)),
            Round(budgetMs),
            Summary.Of(samples.Select(s => Math.Max(s.CpuMs, s.GpuMs ?? 0) / budgetMs)),
            Summary.Of(frameMs),
            Summary.Of(samples.Select(s => s.CpuMs)),
            gpu.Length > 0 ? Summary.Of(gpu) : null,
            samples.Count(s => s.FrameMs > 1.5 * budgetMs),
            new RuntimeStats(
                Summary.Of(samples.Select(s => (double)s.Runtime.AllocatedBytes)),
                samples.Sum(s => s.Runtime.AllThreadsAllocatedBytes),
                Summary.Of(samples.Select(s => s.Runtime.GcPause.TotalMilliseconds)),
                Summary.Of(samples.Select(s => (double)s.Runtime.HeapBytes)),
                samples.Aggregate(default(GcCounts), (total, s) => total + s.Runtime.Collections),
                samples.Sum(s => s.Runtime.JitMethods)));
    }

    // Nearest-rank percentile of sorted values.
    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static double Round(double value) => Math.Round(value, 3);
}
