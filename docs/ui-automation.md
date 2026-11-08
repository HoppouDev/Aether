# Remote UI automation

Start the client with `./aether run --control 47321` or `./aether launch --control 47321`.
The control socket listens on loopback, and the `aether-game` MCP server uses it to run actions on the game thread.

## Inspect and interact

1. Call `game_ui_layout` to read the next drawn frame's retained UI.
2. Find the component by its `label`, `screen`, `role`, or `context` and choose one of its `actions`.
3. Call `game_ui_action` with that `component_id` and action, including a `value` for `set_value` or `set_text`.
4. Request another layout after navigation or if the target has become stale.

Use `interactive_only: true` to return just the actionable components.
The full layout includes parent IDs, the owning screen and component types, and clipped `[x, y, width, height]` bounds in back-buffer pixels.
Rows for saved characters and worlds supply their names as context for the buttons inside them.
Widgets in mod UI layers supply the layer's registration ID as their context.
Inventory and hotbar slots include their slot index, item label, and stack size.
IDs belong to the latest layout on the running client.
Each action checks the target against a fresh drawn frame before running.

| Action | Value | Behavior |
| --- | --- | --- |
| `activate` | Omit | Invoke a button, select a hotbar slot, or pick up or place an item in a supported inventory slot. |
| `secondary` | Omit | Invoke a component's secondary action. |
| `set_value` | Number from `0` to `1` | Set a slider value or scroll position as a fraction of its range. |
| `set_text` | String | Replace the contents of a search field and apply its filter. |

Components with empty `actions` are available for inspection.
Direct actions work on the retained controls that advertise them, including title and settings menus, character and world selection, and parts of the in-game UI.
Use `game_screenshot` and the input tools for gameplay or immediate-mode controls that lack component actions.

## Raw control commands

The same operations are available through `./aether control` and `game_send`.

```text
ui layout [interactive]
ui act ID activate
ui act ID secondary
ui act ID set_value 0.5
ui act ID set_text "Torch"
```
