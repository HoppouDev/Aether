using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Aether.Profiling;

/// <summary>
/// Frames' measurements (<see cref="FrameSample"/>) as a CSV log, one row per frame, like MangoHud's
/// logs: elapsed time, frame rate and time, CPU and GPU time and load (their share of the frame), and
/// the .NET runtime's allocations, collections, GC pause, heap size and JIT compiles. GPU columns are
/// empty where the driver can't time it; all threads' allocations move in steps of about 8 KB (see
/// RuntimeCounters).
/// </summary>
public static class FrameLog
{
    private const string Header = "frame,elapsed_ms,fps,frametime_ms,cpu_ms,cpu_load,gpu_ms,gpu_load," +
        "alloc_bytes,alloc_all_threads_bytes,gc0,gc1,gc2,gc_pause_ms,heap_bytes,jit_methods";

    public static void Write(string path, IEnumerable<FrameSample> samples)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(Header);
        CultureInfo invariant = CultureInfo.InvariantCulture;
        double elapsed = 0;
        int frame = 0;
        foreach (FrameSample s in samples)
        {
            elapsed += s.FrameMs;
            RuntimeCounters r = s.Runtime;
            string gpu = s.GpuMs is double ms ? string.Create(invariant, $"{ms:0.###},{ms / s.FrameMs:0.###}") : ",";
            writer.WriteLine(string.Create(invariant,
                $"{frame++},{elapsed:0.###},{1000 / s.FrameMs:0.##},{s.FrameMs:0.###},{s.CpuMs:0.###},{s.CpuMs / s.FrameMs:0.###},{gpu}," +
                $"{r.AllocatedBytes},{r.AllThreadsAllocatedBytes},{r.Collections.Gen0},{r.Collections.Gen1},{r.Collections.Gen2}," +
                $"{r.GcPause.TotalMilliseconds:0.###},{r.HeapBytes},{r.JitMethods}"));
        }
    }
}
