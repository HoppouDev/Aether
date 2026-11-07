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
| `WorldEvents.Entered` | Once when the local client reaches playable state in a world. |
| `WorldEvents.Left` | Once when the world session ends, the client switches worlds, or the client exits normally from a world. |
| `WorldEvents.Tick` | After a gameplay update in the active world. Paused gameplay, debugger world steps, and replay updates are skipped. |
| `ServerEvents.Ready` | Once after a dedicated server loads the world and opens its listener. |
| `ServerEvents.PlayerJoined` | After an accepted player finishes joining the world. The listener receives the server's live `Terraria.Player` instance. |

World listeners receive a `WorldInfo` value with the display name, world ID, and multiplayer flag.
The `Entered` and `Left` events delimit a session.
Use `Left` to release resources you acquired in `Entered`.

`ServerEvents.IsDedicatedServer` is available during initialization so a mod can register side-specific listeners.
Register custom item definitions on both sides in the same order-independent set.
See [dedicated servers](server.md) for launch arguments and connection rules.
