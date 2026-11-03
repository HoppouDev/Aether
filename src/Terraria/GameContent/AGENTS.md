# Terraria.GameContent: content systems

~790 files of game systems that sit on top of the core entities in [../AGENTS.md](../AGENTS.md).
Most subsystems follow the same shape: a database or manager created in `Main.Initialize`, filled by a `*Populator` or `Register*` methods, and reached through a static on `Main`.

## Subsystems

| Folder | What it does | Entry points |
| --- | --- | --- |
| `ItemDropRules/` | NPC loot and bags as composable rules | `ItemDropDatabase.Populate()` (per-NPC `RegisterToNPC`, global rules, `RegisterBoss_*`); built rules come from `ItemDropRule.Common/BossBag/ByCondition/OneFromOptions`; runtime via `Main.ItemDropsDB` and `Main.ItemDropSolver`; spawning helpers in `CommonCode` |
| `Bestiary/` | Bestiary entries, unlock tracking, filters | `BestiaryDatabaseNPCsPopulator.Populate`; `Main.BestiaryDB` (merged with drop rules at startup) |
| `Creative/` | Journey mode: powers, research/sacrifice | `CreativePowerManager`, `CreativePowers`; research counts are read from the embedded `Terraria.GameContent.Creative.Content.Sacrifices.tsv` (`src/Resources/`) by `CreativeItemSacrificesCatalog` |
| `Personalities/` | Town NPC happiness and shop pricing | `PersonalityDatabasePopulator`, `Main.ShopHelper` |
| `Events/` | Invasions and world events (Old One's Army `DD2Event`, `BirthdayParty`, `Sandstorm`, `LanternNight`, `CultistRitual`, …) | each event's static `Update*`/state |
| `Biomes/` | Micro-biome structures placed during world gen (cave houses, desert, granite/marble, etc.) | configured via world-gen config; see [../WorldBuilding/AGENTS.md](../WorldBuilding/AGENTS.md) |
| `Generation/` | World-gen pass implementations, incl. the large `Dungeon/` generator | see [../WorldBuilding/AGENTS.md](../WorldBuilding/AGENTS.md) |
| `Drawing/` | Tile/wall rendering (`TileDrawing.Draw`, `WallDrawing`), backgrounds/horizons, `ParticleOrchestrator` (networked visual effects) | `Main.instance.TilesRenderer` / `WallsRenderer` |
| `Tile_Entities/` | Tiles with extra state (`TEItemFrame`, `TEWeaponsRack`, `TEDisplayDoll`, `TETeleportationPylon`, `TELogicSensor`, `TETrainingDummy`, …) | base class `DataStructures/TileEntity` (`InitializeAll`, `PlaceEntityNet`, `Write`/`Read` for save and network) |
| `UI/` | Game-specific UI states and elements | see [../UI/AGENTS.md](../UI/AGENTS.md) |
| `NetModules/` | Most modern packet modules (liquid, text, bestiary, creative powers, pylons, particles, ping); some modules are nested in other systems (`BannerSystem`, `LeashedEntity`, …) | `Initializers/NetworkInitializer`; see [../Net/AGENTS.md](../Net/AGENTS.md) |
| `Skies/`, `Shaders/`, `Dyes/`, `RGB/` | Custom skies, shader data, dye shaders, RGB keyboard effects | registered in `Initializers/ScreenEffectInitializer`, `DyeInitializer`, `ChromaInitializer` |
| `Achievements/` | Achievement condition trackers | `Initializers/AchievementInitializer` |
| `ObjectInteractions/` | Smart-interact (right-click) targets | `SmartInteractSystem` |
| `Items/`, `Prefixes/`, `FishDropRules/`, `Golf/`, `LeashedEntities/`, `Liquid/`, `Ambience/`, `Animations/`, `LootSimulation/`, `Metadata/` | Smaller feature areas; `Metadata/TileMaterials` loads `Terraria.GameContent.Metadata.MaterialData.{Materials,Tiles}.json` from `src/Resources/` | |

## Notable top-level files

- `TextureAssets.cs`: static `Asset<Texture2D>` handles for nearly every texture (`TextureAssets.Item[type]`, `.Npc`, `.Projectile`, `.Tile`, …), arrays sized from the ID `Count`s.
  Assets are requested in `Initializers/AssetInitializer` (paths like `"Images\\..."`, relative to `Content/`).
- `SmartCursorHelper.cs` (smart cursor targeting), `BannerSystem.cs`, `TilePaintSystemV2.cs` (painted-tile texture caching), `QuickStacking.cs`, `PortalHelper.cs`, `NPCInteractions.cs`, `CoinLossRevengeSystem.cs`.

## Conventions

- New content of an existing kind usually needs edits in several places: the ID and `Sets` in [../ID](../ID/AGENTS.md), `SetDefaults`/AI in the core files, a texture in `TextureAssets`/`AssetInitializer`, localization JSON in `src/Resources/`, and, depending on the content, drop rules, bestiary entry, research count (`Sacrifices.tsv`), personality or shop data here.
- Database population order in `Main.Initialize` matters (e.g. the bestiary merges drop rules after both are built).
  Hook in at the same point as the existing populators.
