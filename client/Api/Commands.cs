using Aether.Modding;
using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Exceptions;
using Brigadier.NET.Tree;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.Chat.Commands;
using Terraria.Localization;

namespace Aether.Api;

/// <summary>Who entered a command, and where its replies go.</summary>
public sealed class CommandSource
{
    private static readonly Color ErrorColor = new(255, 85, 85);

    internal CommandSource(Player player, bool isServer)
    {
        Player = player;
        IsServer = isServer;
    }

    /// <summary>The player who entered the command: the local player for a client command, the sender for a server command.</summary>
    public Player Player { get; }

    /// <summary>Whether the command runs on the world's simulation owner (a server command).</summary>
    public bool IsServer { get; }

    /// <summary>Shows <paramref name="text" /> in the sender's chat.</summary>
    public void Reply(string text) => Send(text, Color.White);

    /// <summary>Shows <paramref name="text" /> in the sender's chat as an error.</summary>
    public void ReplyError(string text) => Send(text, ErrorColor);

    private void Send(string text, Color color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ChatHelper.DisplayMessageOnClient(NetworkText.FromLiteral(text), color, Player.whoAmI);
    }
}

/// <summary>
/// Commands that run on the local client before chat is sent, so they work on any server.
/// </summary>
public static class ClientCommands
{
    internal static readonly CommandRegistry Registry = new();

    /// <summary>
    /// Registers <c>/mod-id:name</c>, and <c>/name</c> when no other mod registers that name.
    /// <paramref name="command" /> adds arguments and handlers to the command's literal and returns it.
    /// </summary>
    public static void Register(string id, Func<LiteralArgumentBuilder<CommandSource>, LiteralArgumentBuilder<CommandSource>> command) =>
        Registry.Register(id, command);

    internal static bool TryExecute(string text) =>
        text.StartsWith('/') && Registry.TryExecute(text[1..], () => new CommandSource(Main.LocalPlayer, isServer: false));
}

/// <summary>
/// Commands that run on the world's simulation owner: locally in single-player, and on an Aether
/// dedicated server for any player's chat.
/// </summary>
public static class ServerCommands
{
    internal static readonly CommandRegistry Registry = new();

    /// <summary>
    /// Registers <c>/mod-id:name</c>, and <c>/name</c> when the name is unique and Terraria does not recognize it as a chat command.
    /// <paramref name="command" /> adds arguments and handlers to the command's literal and returns it.
    /// </summary>
    public static void Register(string id, Func<LiteralArgumentBuilder<CommandSource>, LiteralArgumentBuilder<CommandSource>> command) =>
        Registry.Register(id, command);

    // Chat without a vanilla command (which Terraria says) that starts with a registered command.
    internal static bool TryExecute(ChatMessage message, int clientId)
    {
        if (Main.netMode == 1 || (uint)clientId >= Main.maxPlayers || !Main.player[clientId].active ||
            !message.CommandId.Equals(ChatCommandId.FromType<SayChatCommand>()) || !message.Text.StartsWith('/'))
            return false;
        if (!Registry.TryExecute(message.Text[1..], () => new CommandSource(Main.player[clientId], isServer: true)))
            return false;
        message.Consume();
        return true;
    }
}

internal sealed class CommandRegistry
{
    private readonly CommandDispatcher<CommandSource> dispatcher = new();
    private readonly Dictionary<string, string> owners = new(StringComparer.Ordinal);

    /// <summary>The commands registered (by full ID; aliases aren't counted).</summary>
    internal int Count => owners.Keys.Count(id => id.Contains(':'));

    internal CommandDispatcher<CommandSource> Dispatcher => dispatcher;

    /// <summary>Whether <paramref name="name" /> (a full ID or a short name) runs a registered command.</summary>
    internal bool Has(string name) => owners.ContainsKey(name) && dispatcher.Root.GetChild(name) != null;

    internal void Register(string id, Func<LiteralArgumentBuilder<CommandSource>, LiteralArgumentBuilder<CommandSource>> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string owner = Registration.Owner();
        Registration.Add(owners, id, owner, owner);
        LiteralArgumentBuilder<CommandSource> literal = command(new LiteralArgumentBuilder<CommandSource>(id));
        if (literal?.Literal != id)
            throw new ArgumentException($"Return the literal for '{id}' from the command builder.", nameof(command));
        dispatcher.Register(literal);
    }

    // After every initializer: each name only one mod registers also works without its namespace.
    internal void Freeze()
    {
        foreach (var group in owners.Keys.Where(id => id.Contains(':')).ToArray()
            .GroupBy(id => id[(id.IndexOf(':') + 1)..], StringComparer.Ordinal))
        {
            if (group.Count() != 1 || owners.ContainsKey(group.Key))
                continue;
            string id = group.Single();
            CommandNode<CommandSource> node = dispatcher.Root.GetChild(id)!;
            // A redirect alone continues into the target's children; the alias runs the target's own handler.
            var alias = new LiteralArgumentBuilder<CommandSource>(group.Key).Requires(node.Requirement);
            if (node.Command is { } handler)
                alias.Executes(handler);
            dispatcher.Register(alias.Redirect(node));
            owners[group.Key] = owners[id];
        }
    }

    // False when the first word isn't a registered command; otherwise runs it, replying with any error.
    internal bool TryExecute(string input, Func<CommandSource> source)
    {
        int end = input.IndexOf(' ');
        string name = end < 0 ? input : input[..end];
        if (!Has(name))
            return false;
        string owner = owners[name];
        CommandSource sender = source();
        try
        {
            dispatcher.Execute(dispatcher.Parse(input, sender));
        }
        catch (CommandSyntaxException exception)
        {
            sender.ReplyError(exception.Message);
        }
        catch (Exception exception)
        {
            AetherLog.For("Aether.Api").Error(exception, "Command '/{Command}' from '{Mod}' failed", name, owner);
            sender.ReplyError($"The command failed: {exception.Message}");
        }
        return true;
    }
}
