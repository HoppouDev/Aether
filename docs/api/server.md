# Dedicated servers

Aether runs dedicated servers on Linux with the same bootstrap, mod loader, and dependency ordering as the client.
The server uses TCP and can run without a desktop session or Steam login.
The optional Aether API adds server lifecycle events, custom item registration, and direct block breaking.

## Start and join

Build the client once and place the server's `.aethermod` archives directly in its save directory's `Mods/`.
The default server save directory is `.aether/server-saves/`.
Pass `--save-dir` to select a different directory and use an absolute path for the world.
For a custom-item profile, copy the matching world from the client's `AetherContent/<profile>/Worlds/`.

```sh
./aether build
./aether server --save-dir /path/to/server-profile -- \
  -world /path/to/server-profile/Worlds/Example.wld -port 7777 -noupnp
```

Use `-ip 127.0.0.1` to bind a local test server to loopback.
Without `-world`, choose or create a world from the server console.
Enter `exit` in that console to save and stop.
Mod startup failures are printed to the console and recorded in `Logs/mod-load-error.log` under the server save directory.

Install the server's exact archive files in the client's active `Mods/` directory.
Launch the client with `-aetherjoin` and use **Multiplayer → Join via IP**.
To prefill the address and port and go straight to character selection, use the following command.

```sh
./aether launch -- -aetherjoin -join 127.0.0.1 -port 7777
```

Clients with custom items, NPCs, or projectiles select the Aether protocol automatically.
Use a character without local custom items for a dedicated server session.

## Compatibility

The server verifies the connection before sending player or world data.
The profile includes the Aether protocol revision, Aether and Terraria versions, the full set of mod IDs and versions, each archive's SHA-256 digest, and the ordered custom item, NPC, and projectile mappings.
Copy the same archives to both machines because repacking can change the digest.
An extra client archive or a mod that registers different content IDs on the server also causes a mismatch.

`-nomods` skips discovery and mod code.
A dedicated server started with `-nomods` still uses the Aether protocol and requires a client started with `-nomods -aetherjoin` and matching builds.
Run the client without `-aetherjoin` and without custom content to use the standard Terraria connection protocol.
Host & Play uses the separate vanilla server.

## Custom items

Register the same IDs and definitions on both sides.
Use `ServerEvents.Ready` to prepare world items after the server has loaded the world.
Use `ServerEvents.PlayerJoined` to assign an item to a connected player.
Mutate item state on the server and send the corresponding Terraria item update after making a change.

```csharp
using System;
using System.Text.Json;
using Aether.Api;
using Terraria;
using Terraria.ID;

// Register the item and this listener in your initializer.
ItemRegistry.Register("example:blade", new ItemDefinition("Example Blade", ItemID.IronBroadsword)
{
    Damage = 15
});
ServerEvents.PlayerJoined.Register(player =>
{
    int slot = Array.FindIndex(player.inventory, 0, 58, item => item.IsAir);
    if (slot < 0)
        return;

    Item blade = ItemRegistry.Create("example:blade");
    ItemRegistry.SetState(blade, JsonSerializer.SerializeToElement(new { charge = 10 }));
    player.inventory[slot] = blade;
    NetMessage.SendData(MessageID.SyncEquipment, number: player.whoAmI,
        number2: PlayerItemSlotID.Inventory0 + slot);
});
```

| Location | Send after a server-side change |
| --- | --- |
| Player inventory, equipment, or bank | `MessageID.SyncEquipment` with the player index and a `PlayerItemSlotID` slot. |
| World chest | `MessageID.SyncChestItem` with the chest index and slot. |
| Dropped world item | `MessageID.SyncItem` with the world item index. Set state in `Item.NewItem`'s `modifier` callback to include it in the spawn packet. |
| Item frame, display jar, weapon rack, food platter, display doll, or hat rack | `MessageID.TileEntitySharing` with the tile entity ID. |

Clients can read synchronized item state with `ItemRegistry.GetState`.
The server rejects client item packets that create, replace, or remove custom items in those locations.
Move or transfer custom items through server-side mod code.
The normal inventory, chest, and pickup controls do not move them in multiplayer.
Items assigned to a player for the session are kept out of that player's local character file.
World chests and displays retain their contents in the server's world save.
Dropped items follow Terraria's normal world-save behavior.
Item use, hit, and equipment callbacks run in single-player.

## Block breaking

Run `Tiles.BreakBlock` from a server-side game-thread callback.
The server validates and breaks the block, then synchronizes affected cells, containers, display items, and drops.
See the [world and tile API](world.md#direct-block-breaking) for supported blocks, result values, and drop behavior.

## Custom entities

The Aether connection profile uses protocol revision `2` for custom entity synchronization.
Custom NPCs and projectiles are simulated on the dedicated server and observed by clients.
See [NPCs and projectiles](entities.md#dedicated-server-authority) for spawn ownership and behavior rules.
