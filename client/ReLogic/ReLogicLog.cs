using Serilog;
using Serilog.Core;

namespace ReLogic;

/// <summary>ReLogic's log (what it used to print to the console), on the logger Aether configures.</summary>
internal static class ReLogicLog
{
    internal static ILogger Log => Serilog.Log.ForContext(Constants.SourceContextPropertyName, "ReLogic");
}
