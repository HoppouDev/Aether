# Aether mod authoring

Aether loads trusted .NET 10 mods before starting the game.
A mod can run an initializer, declare changes to the client assembly, or do both.
For shared lifecycle hooks, client tools, recipes, and custom items, see the [optional Aether API](api/index.md).
See [dedicated servers](api/server.md) for multiplayer setup and compatibility.
Changes are applied in memory for the current run.

## Build and install

Build the client with `./aether build`.
Reference `Aether.Modding.dll` and, if you use Terraria types, `Terraria.dll` from the bootstrap output.
Reference the client's `Mono.Cecil.dll` for [startup IL editing](mixin-wrappers.md#edit-il-at-startup).
The template includes it automatically.
The default Debug client output is `client/Bootstrap/bin/Debug/net10.0/`.
Keep those references out of the mod archive.
Use an assembly name unique to your mod.

For a mod in its own repository, install the project template from your Aether checkout.

```sh
dotnet new install /path/to/Aether/templates/aethermod
dotnet new aethermod -n MyMod --mod-id my-mod
cd MyMod
./aether setup
dotnet build -t:PackMod
```

The first setup clones Aether into the mod repo's Git-ignored `.aether/client` directory and builds it.
It uses the normal Aether setup prerequisites, including the pinned Linux Terraria build.
If setup cannot find that build, follow the download and setup instructions in the cloned checkout's `AGENTS.md`.
On Windows, use `.\aether.cmd setup`.
Open the generated `MyMod.slnx` in your IDE.
IDE and command-line builds use the local client and Aether API automatically after setup.

The archive appears under `bin/Debug/net10.0/`.
Use `-t:InstallMod` to package and install the mod and its separate Aether API archive into the mod repo's `.aether/saves/Mods`.
Set `-p:AetherSaveDirectory=/path/to/save-profile` to install elsewhere.
The template's optional `--game-version` and `--aether-version` arguments override the Terraria and Aether dependency constraints.
Omit either option to have `PackMod` record the exact version from the selected client build.
The template leaves a placeholder for each omitted option in the project manifest and resolves it in the packaged manifest.
Quote constraints that contain operators or spaces, such as `--aether-version ">=0.1.0 <0.2"`.

A generated mod repo can build, install, and launch in one step.

```sh
./aether -- -mixindebug
```

On Windows, run the equivalent from PowerShell.

```powershell
.\aether.cmd -- -mixindebug
```

You can also run `.\aether.ps1` directly if your execution policy allows scripts.

The launcher uses Debug unless you set `AETHER_CONFIGURATION=Release`, and keeps test saves under the mod repo's `.aether/saves`.
It prepares the client automatically when you launch before running setup.
The checkout is reused on subsequent launches.
Set `AETHER_REPOSITORY` before the first run to clone a different Git remote.
For an existing built checkout, set `AETHER_ROOT` or use the Git-ignored `.aether-root` file.

For an existing mod repo, copy the template launchers and `build/Aether.Mod.targets` to the project root and ignore `.aether/` and `.aether-root` in Git.
The launcher calls the project's `InstallMod` target.
Set `AetherUseApi` to `true` in the project and add `"aether-api": "0.1.0"` to the manifest's `depends` to enable the template's API setup and installation.
If your mod was generated before the client projects moved under `client/`, update its launchers and `build/Aether.Mod.targets` from the template.
On Linux and macOS, run `chmod +x aether` after copying.

Build the [startup example](../examples/StartupMod/StartupMod.csproj) with the following command.

```sh
dotnet build examples/StartupMod/StartupMod.csproj -t:PackMod
```

Copy `examples/StartupMod/bin/Debug/net10.0/StartupMod.aethermod` into the active save directory's `Mods` folder.
Local launches use `.aether/saves/Mods` by default.
Use `./aether launch --save-dir PATH` for a separate profile.
The example logs one line from its initializer and one from a method-entry callback.
Its private support DLL is bundled with the mod.

An `.aethermod` is a ZIP archive with a root `aether.mod.json`, the entry DLL, and its private dependencies.
In projects generated from the template, edit `aether.mod.json` at the project root.
`PackMod` copies it to the archive root and packages resource files under `resources/`.
For example, a mapping file at `resources/mappings.json` in the project uses `"mappings": "resources/mappings.json"` in the manifest.
Include the entry assembly's `.deps.json` when using the SDK's dependency resolution layout.
Paths in the manifest are relative to the archive root.

```json
{
  "schemaVersion": 1,
  "id": "startup-example",
  "version": "1.0.0",
  "assembly": "Aether.Examples.StartupMod.dll",
  "entrypoint": "Aether.Examples.Startup",
  "mixins": ["Aether.Examples.StartupMixin"],
  "depends": {
    "aether": ">=0.1.0",
    "terraria": "1.4.5.8"
  }
}
```

### Read packaged resources

Put files your mod needs at runtime under `resources/` in the mod project.
`ModResources.OpenRead(modId, path)` opens a read-only stream using a path relative to the archive's `resources/` folder.
Use the exact mod ID from the manifest and forward slashes for nested files.
The API works from initializers and woven mixin handlers.

```csharp
using var stream = ModResources.OpenRead("my-mod", "text/welcome.txt");
using var reader = new StreamReader(stream);
string welcome = reader.ReadToEnd();
```

This reads `resources/text/welcome.txt`.
Absolute paths and traversal segments are rejected.
A missing file raises an I/O exception, and an unknown mod ID raises `KeyNotFoundException`.
The manifest is stored at the archive root, so it is outside the resource API.

### Manifest reference

| Key | Behavior |
| --- | --- |
| `schemaVersion` | Required. Use `1`. |
| `id` | Required. Use a unique ID of 1–64 characters that starts with a lowercase ASCII letter and continues with lowercase ASCII letters, digits, periods, underscores, or hyphens. |
| `version` | Required nonempty mod version string. The loader does not interpret it as a version constraint. |
| `assembly` | Required relative path to the entry DLL. Its assembly name must be unique among installed mod entry assemblies. |
| `entrypoint` | Optional fully qualified CLR name of an `IModInitializer` implementation. Use a public concrete class with a public parameterless constructor. |
| `mixins` | Optional list of fully qualified types in the entry DLL to weave. List nested types using CLR `+` or Cecil `/` notation. |
| `depends` | Optional object mapping required component or mod IDs to version constraints. |
| `mappings` | Optional relative path to a selector mapping file. |

### Display metadata

Add any of these optional fields to `aether.mod.json` alongside the required keys.

```json
{
  "name": "Inventory Tools",
  "description": "Adds shortcuts for organizing your inventory.",
  "authors": ["Alex", "Sam"],
  "contributors": ["Taylor"],
  "source": "https://example.org/inventory-tools/source",
  "homepage": "https://example.org/inventory-tools",
  "license": "MIT"
}
```

`name` is the display title and defaults to the mod ID.
`description` defaults to an empty string.
`authors` and `contributors` are arrays of nonempty name strings.
Use `source` for the source repository URL and `homepage` for the mod’s main page.
Both are optional strings.
`license` accepts one license identifier or an array of identifiers.
Use SPDX identifiers when applicable.
To set a mod icon, put `icon.png` next to `aether.mod.json`.
The template packages it at the archive root, where the loader discovers it by filename.

Read the active packages' metadata through `ModCatalog.LoadedMods` from an initializer or mixin.
The read-only list follows initialization order and is empty when mods are disabled.
A mod with an icon has `IconPath` set to `icon.png`.
Use `ModResources.OpenIcon(mod.Id)` to open it as a read-only stream.
Opening an icon for a mod without one raises `FileNotFoundException`.
Display metadata does not affect dependency resolution or load order.

### Versioned dependencies

Use the reserved IDs `aether` and `terraria` to constrain the client and game versions.
Use another mod’s manifest ID to require that mod in the same profile.

```json
{
  "depends": {
    "aether": ">=0.1.0",
    "terraria": ">=1.4.5.8 <1.4.6",
    "shared-library": ["~1.2", "2.0.x"]
  }
}
```

All entries are required.
Within one string, space-separated terms must all match.
An array of strings allows any one alternative to match.

| Constraint | Meaning |
| --- | --- |
| `1.4.5.8` or `=1.4.5.8` | Exact version. |
| `>=1.2 <2` | At least 1.2 and below 2. |
| `~1.2.3` | At least 1.2.3 with the same major and minor components. With no minor component, only the major must match. |
| `^1.2.3` | At least 1.2.3 with the same major component, including when the major is zero. |
| `1.2.x` | Any version with the specified numeric prefix. Use `x`, `X`, or `*` for trailing wildcards. |
| `*` | Any version, including a non-SemVer version string. |

The comparison supports Terraria’s fourth numeric component and treats missing numeric components as zero.
Prereleases sort before the corresponding release, and build metadata after `+` is ignored.
A trailing `-` in a comparison term denotes the boundary before all prereleases of that numeric version.
For example, `>1.2- <1.2` accepts prereleases of 1.2.
Use SemVer-style versions for mods that other packages need to constrain.

The loader checks constraints at startup, before weaving or running any initializers.
Aether uses its build version and reads Terraria’s version from the actual client DLL.
Missing dependencies and version mismatches stop startup with the requesting mod ID, requirement, and installed version when available.
Mod dependencies also determine initialization order.

### Discovery and dependency loading

Aether scans the top level of the active profile's `Mods` directory at startup.
From the title screen, open **Mods** to browse the packages loaded for the current session and view their manifest details.
Search by mod name, ID, or author.
**Open Mods Folder** opens the active profile's directory.
Restart Aether after changing the archives there.
It extracts packages to temporary directories and weaves an in-memory copy of the client.
Archives may contain at most 4,096 entries and 512 MiB of uncompressed data.
Aether rejects unsafe paths, links, and case-colliding entries.
Restart after adding, replacing, or removing a mod.

Duplicate mod IDs, duplicate entry assembly names, missing required mods, and dependency cycles stop startup.
Among mods whose dependencies have been satisfied, the loader orders IDs lexically.
Weaving finishes before any initializers run.
`Initialize()` runs once per launch, after arguments, saves, and logging are configured and before the game is constructed.
An initializer can prepare state used by later mixin callbacks.
There is no unload or reload callback.
Mods run in the client process with its permissions.

Each mod loads managed dependencies through its own context.
Include the entry DLL's `.deps.json` and its dependency layout, or place private managed DLLs alongside the entry DLL.
A managed DLL's `.pdb` beside it is loaded with it, so debuggers bind breakpoints and stack traces have line numbers; `PackMod` packages the entry DLL's.
Native libraries use the dependency resolver and need binaries for the player's platform.
Managed-only mods can use one archive across Aether's platforms.
The game, FNA, ReLogic, and `Aether.Modding` are shared host assemblies.
Keep them out of your package.

`depends` controls presence and startup order.
An IDE reference can give you autocomplete for another mod's DLL, but declaring its ID in `depends` does not resolve that DLL at runtime.
Keep direct private dependencies in your own archive.
When woven code references a private DLL, Aether resolves it from the mod that contributed the reference.
If multiple mixins reference the same assembly identity, their resolved DLLs must be byte-identical and Aether shares one copy among those mods.
Copies bundled by unrelated mods stay private.

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

### Targets and merged members

Declare `[Mixin(typeof(Target))]` or `[Mixin("Namespace.Target")]`.
Merged mixins target non-generic game classes.
Dedicated startup IL editors can also target generic types, structs, and interface methods with IL bodies.
A mixin class's fields and methods are merged into the target, so an instance handler's `this` is the game object.
Helper methods declared on the mixin are merged too.
Keep types and members referenced outside the mixin accessible from the game assembly, for example by making a helper type and its method public.
Declare fields and methods directly in the mixin class.
Properties, events, nested types, P/Invoke declarations, and non-final virtual methods are unsupported in merged mixin classes.
Generic handlers are supported for [method and operation wrappers](mixin-wrappers.md).
Use a constructor injection to initialize merged state because mixin constructors and field initializers are unsupported.
Merged fields with custom layout, marshaling, RVA data, or readonly storage are unsupported.

- `[Shadow]` binds a field or method declaration to an existing member with matching types and staticness.
  Pass a selector to bind a member with a different name.
  References to the declaration in woven code use the game member.
- `[Unique]` allows a colliding helper method or field to receive a generated name.
  Ordinary collisions fail startup.
- `[Overwrite]` replaces an existing non-constructor method body.
  Pass a selector when the declaration has a different name.
  Match staticness, return type, and parameter types.
  A method can have one overwrite.
  An overwrite conflicts with injectors, wrappers, and IL editors targeting the same method.

### Select methods and instructions

Use a bare name for an unambiguous method or write a signature such as `CanPauseGame():System.Boolean`.
The return type is optional.
Use CLR type names in signatures, with `/` for nested types in Cecil selectors.
Qualify operand selectors with the declaring type, for example `Terraria.Main::CanPauseGame():System.Boolean` or `Terraria.Main::gamePaused:System.Boolean`.
The field type is optional.
Use `.ctor(...)` for instance constructors and `.cctor()` for static constructors.
Instance constructor sites start after the weaver verifies the base or sibling constructor call.
Injection targets must have an IL body.
Callback injections require non-generic methods with void or by-value returns.
See [wrapper and IL editor signatures](mixin-wrappers.md) for generic methods and ref returns.

| Injection point | Match |
| --- | --- |
| `At.HEAD` | First instruction in the method or active slice. |
| `At.RETURN` | Every IL return in the active region, including a shared epilogue when the compiler emits one. |
| `At.TAIL` | Last IL return in the active region. |
| `At.INVOKE` | Calls and virtual calls matching the qualified method in `Target`. |
| `At.FIELD` | Static and instance field reads or writes matching the qualified field in `Target`. |
| `At.CONSTANT` | A literal matching the value in `Target`, or the constructor argument of `ModifyConstant`. |
| `At.NEW` | Object creation matching the qualified constructor in `Target`. |

Selectors match the original instructions.
By default `Ordinal = -1` selects every match in IL order.
A nonnegative `Ordinal` selects one zero-based match.
Use `[Slice("id", from, to)]` on the mixin and set `Slice = "id"` on the injector to restrict matching to an inclusive region.
Set `FromTarget` or `ToTarget` for boundaries that need an operand selector.
Use `FromOrdinal` or `ToOrdinal` to disambiguate a boundary.
Each boundary must resolve to exactly one instruction.

`Require` sets the minimum match count and defaults to one.
Set `Require = 0` for an optional target.
`Allow` sets the maximum and defaults to `-1`, which means unlimited.
`Expect` is an additional minimum under `-mixindebug` and defaults to one.
Counts apply after slice and ordinal filtering, before shifting.
Among compatible edits at the same site and phase, Aether uses ascending injector `Order`, descending mixin `Priority`, mod dependency order, then handler name and declaration order.
Defaults are `1000` for both settings and `10000` for redirect order.

### Inject and capture

`[Inject(method, at)]` calls a `void` handler before the selected instruction by default.
Use `AtShift.BEFORE` to move back one instruction, `AtShift.AFTER` to run after the match, or `AtShift.BY` with a signed `By` offset.
Shifts must stay within the selected region.
You cannot split an IL prefix or run after a branch, return, throw, or the final instruction.

Use `CallbackInfo` for a void target or `CallbackInfoReturnable<T>` for a target that returns `T`.
The handler can take all target arguments before the callback parameter, or omit them.
A static handler can take an explicit target receiver as its first parameter.
An instance handler requires an instance target.
`Id` contains the target method name.
At a return instruction, `ReturnValue` contains the pending result.
At other sites it starts as `default(T)`.

Set `Cancellable = true` to allow `Cancel()` to return early or `SetReturnValue(value)` to set the result and return early.
`IsCancellable` and `IsCancelled` expose callback state.
Calling either cancellation method on a non-cancellable callback throws.
For a non-void callback, cancellation without setting a value returns the pending result at a return site or `default(T)` elsewhere.
The weaver rejects cancellation in constructors and methods with exception handlers, and at sites where it cannot verify the evaluation stack.

Use `[CaptureLocal(slot)]` on typed parameters after the callback.
Slots are zero-based IL local indices and must match the original local type exactly.
The target must have initialized locals.
Capture reads the value at the selected instruction, which may still be its default before the game assigns it.
Use `-mixindebug` to inspect local slots for the exact build.

### Redirect and modify values

- `[Redirect(method, at)]` replaces an `INVOKE`, `FIELD`, or `NEW` operation.
  Match the original operands in IL order and return the original result type.
  For an instance call or field access, include the original receiver as the first explicit argument.
  For a field write, also take the value and return `void`.
  For `NEW`, take the constructor arguments and return the constructed type.
  Two redirects cannot own the same instruction.
- `[ModifyArg(method, Target = ...)]` transforms one call argument before the original or redirected call.
  `Index` is zero-based and excludes the receiver.
  If omitted, the handler return type must identify exactly one callee argument.
  Accept either the selected argument or all callee arguments, and return the selected argument's type.
- `[ModifyConstant(method, literal)]` transforms a literal after it is pushed.
  Supported selectors include `int:12`, `long:12`, `float:1.5`, `double:1.5`, `string:hello`, and `null`.
  Use invariant-culture number formatting.
  Accept one original value and return the same type.
  For `null`, the immediately following consumer must establish the reference type, such as a return, store, or final call argument.

Redirects and value modifiers use unshifted sites.
Redirects and argument modifiers can match closed generic calls using their concrete operand types.
When several argument modifiers target a call, later modifiers see the arguments already updated by earlier ones.

Use [`WrapOperation`](mixin-wrappers.md#wrap-an-operation) when several mods need to wrap the same instruction or when your handler needs to invoke the original operation.
See [whole-method wrappers and startup IL editing](mixin-wrappers.md) for the other startup patching APIs.

### Access private members

Declare a public mixin interface with method declarations only.
Annotate each method with `[Accessor("field")]` or `[Invoker("Method(System.Int32)")]`.
A field getter takes no arguments and returns the field type.
A setter takes one field value and returns `void`.
Readonly fields cannot have setters and literal fields have no storage to access.
An invoker matches the target method's parameters and return type.

The weaver adds an instance accessor interface to the game type, so cast the target instance through `object` to that interface.
Put static bridges and constructor factories in a separate interface.
Use `IsStatic = true` for static fields or methods.
A constructor invoker selects `.ctor(...)` and returns the target type.
Keep the interface declarations as instance methods even for static bridges, then retrieve the generated bridge with `MixinAccess.BindStatic<TDelegate>(targetType, mixinType, methodName)`.

### Remap selectors for a game version

Set the manifest's optional `mappings` field to the relative path of a JSON file.
Each entry maps one complete authored selector to the exact selector in the installed game build.

```json
{
  "schemaVersion": 1,
  "selectors": {
    "Legacy.Target": "Terraria.Main",
    "OldMethod()": "CanPauseGame()",
    "Legacy.Target::OldField:System.Boolean": "Terraria.Main::gamePaused:System.Boolean"
  }
}
```

Mappings apply to mixin targets, target methods, operand and slice selectors, shadows, overwrites, accessors, and invokers.
They do not rewrite ordinary C# calls or type references in the mod's code.

## Use Aether's FNA extensions

Mixins reach only `Terraria.dll`, so Aether's patch to FNA adds a few extension points for mods.
Set `Texture.SamplerEXT` to draw that texture with its own sampler in any `SpriteBatch`, whatever the batch's sampler.
Aether sets it to linear filtering on font textures.
Create a `SamplerState` with `PixelSnapEXT = true` to have `SpriteBatch` put the edges of unrotated sprites drawn with it on whole device pixels.
Both edges are rounded the same way, so sprites that meet, such as a nine-patch's pieces, still meet.
While a `SpriteBatch`'s `PixelScaleEXT` is above 0, it also draws evenly scaled sprites drawn with such a sampler at a whole multiple of that many device pixels per texel, scaled about their origin.
Set it to 0.5 while drawing Terraria's art (2×2 texels per pixel of the art) at a fractional scale to give it even pixels, and back to 0 afterwards.
Set `Texture.EffectEXT` to draw that texture with its own `Effect` among a batch's other sprites, for example signed distance field text.
`SpriteBatch` sets the effect's `MatrixTransform` parameter, if it has one, to the batch's transform, and restores its own shaders for the next texture.
Give the effect its own vertex shader when its pixel shader needs shader model 3, such as for `ddx` and `ddy`.

Aether's ReLogic adds `DynamicSpriteFont.SetCharacterImage` to draw one of a font's characters from another texture, such as a finer rendering or a distance field.
Pass the texels per font unit and the image's offset from where the font draws the character, in font units.
The character's spacing is unchanged, so text measures and wraps as before.

## Log

Log with `ModLog.For("your-mod")` from `Aether.Modding`: `Debug`, `Information`, `Warning` and `Error`, with optional exceptions.
Messages are Serilog templates whose named holes take the values in order, such as `log.Information("Loaded {Count} recipes", count)`.
Events go to standard error with the game's other log output, and to the log file when the game runs with `-logfile`.
Debug events reach the console only with `-mixindebug`.
Aether shares its Serilog with mods, so leave it out of your archive.

At startup Aether logs its version, the loaded mods as a tree of their dependencies, and a summary of the mixins it applied (each mixin at Debug).

## Diagnose startup

Run `./aether launch -- -mixindebug` to see each mixin and its target, the selected instruction offsets and local slots.
A required startup failure stops the launch and writes `Logs/mod-load-error.log` under the active save directory.
Start with `./aether launch -- -nomods` to bypass discovery.
Restart after replacing or removing a package.

Run the executable weaver tests after building the bootstrap.

```sh
uv run pytest tests/test_modding.py
```

The tests use a small fixture assembly instead of starting the game.
