# Terraria (root namespace): core game

The files directly in this folder are the engine core: global state, the game loop and the main entity types.
Subfolders are separate modules with their own `AGENTS.md`; see the index in the [root AGENTS.md](../../AGENTS.md).

## The big files

| File | Lines | Holds |
| --- | --- | --- |
| `NPC.cs` | ~97k | NPC defaults, AI, spawning, damage, loot hooks |
| `WorldGen.cs` | ~88k | World generation passes, tile placement/killing, world-update ticks (`UpdateWorld`) |
| `Projectile.cs` | ~81k | Projectile defaults, AI, collision, `Kill` effects |
| `Main.cs` | ~68k | Global static state, `Update`/`Draw`, menus, almost all drawing code |
| `Player.cs` | ~58k | Player stats, equipment effects, item use (`ItemCheck`), save/load |
| `Item.cs` | ~50k | Item defaults, prefixes, world items, spawning |
| `Recipe.cs` | ~17k | All crafting recipes and recipe groups |

Never read these whole.
`grep -n` for the method or the `case <type>:`/`type == <id>` you need, then read a range.

## Global state lives on `Main`

- Entity pools are fixed-size static arrays: `Main.player[256]`, `Main.npc[maxNPCs + 1]`, `Main.projectile[1001]`, `Main.item[401]` (dropped items: `WorldItem`, not `Item`; `maxItems = 400`), `Main.dust[6001]`, `Main.gore[601]`, `Main.chest[8000]`, `Main.sign[32000]`.
  Each array has one spare slot past the last usable entry: `player[255]` doubles as "server/none" for owners and targets, while the NPC sentinel is `npc[maxNPCs]` (200), not 255.
  Slots are reused; always check `.active`.
- `Main.tile` is a `Tile[,]` of **classes**, not structs.
  World code null-checks entries (`Main.tile[x, y] == null`, 250+ times in `WorldGen.cs`); keep doing so in code that can run before a world is fully loaded.
  Tile state is packed into `sTileHeader`/`bTileHeader*` bitfields behind accessor methods.
- `Main.netMode`: `0` single player, `1` multiplayer client, `2` server.
  `Main.myPlayer` is the local player's index.
  Most gameplay code branches on these; see [Net/AGENTS.md](Net/AGENTS.md).
- `Main.rand` is the gameplay RNG.
  World generation uses `WorldGen.genRand` instead, so seeds stay deterministic; don't mix them.
- World size: `Main.maxTilesX` / `Main.maxTilesY` (small/medium/large = 4200/6400/8400 wide).

## Game loop

`Main.Update` → `DoUpdate` → (in a world) `DoUpdateInWorld`, which ticks in this order: particles → players (`player[i].Update(i)`) → NPC spawning → NPCs (`npc[k].UpdateNPC(k)`) → gores → projectiles (`projectile[i].Update(i)`) → items (`item[i].UpdateItem(i)`) → dust → floating text → time → world/invasion ticks (`WorldGen.UpdateWorld`) → chests, wind, tile/wall renderers, camera.
`Main.Draw` → `DoDraw` handles rendering (mostly in `Main.cs`, tile drawing in `GameContent/Drawing/`).

Startup: `LinuxLaunch.Main` (entry point; installs the embedded-assembly resolver) → `Program.LaunchGame` → `new Main()`, `Lang.InitializeLegacyLocalization()`, `SocialAPI.Initialize()`, then `Main.Run()` → `Initialize`/`LoadContent`.
`Main.Initialize` populates content databases (`ContentSamples`, bestiary, drop rules, recipes).

## Entity patterns

- `Player`, `NPC` and `Projectile` derive from `Entity` (`whoAmI`, `position`, `velocity`, `width`/`height`, `active`).
  `Item` doesn't (it's the item *data*, used in inventories); a dropped item in the world is a `WorldItem : Entity` (`WorldItem.cs`, with `UpdateItem`) wrapping it.
- **Defaults by type ID**: `NPC.SetDefaults(int type, ...)`, `Projectile.SetDefaults(int type)`, `Item.SetDefaults(int type, ...)`.
  Item defaults are split across `SetDefaults1`–`SetDefaults5` by ID range (≤1000, ≤2001, ≤3000, ≤3989, rest), except food (`ItemID.Sets.IsFood`), which goes to `SetFoodDefaults` first.
  To change a vanilla thing, find its ID in `ID/` and search for that number in the matching `SetDefaults*`.
- **AI by style**: `NPC.AI()` and `Projectile.AI()` dispatch on `aiStyle` to methods named `AI_001`, `AI_007_...`, etc. (~60 for NPCs, ~130 for projectiles).
  Per-type tweaks are `if (type == N)` blocks inside the shared style.
  State is stored in the `ai[]`/`localAI[]` float arrays.
- **Spawning**: always through the static factories, which take an `IEntitySource` describing what caused the spawn: `NPC.NewNPC(source, x, y, type, ...)`, `Projectile.NewProjectile(source, position, velocity, type, damage, knockback, owner, ...)`, `Item.NewItem(source, ...)`.
  They return the slot index into the `Main` array.
- **Sync flags**: set `netUpdate = true` on an NPC/projectile after changing its state outside normal AI flow, so the server resends it.
- **Player equipment**: per-tick stats are reset in `Player.ResetEffects`, then rebuilt by `UpdateEquips` / `UpdateArmorSets`.
  Item use goes through `Player.ItemCheck`.

## Other core files worth knowing

`Collision.cs` (tile/hitbox collision), `Wiring.cs` (wires, actuators, logic gates), `Liquid.cs` / `LiquidBuffer.cs` (liquid simulation), `Lighting.cs` (front end to `Graphics/Light`), `Chest.cs` (chests and NPC shops: `SetupShop`), `Mount.cs`, `Minecart.cs`, `Lang.cs` (legacy text tables and name caches; see [Localization/AGENTS.md](Localization/AGENTS.md)), `NetMessage.cs` / `MessageBuffer.cs` / `Netplay.cs` (see [Net/AGENTS.md](Net/AGENTS.md)), `Utils.cs` (math/drawing helpers, `ReadEmbeddedResource`).
