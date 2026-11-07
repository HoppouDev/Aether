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
Vanilla spawning through these methods runs in single-player.
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
Spawn it with `Entities.TrySpawnNpc(NpcTypeKey.Custom("mod-id:name"), spawn, out handle)` after entering a single-player world or from an Aether dedicated server.
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

Custom NPCs are transient by default.
They are omitted from vanilla NPC save tables, bestiary kill progress, and banner tracking.
The example mod registers a Scout and spawns one with `/api-example:spawn-npc`.

## Registering custom projectiles

Call `ProjectileRegistry.Register("mod-id:name", definition)` from your initializer.
The definition declares a name, sprite, hitbox, lifetime in simulation ticks, tile collision, penetration, and eligible targets.
Use `Penetration = -1` for unlimited hits.
`ProjectileTargets.NpcEnemies` uses Terraria's friendly projectile targeting, and `ProjectileTargets.Players` uses its hostile projectile targeting.
`ProjectileTargets.None` gives the projectile a visual and simulation lifetime without damaging targets.

Use `ProjectileTypeKey.Custom` with `Entities.TrySpawnProjectile` and supply the center, velocity, damage, knockback, owner slot, and spawn source.
Custom projectiles can spawn in single-player or on an Aether dedicated server.
Sprites use the same resource format as NPCs, or a compatible vanilla projectile texture and frame count.
`ProjectileRegistry.TryResolve` and `ProjectileRegistry.TryGetKey` convert between stable keys and runtime IDs.

| Callback | When it runs |
| --- | --- |
| `OnSpawn` | Once after the projectile is live, with its spawn source. |
| `OnTick` | Once in the native projectile update before movement. |
| `OnHitNpc` | After the projectile deals damage to an NPC. Includes the target snapshot and critical-hit flag. |
| `OnHitPlayer` | After a hostile projectile deals damage to a player. |
| `OnRemoved` | Once after the handle expires, with the final snapshot and reason. |

Use `Entities.TrySetProjectileVelocity` and `Entities.TryDespawnProjectile` with a live custom projectile handle.
The callbacks share per-spawn `EntityBehaviorState`, and use the same callback failure policy as NPCs.
A projectile can expire, run out of penetration, hit a solid tile, be replaced by a native spawn, or leave with its world.
The example Scout fires a short-lived pulse using its tick callback.

## Dedicated-server authority

Install the same archives and register the same entity definitions on the server and clients.
Spawn custom entities on the server from a game-thread callback such as `ServerEvents.Ready`, `ServerEvents.PlayerJoined`, or `WorldEvents.Tick`.
Set the projectile owner to `255` for server ownership.
The server runs gameplay callbacks, moves entities, handles collisions and damage, and sends snapshots to clients.
Clients animate and extrapolate those snapshots between updates.
They can look up replicas in loaded sections and receive a fresh set when joining.

The server owns custom entity spawns, state, and removal.
A vanilla strike against a custom NPC sends a request with the spawn identity.
The server checks the actor, identity, distance, damage fields, and hit cooldown before applying it.
See [dedicated servers](server.md) for connection requirements.
