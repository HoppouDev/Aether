# ModProjectName

This is a standalone .NET 10 mod for Aether.

| Path | Purpose |
| --- | --- |
| `ModProjectName.slnx` | IDE solution for the mod project. |
| `.vscode/`, `.mcp.json` | VS Code build, run and debug tasks (see Build and test), and the game's MCP server for agents. |
| `docs/modding.md` | Building, testing, packaging, and distributing the mod: the launcher, build targets and properties. |
| `docs/automation.md` | The game's MCP server: UI automation, synthetic input, screenshots, and performance tools. |
| `.gitignore` | Keeps the local client, test saves, build output, and IDE state out of Git. |
| `src/Startup.cs` | Mod initializer and mod ID used by the starter code. |
| `src/Mixins/` | C# mixins. Register new mixin types in the manifest. |
| `aether.mod.json` | Mod ID, version constraints, entry assembly, initializer, mixins, and display metadata. |
| `icon.png` | Optional mod icon, packaged at the archive root. |
| `resources/` | Files packaged for runtime use. |
| `build/Aether.Mod.targets` | Shared reference, validation, packaging, and install targets. |

- Keep the mod ID, entry assembly, and type names in sync with the manifest.
- Keep C# sources under `src/` and use exact mixin signatures for the Terraria versions in `depends`.
- Use `WrapMethod` to wrap an entire call and `WrapOperation` to wrap a single instruction.
  Use a dedicated mixin class for `EditIL`.
  See `docs/mixin-wrappers.md` in the Aether checkout for signatures, ordering, and examples.
- Add required mods to `depends` by manifest ID with a version constraint, such as `"shared-library": ">=1.2 <2"`.
- Leave the `@aether-version@` and `@terraria-version@` placeholders in the manifest to require the local client's exact versions.
- Keep the local Aether checkout path and game binaries out of Git and the mod archive.
- Use resource paths relative to `resources/`, such as `ModResources.OpenRead(Startup.ModId, "welcome.txt")`, and dispose the stream.

## Build and test

- `./aether setup` (`.\aether.cmd setup` on Windows) prepares the Aether client in `.aether/client` without starting the game.
- `dotnet build` checks that the mod compiles; `dotnet build -t:PackMod` writes the `.aethermod`.
- `./aether -- -mixindebug` builds, installs, and starts the game with the test profile in `.aether/saves`.
- Add `-aethermcp 47321` to the game arguments to drive the game through the MCP server (`.mcp.json`).

See [docs/modding.md](docs/modding.md) for the launcher, the client checkout and its environment variables, the build targets and properties, packaging, depending on other mods, hot reload, debugging, and distribution.
See [docs/automation.md](docs/automation.md) for the MCP server: UI automation, synthetic input, screenshots, and performance tools.
For Aether's modding APIs, see `docs/modding.md`, `docs/mixin-wrappers.md`, and `docs/api/index.md` in the Aether checkout.
