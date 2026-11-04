# Multiplayer networking (Terraria.Net + NetMessage/MessageBuffer/Netplay)

The networking code is split between this folder (transport and the newer module system) and three large files in the root namespace.

## Pieces

| Where | Role |
| --- | --- |
| `../Netplay.cs` | Connections and threads: `Netplay.Clients[256]` (`RemoteClient`, server side), `Netplay.Connection` (`RemoteServer`, client side), `ListenPort = 7777`, `StartServer`, `StartTcpClient`. |
| `../NetMessage.cs` | **Sending** legacy packets: `NetMessage.SendData(msgType, remoteClient = -1, ignoreClient = -1, text, number, number2..number7)`, a large `switch (msgType)` that serializes each packet type. |
| `../MessageBuffer.cs` | **Receiving** legacy packets: `GetData(start, length, out messageType)`, a matching ~200-case `switch` that deserializes and applies them. |
| `../ID/MessageID.cs` | Names for packet types (`SyncItem = 21`, `SyncNPC = 23`, `SyncProjectile = 27`, `NetModules = 82`, …). |
| `NetManager.cs`, `NetModule.cs`, `NetPacket.cs` | The newer module system (see below). |
| `Sockets/` | Transport: `TcpSocket` (plain TCP) and `SocialSocket` (Steam/WeGame P2P through `Social/`), behind `ISocket`. Addresses: `TcpAddress`, `SteamAddress`, `WeGameAddress`. |
| `../GameContent/NetModules/` | Most module implementations (liquid, text/chat, ping, ambience, bestiary, creative powers/unlocks, pylons, particles). `NetDebugModule` there is **not** registered. Other registered modules are nested classes elsewhere: `BannerSystem.NetBannersModule`, `CraftingRequests.NetCraftingRequestsModule`, `LeashedEntity.NetModule`, `UnbreakableWallScan.NetModule`. `Initializers/NetworkInitializer.Load()` is the authoritative list. |

## Legacy packets

- Message types are **raw integers** in the decompiled code (`NetMessage.SendData(23, -1, -1, null, i)`); map them through `MessageID`.
  When adding or changing a packet, edit the writer's `case N:` in `NetMessage.SendData` **and** the reader's `case N:` in `MessageBuffer.GetData` together, keeping field order and types identical.
- `remoteClient = -1` means "everyone" (server broadcast) or "the server" (from a client); `ignoreClient` skips one player (usually the sender when relaying).
- The `number..number7` / `number2..4` parameters mean different things for each packet type.
  Read the matching `case` before calling.
- Handshake: the client sends `"Terraria" + <curRelease>` (`Main.curRelease`, inlined as a number in `NetMessage`/`MessageBuffer`) and the server compares the string exactly.
  Any change to the protocol makes this build incompatible with vanilla clients and servers, so bump or diverge the version string deliberately if you change packets.

## Net modules

- Modules derive from `NetModule` (`abstract bool Deserialize(BinaryReader reader, int userId)`) and build packets with `CreatePacket<T>(maxSize)`.
- They're sent as `MessageID.NetModules` (82) with a `ushort` module ID; `MessageBuffer` passes them to `NetManager.Instance.Read`.
- **Module IDs are assigned by registration order** in `Initializers/NetworkInitializer.Load()`.
  Client and server must register the same modules in the same order; append new modules at the end.
- Sending: `NetManager.Instance.SendToServer(packet)`, `SendToClient(packet, playerId)`, `Broadcast(packet, ignoreClient)` (optionally with a per-client condition), `SendToServerOrLoopback(packet)` (also works in single player).
- Prefer a net module over a new legacy message ID for new features.

## Authority conventions

- Branch on `Main.netMode` (`0` single player, `1` client, `2` server).
  The server simulates NPCs and world state and syncs them out.
  A client simulates its own player (`Main.myPlayer`) and the projectiles it owns (`projectile.owner == Main.myPlayer`).
- After changing NPC/projectile state outside the normal sync flow, set `netUpdate = true` (the server then sends `SyncNPC`/`SyncProjectile`), or call `NetMessage.SendData` directly.
- Tile edits in multiplayer have to be sent too (`TileManipulation = 17`, or the tile-square/section packets); otherwise the server and clients drift apart.
