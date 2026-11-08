using System.ComponentModel;

namespace Aether.Modding;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class ClientStartup
{
    public static Action? InitializeMods { get; set; }
    public static Func<string, bool>? HandleLocalCommand { get; set; }
    public static Func<object, bool>? BlockCraft { get; set; }
    public static Func<object, bool>? UpdateRetainedUi { get; set; }
    public static Action<object>? DrawRetainedUi { get; set; }

    public static Dictionary<string, string> ParseArguments(string[] args, bool monoArgs)
    {
        if (monoArgs)
        {
            var normalized = new List<string>();
            var value = new List<string>();
            foreach (string argument in args)
            {
                if (argument.StartsWith('-'))
                {
                    normalized.Add(string.Join(" ", value));
                    normalized.Add(argument);
                    value.Clear();
                }
                else if (argument.Length > 0)
                {
                    value.Add(argument);
                }
            }
            normalized.Add(string.Join(" ", value));
            args = normalized.ToArray();
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        var words = new List<string>();
        foreach (string argument in args)
        {
            if (argument.Length == 0)
                continue;

            if (argument[0] is '-' or '+')
            {
                if (key != null)
                    parameters.Add(key.ToLowerInvariant(), string.Join(" ", words));

                key = argument;
                words.Clear();
            }
            else
            {
                words.Add(argument);
            }
        }

        if (key != null)
            parameters.Add(key.ToLowerInvariant(), string.Join(" ", words));

        return parameters;
    }

    public static string ResolveSavePath(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue("-savedirectory", out string? path))
            return path;

        string? home = Environment.GetEnvironmentVariable("HOME");
        string storage;
        if (OperatingSystem.IsWindows())
            storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "My Games");
        else if (OperatingSystem.IsMacOS())
            storage = string.IsNullOrEmpty(home) ? "." : Path.Combine(home, "Library/Application Support");
        else
        {
            string? data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            storage = !string.IsNullOrEmpty(data) ? data : string.IsNullOrEmpty(home) ? "." : Path.Combine(home, ".local/share");
        }

        return Path.Combine(storage, "Terraria");
    }
}

public interface IModInitializer
{
    void Initialize();
}
