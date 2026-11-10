using Serilog;

namespace Aether.Modding;

/// <summary>
/// A mod's logger: its events go to the console and log file with Aether's and the game's, named by
/// the mod's ID. Messages are templates whose named holes take the values in order, kept as data
/// in structured sinks: <c>log.Information("Loaded {Count} recipes", count)</c>.
/// </summary>
public sealed class ModLog
{
    private readonly ILogger logger;

    private ModLog(ILogger logger)
    {
        this.logger = logger;
    }

    /// <summary>A logger for the mod <paramref name="modId" />.</summary>
    public static ModLog For(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return new ModLog(AetherLog.For(modId).ForContext("Mod", modId));
    }

    /// <summary>Detail for diagnosing problems (the log file; the console with <c>-mixindebug</c>).</summary>
    public void Debug(string messageTemplate, params object?[] values) => logger.Debug(messageTemplate, values);

    public void Information(string messageTemplate, params object?[] values) => logger.Information(messageTemplate, values);

    public void Warning(string messageTemplate, params object?[] values) => logger.Warning(messageTemplate, values);

    public void Warning(Exception exception, string messageTemplate, params object?[] values) => logger.Warning(exception, messageTemplate, values);

    public void Error(string messageTemplate, params object?[] values) => logger.Error(messageTemplate, values);

    public void Error(Exception exception, string messageTemplate, params object?[] values) => logger.Error(exception, messageTemplate, values);
}
