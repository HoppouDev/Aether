# ModProjectName

This is a standalone .NET 10 mod for Aether.

| Path | Purpose |
| --- | --- |
| `ModProjectName.slnx` | IDE solution for the mod project. |
| `.gitignore` | Keeps the local client, test saves, build output, and IDE state out of Git. |
| `src/Startup.cs` | Mod initializer and mod ID used by the starter code. |
| `src/Mixins/` | C# mixins. Register new mixin types in the manifest. |
| `aether.mod.json` | Mod ID, version constraints, entry assembly, initializer, mixins, and display metadata. |
| `icon.png` | Optional mod icon, packaged at the archive root. |
| `resources/` | Files packaged for runtime use. |
| `build/Aether.Mod.targets` | Shared reference, validation, packaging, and install targets. |

- Keep the mod ID, entry assembly, and type names in sync with the manifest.
- Keep C# sources under `src/` and use exact mixin signatures for the Terraria versions in `depends`.
- Add required mods to `depends` by manifest ID with a version constraint, such as `"shared-library": ">=1.2 <2"`.
  Aether checks these constraints on launch.
- Leave the generated Aether and Terraria version placeholders in place to use exact versions from the local client when packaging.
  Set a literal version or range to target a different version.
- Keep the local Aether checkout path and game binaries out of Git and the mod archive.

`PackMod` copies the manifest to the archive root and preserves files under `resources/`.
Use archive-relative paths in manifest entries such as `"mappings": "resources/mappings.json"`.
From C# use a resource-relative path such as `ModResources.OpenRead(Startup.ModId, "welcome.txt")` and dispose the returned stream.

## Build and test

Run `./aether setup` on Linux or macOS, or `.\aether.cmd setup` on Windows, to prepare the client without opening the game.
The launcher clones Aether into `.aether/client`, runs the checkout's setup if Terraria sources have not been generated, and builds the client and Aether API in the selected configuration.
Launching the mod directly also prepares the client on first use.
You need the checkout's setup prerequisites and the pinned Linux Terraria build.
If setup cannot find that build, see the cloned checkout's `AGENTS.md` for its `download` and setup instructions.

Use `AETHER_CONFIGURATION=Release` to select Release.
The launcher reuses the checkout and builds the client in the selected configuration on every run, which is quick when nothing changed.
After you pull new commits into `.aether/client`, the next run re-applies its patches and rebuilds the client.
Set `AETHER_REPOSITORY` before the first clone to use a different Git remote.
To use an existing built checkout, set `AETHER_ROOT` or put its path in `.aether-root`.
Relative paths are resolved from the mod repo.

- Open `ModProjectName.slnx` in your IDE after setup.
- `dotnet build` checks that the mod compiles against the local client and API.
- `dotnet build -t:PackMod` creates the archive in the build output.
- `dotnet build -t:InstallMod` packages the mod and installs it alongside `aether-api.aethermod` into `.aether/saves/Mods`.
  Set `-p:AetherSaveDirectory=/path/to/test-saves` to install into a different profile.
- `./aether -- -mixindebug` builds, installs, and launches with the local test profile on Linux or macOS.
  On Windows use `.\aether.cmd -- -mixindebug` or `.\aether.ps1 -- -mixindebug`.

For manual builds, `-p:AetherClientDirectory=/path/to/client-output` can point directly to the folder containing `Aether.Modding.dll` and `Terraria.dll`.
Set `AetherApiDirectory` to use an API build from another directory.
That directory must contain `Aether.Api.dll` and `Brigadier.NET.dll` (compile-only references), and for installation `aether-api.aethermod`, from the matching build.

The template references Aether API `0.1.0` by default.
Distribute the mod archive and tell users to install a matching Aether API archive separately.
To opt out, set `AetherUseApi` to `false` in the project and remove `aether-api` from `depends` in the manifest.
See `docs/api/index.md` in the Aether checkout for the API guide.
