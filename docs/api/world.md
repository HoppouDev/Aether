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
