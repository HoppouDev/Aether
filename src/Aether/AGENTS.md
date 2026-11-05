# Aether (src/Aether)

Aether's own code, as opposed to patched game code: everything here is new files (each one is an "added" patch in `patches/Aether/`), namespace `Aether.*`, compiled into `Terraria.exe` with the game, so it can use the game's `internal` members.
Keep edits to the game's own files to small hooks that call into here.

## Retained-mode title menu (`UI/Menu/`)

The legacy title menu is one immediate-mode method, `Main.DrawMenu`, with a branch per `Main.menuMode` that rebuilds its item list every frame and acts on the item clicked last frame.
Those screens are being moved, one `menuMode` at a time, to retained-mode `UIState`s (see [../Terraria/UI/AGENTS.md](../Terraria/UI/AGENTS.md)):

- `MenuScreens` maps a `menuMode` to its screen.
  The one hook in `DrawMenu` (`if (Aether.UI.Menu.MenuScreens.Draw(spriteBatch, gameTime)) { } else if (menuMode == 1212)`) updates and draws that screen and skips the legacy branch, which stays in the file but is no longer reached.
  `menuMode` keeps its vanilla meaning, so legacy screens, vanilla `MenuUI` screens (mode 888) and every other `menuMode` check are unaffected, and screens move between the systems just by setting `menuMode`.
- The screens run in their own `UserInterface`.
  The `UserInterface` constructor and `Draw` take over `UserInterface.ActiveInstance`, which vanilla code uses to mean `Main.MenuUI`, so `MenuScreens` hands it back after drawing.
  To open a vanilla screen from a migrated one, use `Main.MenuUI.SetState(...)` and `Main.menuMode = 888`, never `UserInterface.ActiveInstance`.
- `UIMenuScreen` lays out a column of `UIMenuItem`s like `DrawMenu` does (`ItemsTop`, `ItemSpacing`, per-item scale and offset), and rebuilds them each time the screen opens.
  `UIMenuItem` draws like a legacy item (outlined DeathText, grows and turns gold on hover) and registers its position with `GamepadMainMenuHandler`, so gamepad navigation works as for legacy items.
  `GoBack()` is the gamepad's back button.
- **Migrating a mode:** copy the legacy branch's behaviour exactly, including its per-frame side effects (e.g. mode 0 resets `netMode` and event flags every frame) and its sounds, into a `UIMenuScreen`, and register it in `MenuScreens`.
  Make a private `Main` member the screen needs `internal` rather than copying it.
  If the mode is shared with another screen (mode 0 is the connection status screen while `Netplay.ClientLoopThreadRunning`), return false from `IsAvailable` then.
  Legacy items act on mouse-down, retained ones on mouse-up; `UserInterface.SetState` ignores clicks for 200 ms, so neither clicks through into the next screen.
- Migrated: 0 (main menu), 11 (settings), 12 (multiplayer), 13 (join via IP), 131 (server port).
  Still legacy: the settings pages (112, 1112, 1111, 26, 1125, 1127, 1213 and their children), language select (1212), world/server status and error screens, and the in-game UI.
  Tested by hand with mouse and keyboard; gamepad navigation is untested.
