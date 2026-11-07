# Custom items

Register definitions from `IModInitializer.Initialize()` with a unique `mod-id:name` owned by your mod.
Keep the ID stable across updates.
The API assigns numeric types in ordinal ID order for the current set of mods, so use the string ID in mod data.

```csharp
ItemRegistry.Register("example:blade", new ItemDefinition("Example Blade", ItemID.IronBroadsword)
{
    Damage = 15,
    Knockback = 4,
    Tooltip = "An example custom item."
});
```

Definitions supply a display name and reuse a vanilla item texture.
Set `Damage` for a swing-style melee weapon or `Accessory` for an equippable accessory.
Other fields set dimensions, stack limit, sell value, defense, swing time, and tooltip text.
The registry accepts up to 1,024 definitions.

After `ClientEvents.Ready`, call `ItemRegistry.Create(id, stack)` to construct a Terraria `Item`.
`TryGetType` resolves an ID to the session's numeric type, and `GetId` returns the stable ID for a custom type.
Read game item tables and instantiate items after `ClientEvents.Ready` so the client can size its tables once all mods have registered.

## Item callbacks

Set callbacks on the item definition.
The callbacks run for the local player in single-player on the game thread and receive the live item and player instances.

| Callback | Timing |
| --- | --- |
| `OnUse` | Once after a successful use begins. Setting it on an item without damage makes that item usable. |
| `OnHitNpc` | After the item's melee strike deals damage to an NPC. The context includes the target and damage dealt. |
| `OnEquippedTick` | On each gameplay tick when the item's functional equipment slot grants its benefits. The context includes the slot index. |

If a callback throws, Aether logs it and disables that callback for the session.

## Save profiles

Custom-item sessions currently use single-player local saves under `AetherContent/` in the active save directory.
The subfolder is derived from the game version, registered IDs, and owning mod versions.
Treat these as development profiles while the versioned save format is being implemented.
