# Building, testing, and packaging the mod

This project builds against a local Aether client, packages an `.aethermod` archive, and launches the game with it.
The launcher scripts (`aether`, `aether.ps1`, `aether.cmd`) and `build/Aether.Mod.targets` do the work.
For Aether's modding APIs, see `docs/modding.md`, `docs/mixin-wrappers.md`, and `docs/api/index.md` in the Aether checkout.
For driving the running game, see [automation.md](automation.md).

## First run

```sh
./aether setup
```

On Windows, run `.\aether.cmd setup`, or `.\aether.ps1 setup` if your execution policy allows scripts.

Setup clones Aether into `.aether/client`, runs that checkout's setup if its Terraria sources haven't been generated, and builds the client and Aether API.
It needs the checkout's setup prerequisites and the pinned Linux Terraria build.
If setup can't find that build, follow the `download` and setup instructions in `.aether/client/AGENTS.md`.
Running the game without `setup` first does the same preparation.

Open the project's `.slnx` in your IDE after setup.
IDE and command-line builds then find the client and API automatically.

## The launcher

```text
./aether [run | setup | watch] [-- game-args...]
```

| Command | What it does |
| --- | --- |
| `./aether` or `./aether run` | Prepares and builds the client, installs the mod into the test profile, and starts the game. |
| `./aether setup` | Prepares and builds the client and builds the Aether API, without building the mod or starting the game. |
| `./aether watch` | Starts the game under `dotnet watch` with .NET hot reload (see [Hot reload](#hot-reload)). |

Arguments after `--` go to the game, for example:

| Argument | Effect |
| --- | --- |
| `-mixindebug` | Logs each mixin injection site with its IL offset and local variable slots, and checks the `Expect` counts. |
| `-aethermcp 47321` | Serves the MCP server for agents and automation ([automation.md](automation.md)). |
| `-join HOST -port PORT` | Connects to a server after you pick a character. |
| `-nomods` | Starts without any mods. |

`aether.cmd` forwards to `aether.ps1`, which has the same commands and environment variables.

### Which Aether checkout it uses

The launcher and the build look for the client in this order:

1. The `AETHER_ROOT` environment variable.
2. The path in a `.aether-root` file in the project folder.
3. `.aether/client`, cloned on first use from `AETHER_REPOSITORY` (default `https://github.com/HoppouDev/Aether.git`).

Relative paths are resolved from the project folder.
`.aether/` and `.aether-root` are Git-ignored.

Only the `.aether/client` checkout is managed: the launcher runs its setup when needed and builds it on every run.
The build is incremental, so it's quick when nothing changed.
After you pull new commits into `.aether/client`, the next run re-applies its patches and rebuilds the client.
A checkout from `AETHER_ROOT` or `.aether-root` is used as it is, so build it yourself.

### Environment variables

| Variable | Default | Effect |
| --- | --- | --- |
| `AETHER_CONFIGURATION` | `Debug` | The client and API configuration, `Debug` or `Release`, for the launcher and the build. |
| `AETHER_ROOT` | none | An existing Aether checkout to use in place of `.aether/client`. |
| `AETHER_REPOSITORY` | Aether's GitHub repository | The Git remote for the first clone of `.aether/client`. |
| `AETHER_LAUNCH_OPTIONS` | none | Extra options for `aether launch`, such as `--profile` for the allocation profiler (Linux). |

### Test profile

The launcher runs the game with `.aether/saves` as its save directory, so test characters, worlds, settings, and keybinds stay out of your real profile.
Installed mods go in `.aether/saves/Mods`.
Mod load errors are written to `.aether/saves/Logs/mod-load-error.log`.

## Build targets

Run these with `dotnet build -t:TARGET`.

| Target | What it does |
| --- | --- |
| `Build` (plain `dotnet build`) | Checks that the mod compiles against the client and API. |
| `PackMod` | Builds and writes `bin/<Configuration>/net10.0/<AssemblyName>.aethermod`. |
| `InstallMod` | Builds the Aether API package if needed, runs `PackMod`, and copies the mod's and API's archives into the test profile's `Mods` folder. |
| `PrepareAetherApi` | Builds and packs the Aether API from the checkout (what `./aether setup` runs). |

Before compiling, the build checks that:

- the client directory has `Aether.Modding.dll`, `Terraria.dll`, and `FNA.dll`,
- the API directory has `Aether.Api.dll` and `Brigadier.NET.dll` when the API is used,
- `aether.mod.json` exists, its `id` is a valid mod ID, and its `assembly` matches the DLL the project builds.

### MSBuild properties

Set these with `-p:NAME=VALUE`, or in the project file.

| Property | Default | Effect |
| --- | --- | --- |
| `AetherRoot` | From `AETHER_ROOT`, `.aether-root`, or `.aether/client` | The Aether checkout. |
| `AetherClientConfiguration` | `AETHER_CONFIGURATION`, else `Debug` | Which client and API build to reference. |
| `AetherClientDirectory` | The checkout's `client/Bootstrap/bin/<Configuration>/net10.0` | A folder with `Aether.Modding.dll`, `Terraria.dll`, and `FNA.dll`, for building against a client built elsewhere. |
| `AetherUseApi` | `true` in the project | Whether to reference, build, and install the Aether API. |
| `AetherApiDirectory` | The checkout's `client/Api/bin/<Configuration>/net10.0` | An API build to use: `Aether.Api.dll` and `Brigadier.NET.dll` to compile, and `aether-api.aethermod` to install. Setting it skips building the API. |
| `AetherSaveDirectory` | `.aether/saves` | The profile `InstallMod` installs into. |

### References

The build references the client's `Terraria.dll`, `FNA.dll`, `Aether.Modding.dll`, and `Mono.Cecil.dll` (for startup IL editing), and with the API `Aether.Api.dll` and `Brigadier.NET.dll`.
They're compile-only: the game provides them at runtime, so they stay out of the archive.
Add `ReLogic.dll` from the client directory the same way (`Private="false"`) if you use its types, such as `Asset<T>`.

## The archive

`PackMod` stages these files and zips them into the `.aethermod`:

| File | Source |
| --- | --- |
| `aether.mod.json` | The project's manifest, with version placeholders resolved (see [Versions](#versions)). |
| `<AssemblyName>.dll`, `.deps.json`, `.pdb` | The build output. The symbols give stack traces line numbers and let debuggers bind breakpoints. |
| `icon.png` | The project's icon, if there is one; the mod list shows it. |
| `resources/**` | Everything under the project's `resources/` folder, with its layout. |
| Private dependencies | NuGet packages and other DLLs the build copies to its output, except the game's and Aether's assemblies. |

Manifest paths are relative to the archive root, such as `"mappings": "resources/mappings.json"`.
From C#, open a resource by its path under `resources/` and dispose the stream:

```csharp
using Stream stream = ModResources.OpenRead(Startup.ModId, "welcome.txt");
```

### Versions

The manifest's `"aether": "@aether-version@"` and `"terraria": "@terraria-version@"` placeholders become the exact versions of the client you build against: Aether from the client's `Aether.version`, Terraria from its `Terraria.dll`.
Leave them to require exactly those versions.
Replace them with a version or range, such as `">=0.1.0 <0.2"`, to target others.
Aether checks every `depends` constraint when the game starts, before running any mod code.

## Depending on other mods

Add each required mod to `depends` by its manifest ID with a version constraint, such as `"shared-library": ">=1.2 <2"`.
Aether loads mods after the ones they depend on.

To use another mod's code:

1. Reference its DLL, and any library DLLs whose types you use, from that mod's build output, with `Private="false"` so they stay out of your archive.
2. List the mod in `depends`.
   At runtime, Aether resolves a dependency's entry DLL and the DLLs it ships from that mod, so both mods share its types and static state.
   Only direct dependencies are shared, so also depend on any mod whose types you use through another.
3. Install the other mod's archive next to yours, for example with a target that runs after `InstallMod`:

```xml
<Target Name="InstallSharedLibrary" AfterTargets="InstallMod">
  <Copy SourceFiles="$(SharedLibraryDirectory)/SharedLibrary.aethermod" DestinationFolder="$(AetherSaveDirectory)/Mods" />
</Target>
```

Keep your own private dependencies, such as NuGet packages no other mod provides, in your archive.

## Testing and debugging

```sh
./aether -- -mixindebug
```

This builds, installs, and starts the game with the test profile, logging each mixin's injection sites.
On Windows, run `.\aether.cmd -- -mixindebug`.

### Hot reload

`./aether watch` (`.\aether.cmd watch` on Windows) starts the game under `dotnet watch`, so saved edits to the mod's code apply to the running game.
It needs the ASP.NET Core runtime, which `dotnet watch` runs on.
The project's `RunCommand` is the launcher (`aether.cmd` on Windows), which keeps `dotnet watch`'s startup hook out of its own builds and passes it to the game.
Edits to mixins, which are merged into the game at startup, and edits hot reload can't apply restart the game.
Pass game arguments after `--`, as in `./aether watch -- -aethermcp 47321`.

### VS Code

`.vscode/` has three launch configurations: Debug, Debug with hot reload, and Debug with profiler.
Each starts the game from a task with the MCP server on port 47321, then attaches the debugger.
Pick `Aether.Bootstrap` from the process list, since the debugger attaches by process ID.
Stopping the session ends the game.
Breakpoints bind in the mod's own code; mixin handlers are merged into the game, so they likely won't.

### Profiling

`AETHER_LAUNCH_OPTIONS=--profile ./aether -- -aethermcp 47321` starts the game with Aether's allocation profiler (Linux).
Record with the MCP tool `game_profile`, and measure frame times with `frame_stats` or the overlay (Right Shift + F12).
See [automation.md](automation.md#performance).

## Distribution

Ship the `.aethermod` from `PackMod`.
Players put it in their profile's `Mods` folder, along with any mods in your `depends`.

With the API enabled, tell players to install the matching `aether-api.aethermod` separately; it isn't inside your archive.
To build without the API, set `AetherUseApi` to `false` in the project and remove `aether-api` from `depends`.
