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
On a dedicated server, create items from `ServerEvents.Ready` onward.
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

Custom-item profiles use local saves under `AetherContent/` in the active save directory.
The subfolder is derived from the game version, registered IDs, and owning mod versions.
Player and world saves in these profiles use a versioned Aether format.
Use `ItemRegistry.SetState(item, json)` and `ItemRegistry.GetState(item)` to write and read up to 16 KiB of JSON per custom item.
Passing `null` clears the state.
Clones keep the state, and items with different state cannot stack.
The value returned by `GetState` is a detached `JsonElement`; call `SetState` to update it.
Keep the state schema compatible across releases of your mod.
In multiplayer, the server owns item state and clients read synchronized copies.
See [dedicated servers](server.md) for the supported storage locations and how to send updates.

The save header records the ordered item mapping, required content mods, and a profile identity tied to the Terraria build.
Aether checks compatibility before loading or replacing a save or recovering its backup.
Keep both the mod archives and save files when moving a profile to another machine.
Vanilla saves copied into a custom-item profile convert when Aether first saves them there.

## Buildable example

Build the API package first, then package the [custom items example](../../examples/CustomItems/src/Startup.cs).

```sh
dotnet build examples/CustomItems/CustomItems.csproj -t:PackMod
```

Install `CustomItems.aethermod` from `examples/CustomItems/bin/Debug/net10.0/` alongside `aether-api.aethermod`.
Start a character and world in the custom-item profile, then run `/custom-items:give` in chat.
Swing the Counter Blade and run `/custom-items:inspect` while holding it.
Save and exit, reopen the same character and world, and inspect the blade again to see the saved counters.
