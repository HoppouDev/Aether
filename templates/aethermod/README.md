# ModProjectName

An Aether mod with the ID `example-mod-id`.
Open `ModProjectName.slnx` in your IDE.

Run `./aether` on Linux or macOS, or `.\aether.cmd` on Windows.
The first run clones Aether into `.aether/client` and builds the client and Aether API using your Terraria install.
Add `setup` to prepare the client without starting the game.

Run `dotnet build -t:PackMod` to create an `.aethermod`.
See [docs/modding.md](docs/modding.md) for prerequisites and build options, and [docs/automation.md](docs/automation.md) for driving the game from agents and scripts.
