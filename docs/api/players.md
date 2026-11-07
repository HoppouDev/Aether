# Players

Use the `Players` API on the game thread after a world is ready.
It takes Terraria's `Player` instances, including those passed to item callbacks and `ServerEvents.PlayerJoined`.
Reacquire a player after a world change or reconnect.

```csharp
if (Players.TryGetLocal(out var player))
{
    PlayerStats stats = Players.GetStats(player);
    Console.WriteLine($"{stats.Name} has {stats.Health}/{stats.MaxHealth} health");
}
```

`TryGetLocal` returns false in menus and on a dedicated server.
`TryGet(slot, out player)` looks up an active slot and `GetActive()` returns active players in slot order.
Dead players remain available for reads.
`GetStats` returns a value snapshot.
For remote players, check `Available` for character data, health and alive state, or mana.
Effective maxima and defense are available for the local player.
`TryGetWeaponDamage` uses the local player's effective stats to calculate the supplied item's damage before target defense and critical hits.

## Changes

Writes require a living local player in single-player.

| Method | Behavior |
| --- | --- |
| `SetGameMode` | Changes the character's mode among Classic, Mediumcore, and Hardcore in a non-Journey world. Journey requires a Journey world. The world difficulty is independent. |
| `SetJourneyGodMode` | Sets the Journey power for a Journey character in a Journey world. Terraria updates combat immunity on the next player update. |
| `SetHealth`, `SetMana` | Set current health or mana and clamp to the effective maximum. Health must be positive and mana must be nonnegative. |
| `SetBaseMaxHealth`, `SetBaseMaxMana` | Set saved base capacity, within 20–500 health and 0–200 mana. The next stat update recalculates effective capacities. Raising capacity leaves the meter at its current value. |
| `Heal`, `RestoreMana` | Add a nonnegative amount up to current capacity and return the amount restored. |
| `Hurt` | Uses native defense, dodge, immunity, and death rules. Pass 1–32,767 raw damage, a `PlayerDeathReason`, and optionally a hit direction of −1, 0, or 1. Returns life actually lost. |
| `Kill` | Attempts the native death path with a `PlayerDeathReason` and reports whether death occurred. Journey god mode can prevent it. |

Character mode and base vitals follow Terraria's normal character saving.
Hardcore deaths carry Terraria's normal consequences.
Use `/api-example:player` in the buildable API example to print the local snapshot.

## Effects

Use a Terraria `BuffID` to look up, apply, or remove a buff or debuff.
`TryGetEffects` returns a read-only snapshot in slot order.
False indicates unavailable data, while a successful empty snapshot indicates no active effects.
`TryGetEffect` looks up one active effect.
Each entry reports whether it is a debuff and whether its countdown is timed, untimed, or unavailable.
Remote buff lists carry effect IDs without authoritative countdowns.

`TryApplyEffect` takes 1–5,184,000 simulation ticks and follows Terraria's immunity, duration adjustments, stacking, and slot rules.
It returns true when the effect is active after an eligible application.
`RemoveEffect` clears an active effect and reports whether it was present.
Equipment and environmental effects can return as Terraria updates.
Both writes require single-player.
Use `/api-example:swiftness` in the buildable example to apply Swiftness.
