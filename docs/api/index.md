# Aether API

Aether API is an optional mod package for shared game hooks and registration APIs.
The API version is `0.1.0`.

## Install and reference

Build Aether, then build the API package from the same checkout.

```sh
./aether build
dotnet build client/Api/Aether.Api.csproj -t:PackMod
```

Install `client/Api/bin/Debug/net10.0/aether-api.aethermod` in the active save profile's `Mods` directory.
Use the matching `Aether.Api.dll` from that build as a compile-only reference.
Template projects can set `AetherApiDirectory` to the folder containing the DLL, for example with `-p:AetherApiDirectory=/path/to/client/Api/bin/Debug/net10.0`.
Set the property in your mod's `.csproj` to make the reference available to your IDE and `./aether` launches.
Keep the DLL out of dependent mod archives.

Declare the dependency in the mod manifest.

```json
"depends": {
  "aether-api": "0.1.0"
}
```

Aether validates the dependency before running initializers and gives dependent mods the same API assembly.
Aether starts without the API when no installed mod requires it and the API archive is absent.
`-nomods` bypasses the API and dependent mods together.
The API package pins the Aether and Terraria builds it was compiled against.
Rebuild the API package when switching those builds.

The [API example](../../examples/ApiExample/src/Startup.cs) uses the public API from a separate mod.
Build it with `dotnet build examples/ApiExample/ApiExample.csproj -t:PackMod`.
Install `examples/ApiExample/bin/Debug/net10.0/ApiExample.aethermod` alongside the API.

- [Lifecycle events](lifecycle.md)
- [Keybinds, commands, and HUD](client-tools.md)
- [Recipes](recipes.md)
- [Custom items](items.md)
- [Player state and vitals](players.md)
- [Containers and player inventory](inventory.md)
- [World and tile queries](world.md)
- [Dedicated servers and multiplayer compatibility](server.md)
