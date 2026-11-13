# Remote UI automation

Start the client with `./aether run --mcp 47321` or `./aether launch --mcp 47321`.
It serves the `aether-game` MCP server itself on loopback (`http://127.0.0.1:47321/mcp`), and runs the tools' actions on the game thread.

## Inspect and interact

1. Call `game_ui_layout` to read the next drawn frame's component trees.
2. Find the component by its `label`, `screen`, `role`, or `context` and choose one of its `actions`.
3. Call `game_ui_action` with that `component_id` and action, including a `value` for `set_value` or `set_text`.
4. Request another layout after navigation or if the target has become stale.

The layout lists each component tree separately under `trees`, each with its `id`, the `owner` that registered it, and its `components`.
The `terraria` tree (owner `aether`) is the game's retained UI; mods add trees of their own UI toolkits (see [Mod component trees](#mod-component-trees)), such as FreshUI's `gum` tree.
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
`game_screenshot` with a `component_id` (from any tree) captures just that component, with `padding` window pixels around it.

## Mod component trees

A mod with its own UI toolkit can list its components in the layout by implementing `Aether.Testing.IUiTree` and registering it with `UiTrees.Register` from its initializer.
The tree's `Id` names it in the output and prefixes its component IDs (`gum:12`), which is how `game_ui_action` and component screenshots find it.
Its `Components` describe each component as a JSON object with the same fields as the game's (`id`, `parent`, `type`, `label`, `bounds` in back-buffer pixels, `value`, `actions`), and `TryAct` runs the same actions (`activate`, `secondary`, `set_value` from 0 to 1, `set_text`).
Everything runs on the game thread, at the start of an update.

## Raw control commands

The same operations are available through `./aether control` and `game_send`.

```text
ui layout [interactive]
ui act ID activate
ui act gum:12 activate
ui act ID secondary
ui act ID set_value 0.5
ui act ID set_text "Torch"
```
