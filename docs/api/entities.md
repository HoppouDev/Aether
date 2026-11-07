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

## Registering custom NPCs

Call `NpcRegistry.Register("mod-id:name", definition)` from your initializer.
The definition declares a display name, sprite, hitbox, maximum health, contact damage, defense, knockback resistance, gravity, tile collision, and optional behavior callbacks.
Spawn it with `Entities.TrySpawnNpc(NpcTypeKey.Custom("mod-id:name"), spawn, out handle)` after entering a single-player world.
Numeric types are allocated by ordinal ID at startup.
Keep the namespaced key when referring to a definition across launches.
`NpcRegistry.TryResolve` and `NpcRegistry.TryGetKey` translate between keys and the current runtime IDs.

Use `EntitySprite.Vanilla(NPCID.BlueSlime)` to share a vanilla texture and frame count.
Use `EntitySprite.Resource("textures/scout.png", frames: 4, ticksPerFrame: 6)` for a PNG under your archive's `resources/` directory.
The image must contain equal vertically stacked frames and fit within 4096×4096 pixels.
The loader checks that the path exists when it freezes registration.
The client decodes the image and checks its dimensions before drawing.

| Callback | When it runs |
| --- | --- |
| `OnSpawn` | Once after the native spawn is live, with the `IEntitySource`. |
| `OnTick` | During the NPC's AI phase, before its native movement and collision. |
| `OnHit` | After a strike applies damage and before the native death check. Includes the attacker slot when known. |
| `OnHitPlayer` | After NPC contact damages a player. |
| `OnRemoved` | Once after the handle is invalidated, with the final snapshot and removal reason. |

Each callback receives a snapshot and the same `EntityBehaviorState` for that spawn.
Set its `Value` to hold data for the behavior.
Use `Entities.TrySetNpcVelocity` to steer a custom NPC and `Entities.TryDespawnNpc` to remove it.
Check for `Defeated` in the removal callback when granting defeat rewards.
A failing callback is logged with the mod and NPC ID and is disabled.
A failed spawn or tick callback also retires the NPC with `BehaviorFailed`.
A successful spawn call means the engine created the entity.
A spawn callback can immediately remove it.

Custom NPCs are currently transient and single-player.
They are omitted from vanilla NPC save tables, bestiary kill progress, and banner tracking.
The example mod registers a Scout and spawns one with `/api-example:spawn-npc`.
