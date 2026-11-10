using Aether.Modding;

namespace Aether;

/// <summary>The game's own log (what it used to print to the console), through <see cref="AetherLog" />.</summary>
internal static unsafe class GameLog
{
    /// <summary>Events from Terraria's code.</summary>
    internal static Serilog.ILogger Terraria => AetherLog.For("Terraria");

    // Kept alive: native SDL holds it.
    private static readonly SDL3.SDL.SDL_LogOutputFunction SdlOutput = LogSdl;

    /// <summary>
    /// Has FNA (and FNA3D and FAudio through it) and SDL log here too, from the "FNA" and "SDL" sources,
    /// instead of printing.
    /// </summary>
    internal static void CaptureLibraries()
    {
        SDL3.SDL.SDL_SetLogOutputFunction(SdlOutput, System.IntPtr.Zero);
        Microsoft.Xna.Framework.FNALoggerEXT.LogInfo = message => AetherLog.For("FNA").Information("{Text:l}", message);
        Microsoft.Xna.Framework.FNALoggerEXT.LogWarn = message => AetherLog.For("FNA").Warning("{Text:l}", message);
        Microsoft.Xna.Framework.FNALoggerEXT.LogError = message => AetherLog.For("FNA").Error("{Text:l}", message);
    }

    private static unsafe void LogSdl(System.IntPtr userdata, int category, SDL3.SDL.SDL_LogPriority priority, byte* message)
    {
        Serilog.Events.LogEventLevel level = priority switch
        {
            <= SDL3.SDL.SDL_LogPriority.SDL_LOG_PRIORITY_DEBUG => Serilog.Events.LogEventLevel.Debug,
            SDL3.SDL.SDL_LogPriority.SDL_LOG_PRIORITY_INFO => Serilog.Events.LogEventLevel.Information,
            SDL3.SDL.SDL_LogPriority.SDL_LOG_PRIORITY_WARN => Serilog.Events.LogEventLevel.Warning,
            SDL3.SDL.SDL_LogPriority.SDL_LOG_PRIORITY_ERROR => Serilog.Events.LogEventLevel.Error,
            _ => Serilog.Events.LogEventLevel.Fatal
        };
        AetherLog.For("SDL").Write(level, "{Text:l}", System.Runtime.InteropServices.Marshal.PtrToStringUTF8((System.IntPtr)message));
    }
}
