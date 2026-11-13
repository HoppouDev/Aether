using System;
using System.Runtime.InteropServices;

namespace Aether.Profiling;

/// <summary>The calling thread's CPU time: what it has run, not counting time asleep or waiting.</summary>
public static class ThreadCpuTime
{
    // CLOCK_THREAD_CPUTIME_ID
    private const int LinuxThreadClock = 3, MacThreadClock = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clock, out Timespec time);

    [DllImport("kernel32")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32")]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    // shortcut: Windows counts thread time in scheduler ticks (~15.6 ms), so its frames read as 0 or a
    // tick; averages over many frames even that out. Use QueryThreadCycleTime if it ever matters.
    public static TimeSpan Current()
    {
        if (OperatingSystem.IsWindows())
        {
            GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user);
            return TimeSpan.FromTicks(kernel + user);
        }
        ClockGetTime(OperatingSystem.IsMacOS() ? MacThreadClock : LinuxThreadClock, out Timespec time);
        return TimeSpan.FromTicks(time.Seconds * TimeSpan.TicksPerSecond + time.Nanoseconds / 100);
    }
}
