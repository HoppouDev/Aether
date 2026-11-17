# Game automation, MCP, and screenshots

The Aether client can serve the `aether-game` MCP server, which lets agents and scripts drive the running game.
It reads and acts on the UI, sends synthetic mouse, keyboard, and gamepad input, takes screenshots, and measures performance.
Everything runs inside the client: no window focus, system input, or external tools are needed.

## Start the server

Pass `-aethermcp PORT` to the game.
With this mod's launcher, put it after `--`:

```sh
./aether -- -aethermcp 47321
./aether watch -- -aethermcp 47321
```

The VS Code tasks in `.vscode/` start the game with `-aethermcp 47321` in every configuration.
From an Aether checkout, `./aether run --mcp 47321` and `./aether launch --mcp 47321` do the same.

- The port defaults to `47321` when `-aethermcp` has no valid number.
- The server listens on `http://127.0.0.1:PORT/mcp` only, so other machines can't reach it.
- It speaks MCP's Streamable HTTP transport, stateless.
- If the port is taken, the client logs a warning and runs without the server.
- Without `-aethermcp`, there's no server and no synthetic input.

While the server runs, the game takes synthetic input alongside real input, and the window counts as focused.
The real cursor never moves.

## Connect an agent

`.mcp.json` registers the server for agents that read it, such as Claude Code:

```json
{
  "mcpServers": {
    "aether-game": {
      "type": "http",
      "url": "http://127.0.0.1:47321/mcp"
    }
  }
}
```

Start the game first; the tools fail while nothing is listening.
Change the port in both places if you use another one.

## Tools

Coordinates are window pixels, the same as a full-size screenshot's.
`game_state` gives the screen size.
Every tool returns once its input has finished, so a drag over 400 ms returns when the drag ends.
Commands run one at a time, in order, on the game's main thread at the start of an update.
If the game stops updating for 10 seconds, waiting commands fail.

| Tool | Arguments | What it does |
| --- | --- | --- |
| `game_state` | none | The game's state as JSON (see [State](#state)). |
| `game_ui_layout` | `interactive_only` | The UI's component trees as JSON (see [UI automation](#ui-automation)). |
| `game_ui_action` | `component_id`, `action`, `value` | Acts on a component without moving the cursor. |
| `game_screenshot` | `scale`, `crop`, `component_id`, `padding` | Captures the next drawn frame as an image (see [Screenshots](#screenshots)). |
| `mouse_move` | `x`, `y`, `duration_ms` | Moves the cursor, gliding (eased) over the duration; 0 jumps. |
| `mouse_click` | `x`, `y`, `button`, `hold_frames`, `count` | Clicks at the point, or where the cursor is, holding for `hold_frames` updates (default 2); up to 10 clicks in a row. |
| `mouse_drag` | `x1`, `y1`, `x2`, `y2`, `duration_ms`, `button` | Presses at the first point, glides to the second (default 400 ms), and releases. |
| `mouse_button` | `button`, `down` | Presses or releases a button and leaves it there, for long holds. |
| `mouse_wheel` | `notches` | Scrolls; positive is up. |
| `key_press` | `keys`, `hold_frames`, `hold_ms` | Presses a key or chord and releases it; `hold_ms` holds it that long, for example to walk. |
| `key_hold` | `keys`, `down` | Holds keys down or releases them. |
| `type_text` | `text` | Types text into the game's focused text field; a newline is Enter. |
| `gamepad_connect` | `connected` | Plugs the synthetic gamepad in or out; it replaces a real first gamepad. |
| `gamepad_button` | `buttons`, `action`, `hold_frames` | Presses (`press`), holds (`down`), or releases (`up`) buttons. |
| `gamepad_stick` | `stick`, `x`, `y`, `duration_ms`, `hold_ms` | Moves a stick to `x`, `y` (each −1 to 1, up positive); with `hold_ms` it holds, then returns to centre. |
| `gamepad_trigger` | `trigger`, `value`, `duration_ms`, `hold_ms` | Pulls a trigger to `value` (0 to 1); with `hold_ms` it holds, then releases. |
| `game_wait` | `ms` or `frames` | Lets the game run that long. |
| `release_all` | none | Releases every synthetic button, key, stick, and trigger. |
| `resize` | `width`, `height` | Resizes the window, as dragging its edge does, and returns `WxH`. |
| `frame_stats` | `frames`, `path` | Measures the next frames (see [Performance](#performance)). |
| `overlay` | `shown` | Shows, hides, or toggles the profiler overlay (Right Shift + F12). |
| `game_profile` | `frames`, `top` | Records allocation call stacks (see [Performance](#performance)). |
| `game_send` | `commands`, `screenshot_scale` | Runs raw [control commands](#control-commands) in order, stopping at the first error, with an optional screenshot at the end. |
| `game_quit` | none | Closes the client; unsaved progress is lost. |

The gamepad tools connect the synthetic gamepad if needed.

### Keys and buttons

Keys are XNA `Keys` names joined by `+`, such as `Escape`, `E`, `D1`, `Space`, `OemTilde`, or `ctrl+shift+l`.
The aliases `ctrl`, `shift`, `alt`, `esc`, `enter`, `backspace`, and `0` to `9` also work.

Gamepad buttons are XNA `Buttons` names joined by `+`: `A`, `B`, `X`, `Y`, `Start`, `Back`, `LeftShoulder` (`lb`), `RightShoulder` (`rb`), `LeftStick` (`ls`), `RightStick` (`rs`), `DPadUp` (`up`), `DPadDown` (`down`), `DPadLeft` (`left`), `DPadRight` (`right`), `lt`, and `rt`.

### Typing

`type_text` fills the game's own text input, which its chat, signs, and search fields read.
Toolkits that read the platform's native text input, such as Gum's text boxes, don't receive it.
Use `game_ui_action` with `set_text` for those.

## State

`game_state` returns JSON with these fields.

| Field | Meaning |
| --- | --- |
| `screen` | The window size in pixels. |
| `uiScale` | The game's UI scale. |
| `loading` | Whether the splash screen still shows. |
| `gameMenu`, `menuMode` | Whether the title menu shows, and its `Main.menuMode`. |
| `updates`, `fps`, `ups` | Updates since the server started, and the frame and update rates. |
| `presentInterval` | The vsync setting. |
| `mouse`, `mouseButtons`, `mouseUI` | The synthetic cursor in window pixels, its left, right and middle buttons, and the game's cursor in UI coordinates. |
| `keysDown` | The synthetic keys held. |
| `usingGamepad`, `inputMode` | The game's current input mode. |
| `pad` | The synthetic gamepad's buttons, sticks and triggers, or `null` when unplugged. |
| `player` | In a world: `name`, `tile` (the centre's tile), `life` and `mana` (current and maximum), `selectedItem`, `heldItem`, `inventoryOpen`, `chest`, `talkNPC`, and `mouseItem`. |
| `ingameOptions`, `mapFullscreen` | In a world: whether the settings window or the full-screen map is open. |

Wait for `gameMenu` to be `false` and `player` to appear before acting in a world.
A `life` of 0 means the player is dead and will respawn.

## UI automation

1. Call `game_ui_layout` to read the next drawn frame's components.
2. Find the component by its `label`, `screen`, `role`, `type`, or `context`, and pick one of its `actions`.
3. Call `game_ui_action` with its ID, the action, and a `value` for `set_value` or `set_text`.
4. Read the layout again after the UI changes.

IDs belong to the latest layout.
A component's ID can change when it's rebuilt, such as when its text changes, and an action on a stale ID fails with `stale UI component; request the current layout`.
Each action checks its target against a fresh drawn frame before running.

### Layout

The layout lists each component tree separately under `trees`, each with its `id`, the `owner` that registered it, and its `components`.

- The `terraria` tree, owned by `aether`, is the game's retained UI.
  Its IDs look like `0a1b2c3d-12`.
- Mods can add trees for their own UI toolkits (see [Mod component trees](#mod-component-trees)).
  Their IDs start with the tree's ID, such as FreshUI's `gum:12`.

Each component has these fields:

| Field | Meaning |
| --- | --- |
| `id`, `parent` | The component and its parent; parents come before their children. |
| `screen` | The screen or UI layer it belongs to. Widgets in a mod's retained UI layer report the layer's registration ID. |
| `type`, `role` | Its class, and in the `terraria` tree what it is: `button`, `label`, `slider`, `item_slot`, `hotbar_slot`, `screen`, or `group`. |
| `label` | Its text; for inventory and hotbar slots, the item and its stack size. |
| `context` | What it acts on, where its label isn't enough: the character or world a row's buttons belong to, or a mod layer's registration ID. |
| `bounds` | `[x, y, width, height]` in window pixels, clipped to what shows. |
| `value` | Its value, such as a slider's position from 0 to 1. |
| `slot` | For inventory and hotbar slots, the slot index. |
| `actions` | The actions it supports; empty when it's for inspection only. |

Set `interactive_only` to list only components with actions.

### Actions

| Action | Value | Behaviour |
| --- | --- | --- |
| `activate` | none | Invokes a button, selects a hotbar slot, or picks up or places an item in an inventory slot. |
| `secondary` | none | Invokes the secondary action, such as a right-click. |
| `set_value` | A number from 0 to 1 | Sets a slider or scroll position as a fraction of its range. |
| `set_text` | A string | Replaces a text field's contents and applies its filter. |

Direct actions work on retained controls that advertise them, including the title and settings menus, character and world selection, and parts of the in-game UI.
For gameplay and for controls without actions, use screenshots and the input tools.

### Mod component trees

A mod with its own UI toolkit can list its components by implementing `Aether.Testing.IUiTree` and registering it from its initializer with `UiTrees.Register`.

| Member | Purpose |
| --- | --- |
| `Id` | The tree's name in the layout, and its component IDs' prefix (`gum` gives `gum:12`). It can't contain `:` or be `terraria`. |
| `Owner` | The owning mod's manifest ID. |
| `Components(bool interactiveOnly)` | The components, parents before children, as JSON objects with the same fields as the game's. |
| `TryGetBounds(string componentId, out Rectangle bounds)` | A component's bounds in back-buffer pixels, for component screenshots; `false` if it's gone. |
| `TryAct(string componentId, string action, JsonNode value, out string error)` | Runs `activate`, `secondary`, `set_value`, or `set_text`; `false`, with the reason, if it can't. |

All of these run on the game's main thread, at the start of an update.

## Screenshots

`game_screenshot` captures the next drawn frame from the game's back buffer, not the screen, so other windows never cover it.
It returns a PNG image and its size.

| Argument | Default | Meaning |
| --- | --- | --- |
| `scale` | `0.5` | Downscale factor, above 0 and at most 1, box-filtered. |
| `crop` | none | `[x, y, width, height]` in window pixels, to look closely at an area. |
| `component_id` | none | A component from `game_ui_layout`, in any tree, to capture just its bounds. |
| `padding` | `0` | Window pixels to include around the component. |

To click something seen in a scaled or cropped screenshot, divide its image coordinates by `scale`, then add the crop's or component's origin.
A crop outside the frame fails.
Alpha is made opaque.
Use a full `scale` of 1 with a `crop` to check pixel-exact art.

`game_send` with `screenshot_scale` takes one screenshot after its commands, for a sequence and its result in one call.

## Performance

| Tool | Use |
| --- | --- |
| `frame_stats` | Measures the next `frames` drawn frames (default 120, up to 100,000), in the style of MangoHud's logging. It returns JSON: `fps`, `fps1Low` and `fps01Low`; `budgetMs` and `budgetUsed`; `frameMs`, `cpuMs` and `gpuMs` (Linux; `null` where the driver can't time it); `longFrames`; and `runtime` (`allocatedBytes` and `gcPauseMs` per frame, `heapBytes`, `allThreadsAllocatedBytes`, `gcCollections`, and `jitMethods`). Distributions give the mean, min, p50, p95, p99, and max. With `path`, it also writes each frame to a CSV file. |
| `overlay` | Shows the profiler overlay with frame times on screen. |
| `game_profile` | Records every managed allocation's class, size, and call stack over `frames` updates (1 to 3600), and returns the `top` methods and allocation sites (1 to 500) by bytes per frame. Needs the client started with the allocation profiler (Linux): `AETHER_LAUNCH_OPTIONS=--profile ./aether -- -aethermcp 47321`, or the VS Code profiler configuration. |

## Control commands

The tools are built on plain-text control commands.
`game_send` runs them, and so does `aether control` in the Aether checkout this mod's launcher uses (`.aether/client` by default):

```sh
.aether/client/aether control --port 47321 'state' 'ui layout interactive' 'ui act gum:12 activate'
```

Each command answers `ok [RESULT]` or `error MESSAGE`.
`aether control` reads commands one per line from standard input when none are given.

```text
mouse move X Y [MS]                  glide there over MS milliseconds (eased; 0 jumps)
mouse down|up [left|right|middle]
mouse click [BUTTON] [X Y] [FRAMES]  (move there,) press, hold FRAMES updates (default 2), release
mouse drag X1 Y1 X2 Y2 [MS] [BUTTON] press at 1, glide to 2 over MS, release
mouse wheel NOTCHES                  positive scrolls up
key down|up KEY[+KEY...]
key press KEY[+KEY...] [FRAMES]
type TEXT                            \n is Enter, \t Tab, \\ a backslash
pad connect|disconnect
pad button BUTTON[+BUTTON...] down|up|press [FRAMES]
pad stick left|right X Y [MS]        -1 to 1, Y up
pad trigger left|right VALUE [MS]    0 to 1
wait MS | wait frames N
screenshot [crop X Y W H | component ID [padding P]] [scale S] PATH
                                     answers WxH PATH; S is clamped to 0.05 to 1
state
ui layout [interactive]
ui act ID ACTION [JSON]              activate, secondary, set_value (0 to 1) or set_text
release
alloc [FRAMES]                       bytes the main thread allocates per update and per retained screen
quit
menu MODE                            on the title menu, switch to Main.menuMode MODE
profile [FRAMES] [TOP] PATH          needs the allocation profiler
frames [FRAMES] [PATH]
overlay [on|off]
resize W H
ping
```

The command reference also lives in `client/Terraria/Aether/Testing/ControlServer.cs` in the Aether checkout.

## Example

To join a server and open a mod's menu:

1. Start the game with `./aether -- -aethermcp 47321 -join 127.0.0.1 -port 7777`.
2. Poll `game_ui_layout` until character selection shows, then `activate` the character's `Play` button.
3. Poll `game_state` until `gameMenu` is `false`, then let the world settle with `game_wait`.
4. Press the menu's key with `key_press`, then read `game_ui_layout` to find the menu's components.
5. Act on them with `game_ui_action`, and check the result with `game_state` or `game_screenshot`.

Read the layout again before each action that depends on a change.
