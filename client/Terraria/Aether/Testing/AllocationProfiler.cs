using System;
using System.Collections.Generic;
using System.Reflection;

namespace Aether.Testing;

/// <summary>
/// Counts the bytes the main thread allocates, overall and per retained-mode screen, for the control
/// server's <c>alloc</c> command. Uses the runtime's GC.GetAllocatedBytesForCurrentThread (Mono has
/// it; the net40 reference assemblies don't), so it's unavailable where that's missing.
/// </summary>
public static class AllocationProfiler
{
    private static readonly Func<long> AllocatedBytes = Create();

    /// <summary>Whether screens are being measured (GameScreens and MenuScreens check it).</summary>
    public static bool Active;

    /// <summary>Bytes allocated while drawing each screen since <see cref="Reset" />.</summary>
    public static readonly Dictionary<object, long> ByScreen = new Dictionary<object, long>();

    public static bool Available => AllocatedBytes != null;

    private static Func<long> Create()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Static | BindingFlags.Public);
        if (method == null)
        {
            return null;
        }
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }

    /// <summary>The main thread's allocated bytes so far (0 if unavailable).</summary>
    public static long Now()
    {
        return AllocatedBytes?.Invoke() ?? 0;
    }

    public static void Reset()
    {
        ByScreen.Clear();
    }

    public static void Add(object screen, long bytes)
    {
        ByScreen.TryGetValue(screen, out var total);
        ByScreen[screen] = total + bytes;
    }

    /// <summary>Measures a block by name while profiling: <c>using (AllocationProfiler.Measure("name")) { ... }</c>.</summary>
    public static Section Measure(string name)
    {
        return new Section(name, Active ? Now() : 0);
    }

    public struct Section : IDisposable
    {
        private readonly string _name;

        private readonly long _start;

        public Section(string name, long start)
        {
            _name = name;
            _start = start;
        }

        public void Dispose()
        {
            if (Active)
            {
                Add(_name, Now() - _start);
            }
        }
    }

    private static long _lap;

    /// <summary>
    /// While profiling, counts the bytes allocated since the previous Lap (or <see cref="LapStart" />)
    /// under name: checkpoints dropped through a long method show which stretch allocates.
    /// </summary>
    public static void Lap(string name)
    {
        if (Active)
        {
            long now = Now();
            Add(name, now - _lap);
            _lap = now;
        }
    }

    public static void LapStart()
    {
        if (Active)
        {
            _lap = Now();
        }
    }
}
