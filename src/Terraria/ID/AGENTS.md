# Terraria.ID: content IDs and per-type sets

The source of truth for numeric IDs.
Decompiled code inlines constants, so elsewhere you'll see `type == 1` or `SendData(23, ...)`.
Look the numbers up here.

## Files

- One class per ID space: `ItemID`, `NPCID`, `ProjectileID`, `TileID`, `WallID`, `BuffID`, `DustID`, `PrefixID`, `MessageID`, `SoundID`, `MountID`, `GoreID`, `PaintID`, and others.
- Each ID is a constant named after the content (`ItemID.IronPickaxe = 1`).
  The **constant names double as localization keys** (`ItemName.IronPickaxe`); see [../Localization/AGENTS.md](../Localization/AGENTS.md).
- `Count` gives the size of the ID space (e.g. `TileID.Count = 754`); content arrays are sized from it (`TextureAssets.Item = new Asset<Texture2D>[ItemID.Count]`).
  Negative NPC/item IDs are legacy "net IDs" (variants); `ItemID.FromNetId` converts them.
- `Search` (a ReLogic `IdDictionary`) maps IDs to and from names at runtime, e.g. `ItemID.Search.GetName(id)`.
- `ContentSamples.cs`: one pre-built instance per item/NPC/projectile type (`ContentSamples.ItemsByType[...]`), filled at startup in `Main.Initialize`, plus derived sorting/creative data.
  Prefer it over constructing throwaway instances to read defaults.
- `GenPassNameID.cs`: names of world-gen passes.
  `RecipeGroups.cs`: recipe group handles.

## `Sets`: per-type property tables

Most ID classes have a nested `Sets` class of static arrays indexed by type ID, built with `SetFactory`:

```csharp
public static bool[] ShootsOnUseRelease = Factory.CreateBoolSet(false, 198, 3764, ...);
```

`CreateBoolSet(default, ids...)`, `CreateIntSet(default, id, value, id, value...)`, `CreateFloatSet(...)` and custom sets.
Game logic reads these instead of hard-coding type checks, so **adding a type's behavior is often a one-line edit to a set** rather than a new `if (type == N)` in `NPC.cs`/`Projectile.cs`.

## Editing notes

- Adding a new ID means bumping `Count` and making sure every array sized from it (textures, sets, name caches, `ContentSamples`, and files saved with per-type data such as bestiary/research progress) still lines up.
  Search for `<Class>.Count` before doing it.
- These files are large and flat (`ItemID.cs` has ~6.2k constants).
  Grep for a name or number rather than reading them.
