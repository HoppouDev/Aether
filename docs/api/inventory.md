# Containers and player inventory

Call `Players.GetInventory(player)` to access a player's `PlayerInventory` through the shared `ItemContainer` API.
Use containers on the game thread and reacquire them after leaving the world.
Mutations require a living local player in single-player with an unlocked inventory.
Remote slot contents become readable when Terraria receives them.

```csharp
if (Players.TryGetLocal(out var player))
{
    PlayerInventory inventory = Players.GetInventory(player);
    Item? remainder = inventory.Insert(ItemRegistry.Create(ItemKey.Vanilla(ItemID.Torch), 10));
}
```

`Slots` enumerates slot handles in container order.
`TryGetSlot` accepts a `ContainerSlotId` with a group, a zero-based index within that group, and a loadout number for armor, vanity, and dyes.
The player inventory includes the hotbar, backpack, coin and ammo slots, cursor, trash, all three equipment loadouts, and the five miscellaneous equipment and dye slots.
The miscellaneous indices are pet, light pet, minecart, mount, and grapple in that order.
`SelectedSlot` returns an existing readable slot, including the cursor when selected.
`TryGetEffectiveEquipmentSlot` finds the physical slot that supplies active armor, vanity, or dye, including slots shared from another loadout.

| Operation | Behavior |
| --- | --- |
| `GetItem` | Returns a detached copy or null for an empty slot. An expired or unavailable slot raises an invalid-operation error. |
| `CanRead`, `CanInsert`, `CanExtract` | Report access in the current context. `CanAccept` checks whether a supplied stack can add at least one item. |
| `Insert` | Copies in as much as fits and returns a detached remainder, or null when the whole stack fits. Container insertion fills compatible stacks first, then empty slots. The player convenience overload targets hotbar and backpack slots. |
| `Extract` | Removes up to the requested positive count and returns a detached stack, or null when the slot is empty. |
| `TryReplace` | Validates the entire replacement and returns the old contents on success. Pass null to empty a slot. |
| `MoveTo`, `TrySwapWith` | Transfer items without duplicating the source. A swap validates both final placements and commits both together. |

Item copies preserve prefixes and custom state.
Transfers retain favorites in slots that support them.
Bulk insertion treats repeated physical targets as one slot.
Use `/api-example:inventory` in the example mod to print the number of occupied readable player slots.
