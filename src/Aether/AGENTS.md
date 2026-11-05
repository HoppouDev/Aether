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
  Per-frame work goes in `UpdateState()`, which `MenuScreens` runs before input like `DrawMenu` does, never in `Update()`: `UserInterface` handles clicks before it updates the state, so a reset there undoes the click (mode 12's per-frame `menuServer = false` broke Host & Play that way).
  Make a private `Main` member the screen needs `internal` rather than copying it.
  `MenuScreens.ShownMode` mirrors the few checks `DrawMenu` makes before `menuMode` itself: while `netMode` is 1 every mode except 1212, 1213, 31 and 888 shows the connection status (14), and mode 0 shows the loading status (10) while `Netplay.ClientLoopThreadRunning`.
  Keep it in step with the order of the legacy chain when migrating a mode that comes before those checks.
  Legacy items act on mouse-down, retained ones on mouse-up; `UserInterface.SetState` ignores clicks for 200 ms, so neither clicks through into the next screen.
- **Checking screens in isolation:** `./aether screens` (build, then launch with `-aetherscreens`) opens a gallery of every screen `MenuScreens` registers; `./aether screens MODE` (`--no-build` to skip the build) opens one directly.
  `MenuPreview` seeds the state a screen shows (status text, rejection info, the colour being edited), makes its items and Enter inert (the overlay names what would have been clicked), and returns to the gallery on the menu's back command (Esc, gamepad back).
  Tab switches to the legacy `DrawMenu` branch of the same mode for comparison; there, clicks and Esc are live.
  Sliders, colour bars and text fields stay live in a preview: they change the real settings, and the game saves its settings when it quits, so set them back before exiting.
  Screen actions that don't come from an item click (Enter in a text field) go through `UIMenuScreen.Act` so a preview can report them instead; screens skip their gamepad redirect to the virtual keyboard while previewed.
  New screens show up in the gallery automatically; add sample state to `MenuPreview.Seed` if they display something a previous step sets up.
- The title menu's gamepad page has link points for 20 positions only, so elements register theirs through `MenuScreens.AddGamepadPoint`, which drops the rest instead of crashing `GamepadMainMenuHandler`.
- Sliders: `UIMenuSlider` is a value bar (drawn by vanilla's `IngameOptions.DrawValueBar`) and `UIMenuColorBars` the HSL(+alpha) colour bars; `UIMenuText` is the small caption/value text beside them.
  Both set `UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE` while focused, so the gamepad adjusts them through `UILinksInitializer.HandleOptionsSpecials`.
  The colour bars keep their state in `Main.hBar`/`sBar`/`lBar`/`aBar` like `DrawMenu` (the gamepad edits those), so `DrawMenu`'s per-frame reset of them is patched to skip modes `MenuScreens` owns.
- Window changes (resolution, fullscreen) go through `Main.QueueMainThreadAction`: `DrawMenu` defers them to the end of the frame, and the screens run mid-draw.
- **Every reachable title-menu mode is migrated:** 0 (main menu), 1 and 6 (redirects to vanilla's character and world lists, via `UIRedirectMenu`), 5 and 9 (delete player or world), 40 (discard new character), 3000 (credits' Back), 11 (settings), 12 (multiplayer), 13 (join via IP), 131 (server port), 31 and 30 (server password, joining and hosting), 889 (Host & Play's Steam options), the status screens (10 loading/saving with tips, 14 connecting with Cancel, 15 connection error, 100, 882 Steam join, 200/201 world load failed, 1000000/1000001 rejections), and every settings page: 112 (general), 1112 (interface), 1111 (video), 2008 (effects), 111 (resolution), 28 (parallax), 26 (volume), 1125 (cursor), 25 and 252 (cursor and border colour), 1127 (controls), 1213 (language) and 1212 (first-run language).
  The rest of `DrawMenu`'s chain can't be reached in vanilla, so it stays legacy: the old key binding screens (27, 272727), world list (8), world creation chain (16 size, -7 difficulty, -71 evil, 7 name, 5000 seed; vanilla creates worlds in `UIWorldCreation`) and character creation (2, 3, 17 to 24, 222), which only link to each other.
  `DrawMenu` itself still draws the backdrop, logo, version number, social links, achievement popups and cursor around the screens; the in-game UI is all still immediate-mode.
  Tested by hand with mouse and keyboard (including joining a password-protected local server, losing the connection, and loading a truncated world with and without a backup), and every screen in the gallery; gamepad navigation is untested.
