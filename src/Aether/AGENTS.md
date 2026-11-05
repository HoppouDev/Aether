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
- Sliders: `UIMenuSlider` is a value bar (drawn by vanilla's `IngameOptions.DrawValueBar`) and `UIMenuColorBars` the HSL(+alpha) colour bars; `UIMenuText` is the small caption/value text beside them.
  Both set `UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE` while focused, so the gamepad adjusts them through `UILinksInitializer.HandleOptionsSpecials`.
  The colour bars keep their state in `Main.hBar`/`sBar`/`lBar`/`aBar` like `DrawMenu` (the gamepad edits those), so `DrawMenu`'s per-frame reset of them is patched to skip modes `MenuScreens` owns.
- Window changes (resolution, fullscreen) go through `Main.QueueMainThreadAction`: `DrawMenu` defers them to the end of the frame, and the screens run mid-draw.
- Migrated: 0 (main menu), 11 (settings), 12 (multiplayer), 13 (join via IP), 131 (server port), and every settings page: 112 (general), 1112 (interface), 1111 (video), 2008 (effects), 111 (resolution), 28 (parallax), 26 (volume), 1125 (cursor), 25 and 252 (cursor and border colour), 1127 (controls), 1213 (language) and 1212 (first-run language).
  Modes 27 and 272727 (old key binding screens) are unreachable in vanilla and stay legacy.
  Still legacy: world/server status and error screens (10, 14, 15, 100, 200, 201, …), world generation and server setup (-7, -71, 5, 6, 7, 8, 9, 16, 30, 31, 882, 889, 5000, 3000, 1000000, …), and the in-game UI.
  Tested by hand with mouse and keyboard; gamepad navigation is untested.
