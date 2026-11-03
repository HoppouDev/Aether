# World generation (Terraria.WorldBuilding + GameContent/Generation)

This folder is the world-gen framework.
The actual passes live in `WorldGen.cs` (core) and `GameContent/Generation/`, and placeable structures in `GameContent/Biomes/`.

## How a world is generated

1. `WorldGen.GenerateWorld(...)` (in `../WorldGen.cs`, ~line 11031) loads the config from the embedded `Terraria.GameContent.WorldBuilding.Configuration.json` (in `src/Resources/`) through `WorldGenConfiguration.FromEmbeddedPath`, then fires `WorldGen.Hooks.OnWorldGenConfigProcess`.
2. It creates a `WorldGenerator` with the world seed and config, resets state, then reads the secret-seed toggles (`WorldGenerationOptions.Get<WorldSeedOption_Drunk>().Enabled` → `WorldGen.drunkWorldGen` / `Main.drunkWorld`, and the same for `notTheBees`, `noTrapsWorldGen`, `remixWorldGen`, `getGoodWorldGen`, `everythingWorldGen`, Skyblock…).
3. Each pass is appended with `AddGenerationPass(GenPassNameID.X, (progress, passConfig) => { ... })`.
   There are ~114 of them, inline lambdas wrapped in `PassLegacy` (`GameContent/Generation/PassLegacy.cs`), so **pass code lives in `WorldGen.cs`**.
   Some are separate `GenPass` subclasses.
   Pass names are in `ID/GenPassNameID.cs`; grep for `GenPassNameID.<Name>` to find a pass.
4. `WorldGenerator.GenerateWorld()` runs the passes in order, reporting progress through `GenerationProgress`.

## Framework pieces

- `GenPass`: abstract `ApplyPass(GenerationProgress, GameConfiguration)`, with `Name`, `Weight` (progress-bar share) and `Enabled`.
- `GenVars`: ~150 static fields of state shared between passes (biome positions, dungeon data, `structures`, `configuration`, …).
  Passes communicate through these, so pass order matters.
  Check which earlier pass sets a `GenVars` field before reading it.
- `StructureMap` (`GenVars.structures`): reserved rectangles.
  Call `CanPlace(area)` before placing and `AddStructure`/`AddProtectedStructure` after, so later structures don't overwrite earlier ones.
- `MicroBiome` (extends `GenStructure`): self-contained structures in `GameContent/Biomes/`, created with config values through `GenVars.configuration.CreateBiome<T>()`.
- **Shape/action DSL**: `WorldUtils.Gen(origin, shape, action)` with `Shapes` (circle, rectangle, …), `Actions` (place/clear/paint tiles…), `Modifiers` (chained filters and offsets on a shape), `Conditions` and `Searches`.
  Prefer it for new structure code.
  Much older code in `WorldGen.cs` instead calls direct helpers such as `TileRunner`, `PlaceTile` and `KillTile`.
- `AWorldGenerationOption` / `WorldSeedOption_*` / `WorldGenerationOptions`: secret-seed toggles shown in the world-creation UI, registered in `WorldGenerationOptions` (`Register<WorldSeedOption_...>()`).
- `WorldGenConfiguration` / `GameConfiguration`: typed access to the JSON config (`GetPassConfiguration(name)`, `Get<T>(key)`).

## GameContent/Generation

`Dungeon/` is a full sub-generator (layout providers, rooms, halls, entrances, features, `DungeonCrawler`, `DungeonGenVars`).
The rest are pass helpers: shape runners (`ShapeRunner`, `ShapeBranch`, `ShapeRoot`, `ShapeFloodFill`), actions (grass, vines, stalactites, statues), `TrackGenerator` (minecart tracks) and `TeleporterGenerator`.

## Rules

- Use `WorldGen.genRand` for all randomness during generation, never `Main.rand`; otherwise the same seed stops producing the same world.
- The config JSON contains `//` comments, so read it with Newtonsoft (as the game does), not a strict JSON parser.
  Changes to it go in `src/Resources/Terraria.GameContent.WorldBuilding.Configuration.json`.
- `Main.tile[x, y]` can be null in early passes; guard like the surrounding code does, and bounds-check against `Main.maxTilesX`/`maxTilesY` (many helpers keep a margin from the world edge).
