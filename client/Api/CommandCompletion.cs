using System.ComponentModel;
using Aether.Modding;
using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using Brigadier.NET.Exceptions;
using Brigadier.NET.Suggestion;
using Brigadier.NET.Tree;
using Terraria;
using Terraria.Chat;
using Terraria.Chat.Commands;
using Terraria.GameContent.UI;
using Terraria.Localization;
using Terraria.UI.Chat;

namespace Aether.Api;

/// <summary>A completion for chat input: puts <see cref="Text" /> in place of the text from <see cref="Start" /> to <see cref="End" />.</summary>
/// <param name="Start">Where the replaced text starts in the chat input (the slash is at 0).</param>
/// <param name="End">Where the replaced text ends (the cursor the completion was asked for).</param>
/// <param name="Text">The completed word.</param>
/// <param name="Tooltip">What the suggestion is, or null.</param>
public sealed record CommandSuggestion(int Start, int End, string Text, string? Tooltip)
{
    /// <summary>The chat input with this completion in it.</summary>
    public string Apply(string input) => input[..Start] + Text + input[End..];
}

/// <summary>A line to show when nothing is suggested: the usage of the arguments that follow, or why the command doesn't parse.</summary>
public readonly record struct CommandHint(string Text, bool IsError);

/// <summary>What <see cref="CommandCompletion.Complete" /> found for the chat input.</summary>
public sealed class CommandCompletions
{
    internal CommandCompletions(IReadOnlyList<CommandSuggestion> suggestions, int start, IReadOnlyList<CommandHint> hints, int hintStart)
    {
        Suggestions = suggestions;
        Start = start;
        Hints = hints;
        HintStart = hintStart;
    }

    /// <summary>Nothing to suggest or hint.</summary>
    public static CommandCompletions None { get; } = new([], 0, [], 0);

    /// <summary>The suggestions in order; they all replace the text from <see cref="Start" /> to the cursor.</summary>
    public IReadOnlyList<CommandSuggestion> Suggestions { get; }

    /// <summary>Where the word being completed starts in the chat input.</summary>
    public int Start { get; }

    /// <summary>The usage or errors to show instead of suggestions, with the cursor at the end of the input (otherwise none).</summary>
    public IReadOnlyList<CommandHint> Hints { get; }

    /// <summary>Where the hints belong in the chat input: the argument they describe, or 0 for errors.</summary>
    public int HintStart { get; }
}

/// <summary>
/// Completion of chat commands for a chat UI to show, the way Brigadier completes them: command
/// names from both registries and Terraria's own commands (leaving out what the local player can't
/// use), then the command's literals and arguments, including the suggestions mods add with
/// <c>Suggests</c>. Terraria's commands are completed from a tree that only describes them
/// (<see cref="Vanilla" />): emoji names for /emoji, a message for /p and /me.
/// </summary>
public static class CommandCompletion
{
    // Terraria's commands as Brigadier sees them, for the language they were built in, and their descriptions.
    private static CommandDispatcher<CommandSource>? vanilla;
    private static string? vanillaCulture;
    private static readonly Dictionary<string, string> vanillaDescriptions = new(StringComparer.Ordinal);

    /// <summary>
    /// Completes chat input <paramref name="text" /> at <paramref name="cursor" />, on the game thread.
    /// Text that isn't a command gets <see cref="CommandCompletions.None" />. The task is usually
    /// complete already; a mod's suggestion provider can make it finish later.
    /// </summary>
    public static Task<CommandCompletions> Complete(string text, int cursor)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cursor < 1 || cursor > text.Length || text[0] != '/')
            return Task.FromResult(CommandCompletions.None);
        // Brigadier's positions are in the command, after the slash.
        string command = text[1..];
        try
        {
            Parsed? parsed = Parse(command);
            var hints = new List<CommandHint>();
            int hintStart = parsed != null && cursor == text.Length ? Hint(parsed, hints) + 1 : 0;
            return Finish(Suggest(parsed, command, cursor - 1), hints, hintStart);
        }
        catch (Exception exception)
        {
            AetherLog.For("Aether.Api").Warning(exception, "Completing '{Input}' failed", text);
            return Task.FromResult(CommandCompletions.None);
        }
    }

    private static async Task<CommandCompletions> Finish(Task<Suggestions> pending, List<CommandHint> hints, int hintStart)
    {
        Suggestions suggestions = await pending;
        var list = suggestions.List.Select(suggestion => new CommandSuggestion(suggestion.Range.Start + 1, suggestion.Range.End + 1,
            suggestion.Text, suggestion.Tooltip?.String)).ToArray();
        return new CommandCompletions(list, suggestions.Range.Start + 1, hints, hintStart);
    }

    // The commands that take the first word, in the order Enter tries them: client commands, then
    // Terraria's, then server commands.
    private static Parsed? Parse(string command)
    {
        int space = command.IndexOf(' ');
        string name = space < 0 ? command : command[..space];
        var client = new CommandSource(Main.LocalPlayer, isServer: false);
        if (ClientCommands.Registry.Has(name))
            return Parsed.Of(ClientCommands.Registry.Dispatcher, command, client, strict: true);
        // Terraria ignores what its commands don't use, so nothing is an error there.
        if (Vanilla().Root.GetChild(name) != null)
            return Parsed.Of(Vanilla(), command, client, strict: false);
        if (ServerCommands.Registry.Has(name))
            return Parsed.Of(ServerCommands.Registry.Dispatcher, command, new CommandSource(Main.LocalPlayer, isServer: true), strict: true);
        return null;
    }

    private static Task<Suggestions> Suggest(Parsed? parsed, string command, int cursor)
    {
        string typed = command[..cursor];
        if (!typed.Contains(' '))
            return Task.FromResult(Names(typed));
        if (parsed == null)
            return Task.FromResult(new Suggestions(StringRange.At(cursor), []));
        // Brigadier's own completion, leaving out what the sender can't use.
        CommandContextBuilder<CommandSource> context = parsed.Results.Context;
        SuggestionContext<CommandSource> at = context.FindSuggestionContext(cursor);
        int start = Math.Min(at.StartPos, cursor);
        string lower = typed.ToLowerInvariant();
        var tasks = new List<Task<Suggestions>>();
        foreach (CommandNode<CommandSource> child in at.Parent.Children)
        {
            if (!child.CanUse(parsed.Source))
                continue;
            try
            {
                tasks.Add(child.ListSuggestions(context.Build(typed), new SuggestionsBuilder(typed, lower, start)));
            }
            catch (CommandSyntaxException)
            {
            }
            catch (Exception exception)
            {
                AetherLog.For("Aether.Api").Warning(exception, "Suggestions for '/{Command}' failed", command);
            }
        }
        return Merge(command, tasks);
    }

    private static async Task<Suggestions> Merge(string command, List<Task<Suggestions>> tasks)
    {
        var results = new List<Suggestions>();
        foreach (Task<Suggestions> task in tasks)
        {
            try
            {
                results.Add(await task);
            }
            catch (CommandSyntaxException)
            {
            }
            catch (Exception exception)
            {
                AetherLog.For("Aether.Api").Warning(exception, "Suggestions for '/{Command}' failed", command);
            }
        }
        return Suggestions.Merge(command, results);
    }

    // Command names: both registries' (and their short names), and Terraria's.
    private static Suggestions Names(string typed)
    {
        var builder = new SuggestionsBuilder(typed, 0);
        var client = new CommandSource(Main.LocalPlayer, isServer: false);
        AddNames(builder, ClientCommands.Registry.Dispatcher, client);
        AddNames(builder, Vanilla(), client);
        AddNames(builder, ServerCommands.Registry.Dispatcher, new CommandSource(Main.LocalPlayer, isServer: true));
        return builder.Build();
    }

    private static void AddNames(SuggestionsBuilder builder, CommandDispatcher<CommandSource> dispatcher, CommandSource source)
    {
        foreach (CommandNode<CommandSource> node in dispatcher.Root.Children)
        {
            if (!node.CanUse(source) || !node.Name.StartsWith(builder.RemainingLowerCase, StringComparison.OrdinalIgnoreCase))
                continue;
            if (dispatcher == vanilla && vanillaDescriptions.TryGetValue(node.Name, out string? description))
                builder.Suggest(node.Name, new LiteralMessage(description));
            else
                builder.Suggest(node.Name);
        }
    }

    /// <summary>
    /// Terraria's chat commands, in the current language, as a Brigadier tree for completion only
    /// (Terraria runs them itself): emoji names for /emoji, a message for /p and /me.
    /// </summary>
    private static CommandDispatcher<CommandSource> Vanilla()
    {
        string culture = Language.ActiveCulture.Name;
        if (vanilla != null && vanillaCulture == culture)
            return vanilla;
        var dispatcher = new CommandDispatcher<CommandSource>();
        vanillaDescriptions.Clear();
        foreach ((LocalizedText text, ChatCommandId id) in ((IChatCommandProcessorAccess)(object)ChatManager.Commands).GetLocalizedCommands())
        {
            string value = text.Value;
            if (value.Length < 2 || value[0] != '/' || value.Contains(' '))
                continue;
            string name = value[1..];
            // The commands with an argument do nothing without it.
            var literal = new LiteralArgumentBuilder<CommandSource>(name);
            if (id.Equals(ChatCommandId.FromType<EmojiCommand>()))
                literal.Then(RequiredArgumentBuilder<CommandSource, string>.RequiredArgument("emoji", Arguments.GreedyString())
                    .Suggests(SuggestEmoji).Executes(_ => 0));
            else if (id.Equals(ChatCommandId.FromType<PartyChatCommand>()) || id.Equals(ChatCommandId.FromType<EmoteCommand>()))
                literal.Then(RequiredArgumentBuilder<CommandSource, string>.RequiredArgument("message", Arguments.GreedyString()).Executes(_ => 0));
            else
                literal.Executes(_ => 0);
            dispatcher.Register(literal);
            // ChatCommand.Emoji_1 is described by ChatCommandDescription.Emoji.
            string key = text.Key["ChatCommand.".Length..];
            int variant = key.IndexOf('_');
            string description = "ChatCommandDescription." + (variant < 0 ? key : key[..variant]);
            if (Language.Exists(description))
                vanillaDescriptions[name] = Language.GetTextValue(description);
        }
        vanillaCulture = culture;
        return vanilla = dispatcher;
    }

    private static Task<Suggestions> SuggestEmoji(CommandContext<CommandSource> context, SuggestionsBuilder builder)
    {
        for (int emote = 0; emote < EmoteID.Count; emote++)
        {
            LocalizedText name = Lang.GetEmojiName(emote);
            if (name != LocalizedText.Empty && name.Value.StartsWith(builder.RemainingLowerCase, StringComparison.OrdinalIgnoreCase))
                builder.Suggest(name.Value);
        }
        return builder.BuildAsync();
    }

    // What to show with nothing to suggest: the parse errors (from the start), the next arguments'
    // usage (from where they go), or what's left over. Returns where the hints go in the command.
    private static int Hint(Parsed parsed, List<CommandHint> hints)
    {
        ParseResults<CommandSource> results = parsed.Results;
        if (!parsed.Strict)
            return ArgumentUsage(parsed, hints);
        bool trailing = false;
        if (results.Exceptions.Count > 0)
        {
            int literals = 0;
            foreach (CommandSyntaxException exception in results.Exceptions.Values)
            {
                if (exception.Type == CommandSyntaxException.BuiltInExceptions.LiteralIncorrect())
                    literals++;
                else
                    hints.Add(new CommandHint(exception.Message, IsError: true));
            }
            if (literals > 0)
                hints.Add(new CommandHint(CommandSyntaxException.BuiltInExceptions.DispatcherUnknownCommand().Create().Message, IsError: true));
        }
        else if (results.Reader.CanRead())
        {
            trailing = true;
        }
        if (hints.Count > 0)
            return -1;
        int start = ArgumentUsage(parsed, hints);
        if (hints.Count > 0 || !trailing)
            return start;
        SimpleCommandExceptionType error = results.Context.Range.IsEmpty
            ? CommandSyntaxException.BuiltInExceptions.DispatcherUnknownCommand()
            : CommandSyntaxException.BuiltInExceptions.DispatcherUnknownArgument();
        hints.Add(new CommandHint(error.CreateWithContext(results.Reader).Message, IsError: true));
        return -1;
    }

    private static int ArgumentUsage(Parsed parsed, List<CommandHint> hints)
    {
        SuggestionContext<CommandSource> at = parsed.Results.Context.FindSuggestionContext(parsed.Results.Reader.TotalLength);
        foreach ((CommandNode<CommandSource> node, string text) in parsed.Dispatcher.GetSmartUsage(at.Parent, parsed.Source))
        {
            if (node is not LiteralCommandNode<CommandSource>)
                hints.Add(new CommandHint(text, IsError: false));
        }
        return at.StartPos;
    }

    // Strict: what doesn't parse is an error (it is for registered commands).
    private sealed record Parsed(CommandDispatcher<CommandSource> Dispatcher, ParseResults<CommandSource> Results, CommandSource Source, bool Strict)
    {
        internal static Parsed Of(CommandDispatcher<CommandSource> dispatcher, string command, CommandSource source, bool strict) =>
            new(dispatcher, dispatcher.Parse(command, source), source, strict);
    }
}

/// <summary>Terraria's chat command names, for completion.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Mixin(typeof(ChatCommandProcessor))]
public interface IChatCommandProcessorAccess
{
    [Accessor("_localizedCommands")] Dictionary<LocalizedText, ChatCommandId> GetLocalizedCommands();
}
