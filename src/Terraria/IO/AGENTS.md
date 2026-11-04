# Persistence (Terraria.IO)

Save files, config and resource packs.
Player save code itself lives in `../Player.cs` (`SavePlayer` ~line 55457, `LoadPlayer` ~line 55843).

## Paths

- `Main.SavePath` (= `Program.SavePath`) defaults to the platform storage path (`~/.local/share/Terraria` on Linux).
  The `-savedirectory <dir>` launch argument overrides it; use that when testing so real saves aren't touched.
- Under it: `Players/` (`Main.PlayerPath`), `Worlds/` (`Main.WorldPath`), `config.json` (`Main.Configuration`, a `Preferences`), `input profiles.json` (`Main.InputProfiles`), `favorites.json` (`FavoritesFile`).
- Steam Cloud copies use the relative `players` / `worlds` paths through `SocialAPI.Cloud` (see [../Social/AGENTS.md](../Social/AGENTS.md)); `FileData.IsCloudSave` tracks which kind a file is.

## World files (`WorldFile.cs`)

- `SaveWorld(...)` / `LoadWorld()` are the entry points (temp file plus backup handling, cloud vs local).
  The binary format is `SaveWorld_Version2` / `LoadWorld_Version2`.
- Layout: `SaveFileFormatHeader` (the release number `Main.curRelease`, `FileMetadata` with the "relogic" magic number and file type, revision, then a table of section pointers), followed by sections in a fixed order: world header, tiles, chests, signs, NPCs, tile entities, weighted pressure plates, town manager, bestiary, creative powers, then a footer.
  The pointer table is rewritten at the end (`SaveHeaderPointers`).
- Loading checks the stored release against the current one (`if (num > <curRelease>)`, inlined as a number by the decompiler): **files written by a newer version are rejected**, and older versions are upgraded through `versionNumber`-gated branches in the `Load*` methods.
- **Format changes must stay symmetric**: change `SaveX` and `LoadX` together.
  If the output should stay loadable by vanilla, don't change the format at all, because vanilla validates section offsets (`ValidateWorld`).
- `WorldFileData.cs` holds world metadata shown in menus (seed, size, difficulty, secret-seed flags).

## Player files

- `.plr` files are **encrypted** with Rijndael (`Player.ENCRYPTION_KEY`), then written with a `BinaryWriter`.
  `PlayerFileData` carries metadata (name, playtime, favorites, cloud flag).
  The same symmetric-change rule applies to `SavePlayer`/`LoadPlayer`.
- Map exploration data is saved separately (`.map`, `MapHelper.SaveMap` in `../Map/`), unless `skipMapSave` is passed.

## Other files

- `Preferences.cs`: a JSON key/value store (Newtonsoft) behind `config.json` and `input profiles.json`; read with `Get(key, default)`, write with `Put`, persist with `Save`.
  Settings are loaded and saved through `Main` (search for `Configuration.`).
- `GameConfiguration.cs`: read-only typed view over a `JObject` (used for world-gen config).
- `ResourcePack*.cs`: texture/sound/text packs.
  `ResourcePackList` is built from `ResourcePacks/` and the Workshop, and packs feed the asset and language content sources.
  The embedded `Terraria.IO.Data.ResourcePacksDefaultInfo.tsv` (`src/Resources/`) lists every vanilla texture path with its width and height; `GameContent/VanillaContentValidator` uses it to check that pack textures have the expected dimensions.
- `FileMetadata.cs`, `FileType.cs`: the header shared by world/player/map files.
