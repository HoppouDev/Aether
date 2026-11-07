# Worlds and tiles

Use `Worlds.TryGetCurrent(out world)` on the game thread for a snapshot of the current world.
It returns false before the world is ready.
The snapshot includes the world's identity, difficulty, hardmode state, time, weather, spawn, and dimensions in tiles.
`Time` uses Terraria's elapsed ticks for the current day or night.

```csharp
if (Worlds.TryGetCurrent(out var world))
{
    TileQueryResult result = Tiles.Query(world.Spawn);
    if (result.Tile is { } tile)
        Console.WriteLine($"Spawn block {tile.BlockType}, wall {tile.WallType}");
}
```

`Tiles.Query(new TilePosition(x, y))` takes tile coordinates and returns a detached snapshot for a loaded cell.
`Status` distinguishes `Available`, `Unloaded`, `OutOfBounds`, and `WorldUnavailable`.
In multiplayer, clients can query only loaded sections.
A loaded cell can have a wall, liquid, wires, or an actuator while its foreground is empty.
`BlockType`, `WallType`, and `Liquid` use null for an absent layer.

Use `/api-example:spawn` in the example mod to query the current world and its spawn tile.

## Direct block breaking

Call `Tiles.GetBlockBreakability(position, options)` to check a foreground block.
The preview reports `Eligible`, `Ineligible`, or `Unsupported` and supplies a reason and a footprint when known.
It does not create tiles, roll random outcomes, drop items, or remove container data.

Call `Tiles.BreakBlock(position, options)` on the game thread in single-player.
It checks the target again and attempts one direct break through the game's destruction and framing path.
A direct break skips tool and reach requirements and leaves the wall in place.
The result contains snapshots at the requested cell and the bounds of the target.
`Removed` means the cell's foreground is gone.
`Changed` covers a replacement block or a display that ejected its item.
`TileEntityRemoved` reports removal of the target's tile entity.

Supported targets are dirt, stone, wood, clay, grass variants, mud, snow, ice, sand, chests, dressers, item frames, weapon racks, food platters, mannequins, and hat racks.
A multi-tile object can be selected by any of its cells.
Its complete footprint must pass validation.
Chests, dressers, mannequins, and hat racks with contents are protected.
A filled item frame, weapon rack, or food platter ejects its item on the first attempt.
A later attempt can remove the empty display.

Drops are enabled by default.
`new BlockBreakOptions(SuppressBlockDrop: true)` suppresses the target block's drop for dirt, stone, wood, and clay.
Native framing can also break attachments, which still use their normal drop rules.

## Mining with a held pickaxe

`Tiles.MineBlock(player, position)` applies one hit using the living local player's held pickaxe in single-player.
It uses the game's tool reach, pickaxe power requirements, hit cache, cooldown, and mining credit.
The call supplies the action directly, so it can run from a tick callback without input or an item-use animation.
It supports ordinary terrain and ores as well as the multi-tile objects listed above that a pickaxe can break.

`PartialHit` includes accumulated damage in `MiningDamage`.
Terraria attempts a break at 100.
A hit can instead transform the tile, such as when grass becomes dirt.
The result reports `Changed` and contains both snapshots.
A rejected call supplies a reason and leaves mining progress and cooldown unchanged when the target or tool fails validation.
