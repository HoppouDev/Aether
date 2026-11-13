using System;
using System.Runtime;

namespace Aether.Profiling;

/// <summary>
/// The .NET runtime's counters: managed bytes allocated (by the calling thread, and by all), garbage
/// collections and the time they paused the game, the managed heap's size, and methods JIT-compiled.
/// Reading them doesn't allocate. AllThreadsAllocatedBytes moves in steps of the runtime's allocation
/// chunks (about 8 KB a thread), so it's only meaningful summed over many frames.
/// </summary>
public readonly record struct RuntimeCounters(
    long AllocatedBytes,
    long AllThreadsAllocatedBytes,
    GcCounts Collections,
    TimeSpan GcPause,
    long HeapBytes,
    long JitMethods)
{
    public static RuntimeCounters Now() => new(
        GC.GetAllocatedBytesForCurrentThread(),
        // Not precise: that suspends every managed thread, which per frame would disturb what's measured.
        GC.GetTotalAllocatedBytes(),
        GcCounts.Now,
        GC.GetTotalPauseDuration(),
        GC.GetTotalMemory(forceFullCollection: false),
        JitInfo.GetCompiledMethodCount());

    /// <summary>What changed since <paramref name="before"/>; HeapBytes stays this one's (it's a size, not a count).</summary>
    public RuntimeCounters Since(RuntimeCounters before) => new(
        AllocatedBytes - before.AllocatedBytes,
        AllThreadsAllocatedBytes - before.AllThreadsAllocatedBytes,
        Collections - before.Collections,
        GcPause - before.GcPause,
        HeapBytes,
        JitMethods - before.JitMethods);
}

/// <summary>Garbage collections so far, by generation; subtract two to get those between them.</summary>
public readonly record struct GcCounts(int Gen0, int Gen1, int Gen2)
{
    public static GcCounts Now => new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public static GcCounts operator -(GcCounts after, GcCounts before) =>
        new(after.Gen0 - before.Gen0, after.Gen1 - before.Gen1, after.Gen2 - before.Gen2);

    public static GcCounts operator +(GcCounts a, GcCounts b) => new(a.Gen0 + b.Gen0, a.Gen1 + b.Gen1, a.Gen2 + b.Gen2);
}
