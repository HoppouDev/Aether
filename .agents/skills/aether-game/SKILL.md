---
name: aether-game
description: Launch, play and test the Aether Terraria client through the aether-game MCP server, which sends synthetic mouse, keyboard and gamepad input straight into the game (no window focus, no real cursor or system input). Use to check UI changes in game, reproduce bugs, take screenshots, or drive menus and gameplay.
---

# Driving the game with the aether-game MCP server

The `aether-game` MCP server's input goes straight into the game.
The window doesn't need focus and can sit behind other windows, and the user's real cursor and keyboard are never touched.

## Tools

| Tool | What it does |
| --- | --- |
| `game_launch` | Start the client and return once the title menu is up, with the screen size. Builds first by default (`build: false` reuses the last build). `screens: true` starts it in screen-testing mode (see `src/Aether/AGENTS.md`). `save_dir` defaults to `.aether/saves/`; never point it at the user's real saves. `game_args` are extra launch arguments. |
| `game_quit` | Close the client, including one this server didn't start. Progress since the last save is lost. |
| `game_state` | Screen size, `loading`, `gameMenu`, `menuMode`, the synthetic mouse, keys and pad, `usingGamepad`. In game it also gives the player (tile, life, mana, held item, inventory open, chest, `talkNPC`, item on the cursor), `ingameOptions` and `mapFullscreen`. |
| `game_screenshot` | The next drawn frame. `scale` defaults to 0.5. `crop: [x, y, w, h]` with `scale: 1` reads small text or checks pixels. |
| `mouse_move` | Move the cursor to (x, y), gliding over `duration_ms`. |
| `mouse_click` | Click at (x, y), or where the cursor is. Options: `button`, `hold_frames` (default 2), `count`. |
| `mouse_drag` | Press at (x1, y1), glide to (x2, y2) over `duration_ms` (default 400), release. |
| `mouse_button` | Press or release a button and leave it there (long holds). |
| `mouse_wheel` | Scroll by `notches`; positive is up. |
| `key_press` | Press and release a key or chord. Hold it longer with `hold_frames`, or with `hold_ms` (e.g. to walk). |
| `key_hold` | Hold keys down, or release them. |
| `type_text` | Type into the active text field; `\n` is Enter. |
| `gamepad_connect` | Plug in or unplug the synthetic gamepad (the other gamepad tools plug it in themselves). |
| `gamepad_button` | `press`, `down` or `up` buttons. |
| `gamepad_stick` | Move `left` or `right` to (x, y) over `duration_ms`. With `hold_ms`, hold there and then glide back to centre. |
| `gamepad_trigger` | Pull `left` or `right` to `value` over `duration_ms`; with `hold_ms`, hold, then release. |
| `game_wait` | Let the game run for `ms` milliseconds or a number of `frames`. |
| `release_all` | Release every button, key, stick and trigger. |
| `game_send` | Run raw commands in order in one call, optionally screenshotting at the end (`screenshot_scale`). It stops at the first error. |

Every tool returns once its input has finished; a 400 ms drag returns after 400 ms.

## Workflow

1. Run `game_launch`.
2. Take a `game_screenshot` before acting, so you click what is really there.
3. Act with the mouse, key, text or gamepad tools.
4. Let the game react with `game_wait` (~200–700 ms for a menu to open or close).
5. Check the result with `game_screenshot` or `game_state`.
6. Call `game_quit` when done.

## Coordinates, keys and buttons

- Coordinates are window pixels, the same as a `scale: 1` screenshot.
  To convert from a scaled screenshot, divide by the scale and add the crop's origin.
  For example, at scale 0.5 the image point (200, 125) is window point (400, 250).
- A click at a position always counts as mouse movement, which takes the game out of gamepad mode.
- `mouse_button` and `key_hold` keep input held until you release it.
- Keys:
  - XNA names: `Escape`, `Enter`, `Space`, `E`, `D1`, `F11`, `LeftShift`.
  - Aliases: `esc`, `ctrl`, `shift`, `alt`, `0`–`9`.
  - Join keys with `+` for chords: `ctrl+shift+l`.
- Gamepad:
  - Sticks run from -1 to 1, with up positive.
  - Buttons: `A`, `B`, `X`, `Y`, `Start`, `Back`, `lb`, `rb`, `ls`, `rs`, `lt`, `rt`, and the D-pad as `up`, `down`, `left`, `right`.
  - Join buttons with `+`.

### `game_send` commands

```text
mouse move X Y [MS] | mouse down|up [BUTTON] | mouse click [BUTTON] [X Y] [FRAMES]
mouse drag X1 Y1 X2 Y2 [MS] [BUTTON] | mouse wheel NOTCHES
key down|up KEY[+KEY] | key press KEY[+KEY] [FRAMES] | type TEXT
pad connect|disconnect | pad button B[+B] down|up|press [FRAMES]
pad stick left|right X Y [MS] | pad trigger left|right VALUE [MS]
wait MS | wait frames N | release | state | ping
```

## Getting around

- **Title menu to a world:**
  1. Click Single Player.
  2. Click the player's small **Play** icon, at the bottom left of its row.
  3. Click the world's **Play** icon.
  4. The world takes ~10 s to load; poll `game_state` until `gameMenu` is false.
- **In game:**
  - Escape toggles the inventory.
  - The Settings button is at the bottom right while the inventory is open.
  - Settings → Save & Exit returns to the title menu.
