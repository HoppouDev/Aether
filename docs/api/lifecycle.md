# Lifecycle events

Register listeners in `IModInitializer.Initialize()`.
Aether calls listeners on the game thread in registration order.
A listener that throws is logged with its mod ID and disabled for the session.

```csharp
using Aether.Api;

ClientEvents.Ready.Register(_ => Console.WriteLine("Client ready"));
WorldEvents.Entered.Register(world => Console.WriteLine($"Entered {world.Name}"));
WorldEvents.Left.Register(world => Console.WriteLine($"Left {world.Name}"));
```

| Event | Timing |
| --- | --- |
| `ClientEvents.Ready` | Once after the engine has loaded and the title menu has drawn. |
| `WorldEvents.Entered` | Once when the client reaches playable state or the dedicated server starts the world. |
| `WorldEvents.Left` | Once when the world session ends, including normal client exit or dedicated server shutdown. |
| `WorldEvents.TickStart` | Before a gameplay update in the active world, ahead of players, NPCs, and projectiles. Paused gameplay, debugger world steps, and replay updates are skipped. |
| `WorldEvents.TickEnd` | After a gameplay update in the active world. Skipped in the same cases as `TickStart`. |
| `ServerEvents.Ready` | Once after a dedicated server loads the world and opens its listener. |
| `ServerEvents.PlayerJoined` | After an accepted player finishes joining the world. The listener receives the server's live `Terraria.Player` instance. |

World listeners receive a `WorldInfo` value with the display name, world ID, and multiplayer flag.
The `Entered` and `Left` events delimit a session.
`TickStart` begins on the first update after `Entered`, so the entering update raises only `TickEnd`.
Use `Left` to release resources you acquired in `Entered`.

`ServerEvents.IsDedicatedServer` is available during initialization so a mod can register side-specific listeners.
Register the same custom item and entity definitions on the client and server.
See [dedicated servers](server.md) for launch arguments and connection rules.
