using Aether.Modding;
using Terraria;

namespace Aether.Api;

public readonly record struct WorldInfo(string Name, int Id, bool IsMultiplayer);

public sealed class ApiEvent<T>
{
    private readonly List<Listener<T>> listeners = [];

    internal ApiEvent() { }

    public void Register(Action<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        listeners.Add(new Listener<T>(Registration.Owner(), callback));
    }

    internal void Raise(T value)
    {
        foreach (var listener in listeners)
            listener.Invoke(value);
    }
}

public static class ClientEvents
{
    public static ApiEvent<EventArgs> Ready { get; } = new();
}

public static class ServerEvents
{
    public static bool IsDedicatedServer => ClientContent.IsDedicatedServer;
    public static ApiEvent<EventArgs> Ready { get; } = new();
    public static ApiEvent<Player> PlayerJoined { get; } = new();
}

public static class WorldEvents
{
    public static ApiEvent<WorldInfo> Entered { get; } = new();
    public static ApiEvent<WorldInfo> Left { get; } = new();
    public static ApiEvent<WorldInfo> Tick { get; } = new();
}

internal static class Registration
{
    private static int gameThread = -1;

    internal static void SetGameThread() => gameThread = Environment.CurrentManagedThreadId;

    internal static void RequireGameThread()
    {
        if (Environment.CurrentManagedThreadId != gameThread)
            throw new InvalidOperationException("Call the gameplay API on the game thread.");
    }

    internal static string Owner()
    {
        if (Environment.CurrentManagedThreadId != gameThread || ModCatalog.InitializingModId is not { } owner)
            throw new InvalidOperationException("Register API callbacks and content from the mod initializer.");
        return owner;
    }

    internal static void Add<T>(Dictionary<string, T> entries, string id, T entry, string owner)
    {
        if (!id.StartsWith(owner + ":", StringComparison.Ordinal) || id.Length == owner.Length + 1 ||
            id[(owner.Length + 1)..].Any(c => !IsIdCharacter(c)))
            throw new ArgumentException($"Registration ID must use '{owner}:name' with a lowercase name.", nameof(id));
        if (!entries.TryAdd(id, entry))
            throw new InvalidOperationException($"Registration '{id}' already exists.");
    }

    internal static void RequireContentId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int colon = id.IndexOf(':');
        if (colon is < 1 or > 64 || colon == id.Length - 1 || id.Length > 1024 || !char.IsAsciiLetterLower(id[0]))
            throw new ArgumentException("Use a namespaced content ID in the form mod-id:name.", nameof(id));
        for (int index = 0; index < id.Length; index++)
        {
            if (index != colon && !IsIdCharacter(id[index]))
                throw new ArgumentException("Use a namespaced content ID in the form mod-id:name.", nameof(id));
        }
    }

    internal static bool IsIdCharacter(char c) => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.';
}

internal sealed class Listener<T>(string owner, Action<T> callback)
{
    internal string Owner { get; } = owner;
    private bool enabled = true;

    internal void Invoke(T value)
    {
        if (!enabled)
            return;
        try { callback(value); }
        catch (Exception exception)
        {
            enabled = false;
            Console.Error.WriteLine($"Aether API disabled a callback from '{Owner}': {exception}");
        }
    }
}
