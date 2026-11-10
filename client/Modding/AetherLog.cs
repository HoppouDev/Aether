using System.Text;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Aether.Modding;

/// <summary>
/// Logging for Terraria and Aether, through Serilog. The game writes to the console throughout
/// (<c>Console.WriteLine</c>, <c>Console.Error</c>): that output still reaches the console unchanged,
/// as it's written (prompts included), and each line is also logged, from the "Console" source.
/// Events logged directly are written to standard error (standard output stays the game's own), and
/// with Terraria's <c>-logfile</c> everything also goes to a file, from the start: events from before
/// the game opens it (the bootstrap's) are kept until then.
/// </summary>
public static class AetherLog
{
    /// <summary>The source of the console's own lines.</summary>
    public const string ConsoleSource = "Console";

    private const string ConsoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private const string FileTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private static readonly object Gate = new();

    private static LineWriter? _out;

    private static LineWriter? _error;

    // One logger for the whole run (loggers taken from it stay valid); the file comes and goes in its sink.
    private static readonly FileSink FileOutput = new();

    // The console shows Information and above, Debug too with -mixindebug; the file has everything.
    private static readonly LoggingLevelSwitch ConsoleLevel = new(LogEventLevel.Information);

    /// <summary>The least severe events the console shows (the file has all of them).</summary>
    public static LogEventLevel ConsoleMinimum
    {
        get => ConsoleLevel.MinimumLevel;
        set => ConsoleLevel.MinimumLevel = value;
    }

    /// <summary>A logger whose events name <paramref name="source" /> (Serilog's SourceContext).</summary>
    public static ILogger For(string source)
    {
        Start();
        return Log.ForContext(Constants.SourceContextPropertyName, source);
    }

    /// <summary>Starts logging and captures the console, once (the bootstrap does it first thing).</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_out != null)
                return;
            TextWriter output = Console.Out, error = Console.Error;
            _out = new LineWriter(output, LogEventLevel.Information);
            _error = new LineWriter(error, LogEventLevel.Error);
            Console.SetOut(_out);
            Console.SetError(_error);
            // Console's synchronized wrapper locks itself: the sink takes the same lock, so its events
            // and the game's own writes to standard error don't interleave.
            TextWriter errorLock = Console.Error;
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty(Constants.SourceContextPropertyName, "Aether")
                .WriteTo.Logger(console => console
                    .MinimumLevel.ControlledBy(ConsoleLevel)
                    .Filter.ByExcluding(IsConsoleLine)
                    .WriteTo.Sink(new ConsoleSink(error, errorLock, new MessageTemplateTextFormatter(ConsoleTemplate))))
                .WriteTo.Sink(FileOutput)
                .CreateLogger();
        }
    }

    /// <summary>
    /// Also writes everything to <paramref name="path" />, the events kept from before first
    /// (Terraria's <c>-logfile</c>). A file that can't be opened is reported and skipped, as vanilla did.
    /// </summary>
    public static void WriteToFile(string path)
    {
        Start();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            FileOutput.Open(new LoggerConfiguration().MinimumLevel.Debug()
                .WriteTo.File(path, outputTemplate: FileTemplate, flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            For("Aether").Warning(exception, "Unable to log to the file {Path}", path);
        }
    }

    /// <summary>Drops the events kept for a file, when the game runs without one (no <c>-logfile</c>).</summary>
    public static void WithoutFile() => FileOutput.Discard();

    /// <summary>Logs any unfinished console line and writes out what's buffered (at exit).</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            // Console's synchronized wrappers lock themselves: the writers are only used under those locks.
            if (_out != null)
            {
                lock (Console.Out)
                    _out.LogPending();
                lock (Console.Error)
                    _error!.LogPending();
            }
            Log.CloseAndFlush();
            FileOutput.Close();
        }
    }

    // The console already shows its own lines.
    private static bool IsConsoleLine(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out LogEventPropertyValue? source) &&
        source is ScalarValue { Value: ConsoleSource };

    /// <summary>
    /// The log file: until it's opened, keeps the events (the first EarlyLimit) to write first; with
    /// none to come, drops them and keeps no more.
    /// </summary>
    private sealed class FileSink : ILogEventSink
    {
        private const int EarlyLimit = 10000;

        private List<LogEvent>? _early = new();

        private Logger? _file;

        public void Emit(LogEvent logEvent)
        {
            lock (this)
            {
                if (_file != null)
                    _file.Write(logEvent);
                else if (_early is { Count: < EarlyLimit })
                    _early.Add(logEvent);
            }
        }

        public void Open(Logger file)
        {
            lock (this)
            {
                _file?.Dispose();
                _file = file;
                if (_early != null)
                {
                    foreach (LogEvent logEvent in _early)
                        file.Write(logEvent);
                }
                _early = null;
            }
        }

        public void Discard()
        {
            lock (this)
                _early = null;
        }

        public void Close()
        {
            lock (this)
            {
                _file?.Dispose();
                _file = null;
                _early = null;
            }
        }
    }

    /// <summary>Writes events to the real standard error (not Console.Error, which logs what it's given).</summary>
    private sealed class ConsoleSink(TextWriter writer, object gate, ITextFormatter formatter) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            lock (gate)
            {
                formatter.Format(logEvent, writer);
                writer.Flush();
            }
        }
    }

    /// <summary>The console's writer: passes everything on as it comes, and logs each line once complete.</summary>
    private sealed class LineWriter(TextWriter inner, LogEventLevel level) : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            inner.Write(value);
            Append(value);
        }

        public override void Write(string? value)
        {
            inner.Write(value);
            if (value != null)
            {
                foreach (char c in value)
                    Append(c);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            inner.Write(buffer, index, count);
            for (int i = index; i < index + count; i++)
                Append(buffer[i]);
        }

        public override void Flush() => inner.Flush();

        /// <summary>Logs the line written so far, if it isn't finished (a prompt, the last output); hold Console's lock.</summary>
        public void LogPending()
        {
            if (_line.Length != 0)
                LogLine();
        }

        private void Append(char value)
        {
            if (value == '\n')
                LogLine();
            else
                _line.Append(value);
        }

        private void LogLine()
        {
            string line = _line.ToString().TrimEnd('\r');
            _line.Clear();
            Log.ForContext(Constants.SourceContextPropertyName, ConsoleSource).Write(level, "{Line:l}", line);
        }
    }
}
