# Commands

Register chat commands in the mod initializer with [Brigadier.NET](https://github.com/AtomicBlom/Brigadier.NET), a port of Mojang's Brigadier command parser.
Each command is a tree of literals and typed arguments with handlers.

| Registry | Where it runs |
| --- | --- |
| `ClientCommands` | On the local client, before the chat message is sent. It works on any server, including vanilla servers. |
| `ServerCommands` | On the world's simulation owner, locally in single-player or on an Aether dedicated server for connected players using its mod profile. |

```csharp
using Aether.Api;
using Brigadier.NET;
using Brigadier.NET.Builder;

ClientCommands.Register("example:echo", command => command
    .Then(c => c.Argument("text", Arguments.GreedyString())
        .Executes(context =>
        {
            context.Source.Reply(Arguments.GetString(context, "text"));
            return 1;
        })));

ServerCommands.Register("example:heal", command => command
    .Executes(context =>
    {
        context.Source.Player.Heal(context.Source.Player.statLifeMax2);
        return 1;
    }));
```

The callback receives the command's root literal and returns it after adding arguments, subcommands, and handlers.
The ID uses the `your-mod:name` form of other registrations, and it is the command's full name: `/example:echo hello`.
When only one mod registers a name in a registry, the name also works on its own: `/echo hello`.
Client commands take precedence over Terraria chat commands and server commands.
Terraria parses built-in chat commands before forwarding chat to the server registry, so use a server command's full name when its short name overlaps a built-in command.

## Sources and replies

`context.Source` is a `CommandSource`.
`Player` is the local player for a client command and the sender for a server command.
`IsServer` is `true` for server commands.
`Reply` and `ReplyError` show text in the sender's chat, sending it over the network from a dedicated server.
Use `Requires` on a node to limit who can run it; a sender who fails the requirement gets Brigadier's unknown-command error.

## Completion

`CommandCompletion.Complete(text, cursor)` completes chat input for a chat UI to show, the way Minecraft completes commands; the API draws nothing itself ([FreshUI](https://github.com/HoppouDev/FreshUI) shows it).
Call it on the game thread with the input, slash included, and the cursor's position in it.
It returns a task that is usually already complete; a mod's suggestion provider can make it finish later.

```csharp
CommandCompletions completions = await CommandCompletion.Complete("/example:buff ir", 16);
foreach (CommandSuggestion suggestion in completions.Suggestions)
    Console.WriteLine($"{suggestion.Apply("/example:buff ir")} ({suggestion.Tooltip})");
```

Each `CommandSuggestion` replaces the text from `Start` to `End` (the cursor) with `Text`; `Apply` returns the input with it.
Positions count the slash, so a suggestion for the command's name starts at 1.
`Start` on `CommandCompletions` is where the word being completed starts, to place a list under it.
With nothing to suggest and the cursor at the end of the input, `Hints` holds the usage of the arguments that follow (from `HintStart`), or why the command doesn't parse (`IsError`, from 0).
Text that isn't a command gets `CommandCompletions.None`.

Command names come from both registries and Terraria's chat commands, leaving out commands whose `Requires` fails for the local player.
After the name, the command's own registry completes literals and arguments.
Terraria's commands complete too: emoji names after `/emoji` and `/e`, and a `<message>` hint after `/p` and `/me`; text after them is never an error, since Terraria ignores what they don't use.
Add suggestions to an argument with Brigadier's `Suggests`; a suggestion's tooltip is passed along:

```csharp
ClientCommands.Register("example:buff", command => command
    .Then(c => c.Argument("buff", Arguments.Word())
        .Suggests((context, builder) =>
        {
            foreach (string name in new[] { "ironskin", "swiftness" })
            {
                if (name.StartsWith(builder.RemainingLowerCase, StringComparison.Ordinal))
                    builder.Suggest(name, new LiteralMessage($"The {name} buff"));
            }
            return builder.BuildAsync();
        })
        .Executes(context => 1)));
```

An exception from a provider is logged and its suggestions are left out.
Server commands are completed from the client's own registrations, so in multiplayer they assume the server runs the same mods.

## Errors

Chat whose first word is not a registered command is sent or said as usual.
A syntax error, such as a missing or invalid argument, is shown to the sender in red with Brigadier's message and position.
An exception from a handler is logged with the mod ID and reported to the sender, and the command stays registered.
Handlers return Brigadier's integer result, which Aether does not use.

## References

The API package ships `Brigadier.NET.dll` next to `Aether.Api.dll` and shares it with every mod that depends on `aether-api`.
The mod template references both as compile-only assemblies.
For a manual setup, reference the `Brigadier.NET.dll` from the same API build and keep it out of your archive.
