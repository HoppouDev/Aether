# Platform integration (Terraria.Social)

Abstracts the storefront platform: Steam (`lib/Steamworks.NET.dll`) and WeGame (Tencent's Chinese platform, via `lib/RailSDK.Net.dll`).

## Structure

- `SocialAPI.cs`: static entry point.
  `SocialAPI.Initialize(mode)` (called from `Program.LaunchGame`) picks a `SocialMode` and loads one module of each kind into static fields: `SocialAPI.Friends`, `Achievements`, `Cloud`, `Network`, `Overlay`, `Workshop`, `Platform`, `DLCs` (Steam only).
- `Base/`: abstract module classes (`CloudSocialModule`, `NetSocialModule`, `WorkshopSocialModule`, …) and shared types (workshop entries, publish settings, join requests).
  Game code only talks to these.
- `Steam/` and `WeGame/`: the concrete implementations.
  The network module is split into `NetClientSocialModule` / `NetServerSocialModule`, chosen by `Main.dedServ`.

## Mode selection and what it means

- Client: always `SocialMode.Steam` unless a mode is passed in explicitly.
  Dedicated server: `SocialMode.None` unless launched with `-steam`.
- With `SocialMode.None`, **the module fields stay null**.
  Code that touches them null-checks first (`SocialAPI.Cloud != null`, etc.; ~37 places); keep doing that, since server and non-Steam paths depend on it.
- Steam startup (`Steam/CoreSocialModule.Initialize`): on the client, `SteamAPI.RestartAppIfNecessary(105600)` relaunches through Steam and exits when started outside it (skipped while `steam_appid.txt` is next to the exe, as in the game folder).
  If `SteamAPI.Init()` fails (Steam not running), the game shows "launch from Steam" and exits.
  It then starts callback and tick threads and initializes Steam Input.
  Running a build therefore needs the Steam client running and `libsteam_api.so` from the game's `lib64/`.

## Features

- **Cloud saves**: `CloudSocialModule` backs cloud players/worlds/favorites (`FileData.IsCloudSave`; see [../IO/AGENTS.md](../IO/AGENTS.md)).
- **Multiplayer over Steam**: `Lobby`, `SteamP2PReader`/`SteamP2PWriter`, used by `Net/Sockets/SocialSocket` with `SteamAddress`.
  Server options like `-lobby` are read in `NetServerSocialModule`.
  Plain TCP play doesn't go through here; see [../Net/AGENTS.md](../Net/AGENTS.md).
- **Workshop**: `WorkshopSocialModule` / `WorkshopHelper` publish and download worlds and resource packs (`SupportedWorkshopTags`, progress reporters, `WorkshopIssueReporter`).
- **Achievements**: `AchievementsSocialModule` mirrors in-game achievements (`Terraria/Achievements`) to Steam.
- **Rich presence, friends, overlay, DLC checks**: the matching modules.

## Notes

- Changes here have real-account side effects (achievements unlocked, Workshop items published, cloud files written).
  Test with `-savedirectory` and avoid workshop/achievement code paths unless that's the point.
- Steamworks.NET types (`CSteamID`, `SteamUGCDetails_t`, …) are structs; take care with copies vs `ref` when editing code that mutates them.
