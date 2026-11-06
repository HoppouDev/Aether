# Aether mod authoring

Aether loads trusted .NET 10 mods before starting the game.
A mod can run an initializer, declare changes to the client assembly, or do both.
Changes are applied in memory for the current run.

## Build and install

Build the client with `./aether build`.
Reference `Aether.Modding.dll` and, if you use Terraria types, `Terraria.dll` from the bootstrap output.
Keep those references out of the mod archive.
Use an assembly name unique to your mod.

Build the [startup example](examples/StartupMod/StartupMod.csproj) with the following command.

```sh
dotnet build examples/StartupMod/StartupMod.csproj -t:PackMod
```

Copy `examples/StartupMod/bin/Debug/net10.0/StartupMod.aethermod` into the active save directory's `Mods` folder.
Local launches use `.aether/saves/Mods` by default.
Use `./aether launch --save-dir PATH` for a separate profile.
The example logs one line from its initializer and one from a method-entry callback.
Its private support DLL is bundled with the mod.

An `.aethermod` is a ZIP archive with a root `aether.mod.json`, the entry DLL, and its private dependencies.
Include the entry assembly's `.deps.json` when using the SDK's dependency resolution layout.
Paths in the manifest are relative to the archive root.

```json
{
  "schemaVersion": 1,
  "id": "startup-example",
  "version": "1.0.0",
  "gameVersion": "1.4.5.8",
  "assembly": "Aether.Examples.StartupMod.dll",
  "entrypoint": "Aether.Examples.Startup",
  "mixins": ["Aether.Examples.StartupMixin"]
}
```

The mod ID is unique and starts with a lowercase ASCII letter.
The remaining characters can be lowercase ASCII letters, digits, periods, underscores, or hyphens.
`gameVersion` is the exact Terraria assembly version.
`depends` optionally lists mod IDs that must be present.
It orders initialization and does not expose those mods' private DLLs.
`entrypoint` names a public, concrete `IModInitializer` with a public parameterless constructor.
`mixins` lists the declared types to weave.
Both fields are optional.

## Declare a mixin

```csharp
using Aether.Modding;

[Mixin(typeof(Terraria.Main))]
public static class PauseMixin
{
    [Inject("CanPauseGame()", At.HEAD, Cancellable = true)]
    private static void BeforePause(CallbackInfoReturnable<bool> callback)
    {
        if (PauseSettings.DisableMenuPause)
            callback.SetReturnValue(false);
    }
}
```

Use a bare name for an unambiguous target or write a CLR signature such as `CanPauseGame():System.Boolean`.
Qualify instructions with their declaring type, as in `Terraria.Main::CanPauseGame():System.Boolean`.
Use Cecil names for types in string selectors, including `/` for nested types.
Use `.ctor(...)` for instance constructors and `.cctor()` for static constructors.

A mixin class's instance members act on the game instance once merged.
Declare private fields and methods with `[Shadow]` to bind existing members.
Use `[Unique]` for state or helpers that may collide.
Initialize merged state through a constructor callback.
An `[Overwrite]` replaces an existing method body with a signature match and excludes other edits to that method.

| Attribute | Behavior |
| --- | --- |
| `Inject` | Use `HEAD`, `RETURN`, `TAIL`, `INVOKE`, `FIELD`, `CONSTANT`, or `NEW`. The callback receives `CallbackInfo` or `CallbackInfoReturnable<T>`. It can also take all target arguments before that parameter. A static handler can take the target receiver first. At a return site, `ReturnValue` is the pending result. |
| `Redirect` | Replace a selected call, field read or write, or allocation. Take the original operation's receiver and operands in IL order and return its result type. Two redirects cannot own the same site. |
| `ModifyArg` | Select a call via `Target` and an argument via `Index`. Take the selected argument or all callee arguments and return the selected argument's type. If exactly one argument matches the return type, `Index` can be inferred. |
| `ModifyConstant` | Use a literal selector such as `int:12`, `long:12`, `float:1.5`, `double:1.5`, `string:hello`, or `null`. Take the original value and return the same type. For `null`, the consumer must establish the reference type. |
| `Accessor` and `Invoker` | Declare a public mixin interface with typed methods for field getters, setters, or method calls. Cast the woven target to the interface. Put static bridges and constructor factories in a separate interface and retrieve them with `MixinAccess.BindStatic<TDelegate>(targetType, mixinType, methodName)`. Mark static members with `IsStatic = true`. |

Set `Target` to select an instruction operand and `Ordinal` to select a zero-based match.
A callback runs before its matched instruction by default.
Use `AtShift.BEFORE` to move back one instruction, `AtShift.AFTER` to run after the match, or `AtShift.BY` with a signed instruction offset.
Add a named `[Slice]` on the mixin type and set `Slice` on the injector to limit matching to an inclusive region.
Slice boundaries must each resolve to one instruction.
To capture a local, add a typed parameter after the callback parameter and mark it with `[CaptureLocal(slot)]`.
Capture requires a matching initialized local slot.

`Require` sets the minimum number of sites and defaults to one.
`Allow` sets a maximum.
`Expect` adds a minimum when launching with `-mixindebug`.
Edits are ordered by injector `Order`, descending mixin `Priority`, mod dependency order, and declaration name.
Selectors are resolved against the original instructions.
Constructor callbacks start after a verifiable base or sibling constructor call.
The weaver rejects cancellation in methods with exception handlers and rejects edits it cannot safely emit.

For version-specific names, set the manifest's optional `mappings` field to a JSON file containing `schemaVersion: 1` and a `selectors` object mapping exact authored strings to local selectors.
Use the authored strings in selectors and shadow declarations.

## Diagnose startup

Run `./aether launch -- -mixindebug` to see selected instruction offsets and local slots.
A required startup failure writes `Logs/mod-load-error.log` under the active save directory.
Start with `./aether launch -- -nomods` to bypass discovery.
Restart after replacing or removing a package.

Run the executable weaver tests after building the bootstrap.

```sh
uv run pytest tests/test_modding.py
```

The tests use a small fixture assembly instead of starting the game.
