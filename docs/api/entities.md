# NPCs and projectiles

Use the entity API on the game thread after the world is ready.
`Entities.TryGetNpc(slot, out snapshot)` and `Entities.TryGetProjectile(slot, out snapshot)` read active pool slots.
`Entities.FindNpcs(area)` and `Entities.FindProjectiles(area)` return snapshots of entities whose hitboxes intersect a rectangle in world pixels, in ascending slot order.
A multiplayer client can read observed entities in loaded sections.

Each snapshot includes a handle for its current spawn.
Keep the handle for later lookup through the overloads that accept `NpcHandle` or `ProjectileHandle`.
A handle stops resolving when its entity is gone or the world closes.
Reusing the slot produces a different handle.
Snapshots are detached values.

## Spawning vanilla entities

Use `NpcTypeKey.Vanilla` with an `NPCID` and `ProjectileTypeKey.Vanilla` with a `ProjectileID`.
NPC keys accept vanilla variants with negative IDs.
Both spawn methods currently run in single-player.
Pass a Terraria `IEntitySource` so the native spawn path has the source of the action.
NPC positions use the bottom-center point.
Projectile positions use the center.
Both are measured in world pixels.

```csharp
var source = new EntitySource_Parent(player);
Entities.TrySpawnNpc(NpcTypeKey.Vanilla(NPCID.BlueSlime),
    new NpcSpawn(player.Bottom + new Vector2(64, 0), source), out var slime);
Entities.TrySpawnProjectile(ProjectileTypeKey.Vanilla(ProjectileID.WoodenArrowFriendly),
    new ProjectileSpawn(player.Center, new Vector2(8, 0), 12, 1, player.whoAmI, source), out var arrow);
```

Pass the local player slot or `255` as the projectile owner.
The latter is reserved for world-owned projectiles.
Optional AI values are passed to Terraria and may be overridden by a vanilla type's spawn logic.
The methods return false if the world or type is unavailable or the engine has no eligible unused slot.
Existing entities keep their slots.
The engine's per-owner golf ball limit also applies.
