---
name: aether-game
description: Play and test the Aether Terraria client through the aether-game MCP server the client itself serves, which inspects and acts on UI components and sends synthetic input straight into the game without moving the real cursor. Use to check UI changes in game, reproduce bugs, take screenshots, or drive menus and gameplay.
---

# Driving the game with the aether-game MCP server

The `aether-game` MCP server's input goes straight into the game.
The window doesn't need focus and can sit behind other windows, and the user's real cursor and keyboard are never touched.

## Starting the client

The client serves the MCP server itself, so start it first, in the background, from the repository root:

```bash
./aether run --mcp 47321 > .aether/game.log 2>&1 &
```

`run` builds first (`launch` reuses the latest build; `screens` starts the screen gallery, see `client/Terraria/Aether/AGENTS.md`).
Saves default to `.aether/saves/`: always use a test save directory and leave the user's real saves alone.
`--profile` (Linux) preloads the allocation profiler for `game_profile` (allocations are slower for the whole run).
Steam must be running.
The server answers at `http://127.0.0.1:47321/mcp` (`.mcp.json`) once the title menu is up; if the MCP client connected before that, reconnect it.

## Tools

| Tool | What it does |
| --- | --- |
| `game_profile` | With the client started with `--profile` (Linux), record every allocation's class, size and call stack over `frames` updates and return the report per frame: methods by bytes allocated under them, then the top `top` allocation sites. Use it to find what allocates every frame. |
| `game_quit` | Close the client. Progress since the last save is lost. |
| `frame_stats` | Measure the next `frames` drawn frames, MangoHud-style: FPS with 1% and 0.1% lows, the frame budget and its use, frame, CPU and GPU (Linux) time percentiles, long frames, and the .NET runtime (allocations, GC pauses and collections, heap, JIT). With `path`, also writes every frame to a CSV file. |
| `overlay` | Show or hide the profiler overlay (Right Shift + F12), or toggle it. |
| `resize` | Resize the window, as dragging its edge does. |
| `game_state` | Screen size, `loading`, `gameMenu`, `menuMode`, the frame and update rates (`fps`, `ups`, per second) and `presentInterval` (`One`: vsync), the synthetic mouse, keys and pad, `usingGamepad`. In game it also gives the player (tile, life, mana, held item, inventory open, chest, `talkNPC`, item on the cursor), `ingameOptions` and `mapFullscreen`. |
| `game_ui_layout` | Read the next drawn frame’s component trees, each separately under `trees` with its `id` and `owner`: the game's retained UI (`terraria`) and mods' own (`gum`: FreshUI's Gum screens, with IDs like `gum:12`). Components have IDs, labels, bounds, values and actions. Use `interactive_only: true` to return actionable controls only. |
| `game_ui_action` | Act on a component ID from the latest layout. Use `activate`, `secondary`, `set_value` (a number from 0 to 1), or `set_text` (a string) as advertised by the component. |
| `game_screenshot` | The next drawn frame. `scale` defaults to 0.5. `crop: [x, y, w, h]` with `scale: 1` reads small text or checks pixels; `component_id` (any tree, with optional `padding`) captures one component. |
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

Every tool returns once its input has finished (a 400 ms drag after 400 ms).
If the game stops updating for 10 s (it froze, or it crashed and is showing an error dialog), tools fail with "the game stopped updating".
Then `game_screenshot` can't help: stop the client, check `.aether/game.log`, and start it again.

## Workflow

1. Start the client (see above).
2. Use `game_ui_layout` to find controls on the current screen and `game_ui_action` to act on them by ID.
3. Request a fresh layout after changing screens or if a component ID becomes stale.
4. For gameplay or controls without an action, use a screenshot and the mouse, key, text or gamepad tools.
5. Check the result with the next layout, `game_state`, or a screenshot.
6. Call `game_quit` when done.

The same commands work from a shell: `./aether control 'key press e' state`.

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
ui layout [interactive] | ui act ID ACTION [JSON]
menu MODE (title menu: show Main.menuMode MODE) | alloc [FRAMES] (allocations, JSON) | quit
profile [FRAMES] [TOP] PATH (allocation call stacks, with --profile)
frames [FRAMES] [PATH] (frame statistics, JSON; PATH: each frame as CSV)
overlay [on|off] | resize W H
screenshot component ID [padding P] [scale S] PATH
```

## Getting around

- **Title menu to a world:**
  1. Activate Single Player.
  2. Refresh the layout and activate **Play** with the character name as its `context`.
  3. Refresh the layout and activate **Play** with the world name as its `context`.
  4. The world takes ~10 s to load, so poll `game_state` until `gameMenu` is false.
- **In game:**
  - Escape toggles the inventory.
  - The Settings button is at the bottom right while the inventory is open.
  - Settings → Save & Exit returns to the title menu.
