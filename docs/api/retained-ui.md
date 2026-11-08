# Retained in-game UI

Use `RetainedUi.Register` to mount a Terraria `UIState` with its own widget tree during gameplay.
The registration requires the optional Aether API and belongs in your mod initializer.
Give each layer a unique `your-mod:name` ID and a factory that returns a new `UIState`.
The factory runs on the client game thread when the layer first becomes visible in a world, after the game assets are available.

```csharp
using Aether.Api;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

// In Initialize()
RetainedUiLayer counter = RetainedUi.Register("example:counter", () =>
{
    var state = new UIState();
    var button = new UITextPanel<string>("Clicks: 0") { VAlign = 0.5f };
    button.Left.Set(16, 0);
    button.Width.Set(160, 0);
    button.Height.Set(44, 0);
    int clicks = 0;
    button.OnLeftClick += (_, _) => button.SetText($"Clicks: {++clicks}");
    state.Append(button);
    return state;
}, initiallyVisible: false);
Keybinds.Register("example:counter", "F7", () => counter.IsVisible = !counter.IsVisible);
```

`initiallyVisible` defaults to `true`.
Set `IsVisible` on the game thread to show or hide the layer.
The setting persists for the current run, and the UI tree is kept when hidden and released at the end of the world session.
Terraria activates and deactivates the state as it is shown and hidden.
Use `OnInitialize` to build a custom state's children, `Update` for changes over time, and `OnDeactivate` for cleanup when the state is hidden.
If a factory or hosted widget throws during layout, update, or draw, Aether logs its registration ID and disables that layer for the session.

Coordinates follow Terraria's UI scale, and Aether recalculates the tree on resolution and UI scale changes.
The `StyleDimension.Set` arguments are pixels and a fraction of the parent.
Call `Recalculate()` after changing layout properties at runtime.
Use existing widgets like `UIPanel`, `UIText`, `UITextPanel<T>`, `UIImage`, and `UIList`, or subclass `UIElement` and draw with the supplied batch in `DrawSelf`.
For a packaged image, open its resource path with `ModResources.OpenRead`, load it with `Texture2D.FromStream` on the client game thread, and pass the texture to `UIImage`.
Dispose the resource stream after loading.
If you dispose the texture in `OnDeactivate`, load a replacement in `OnActivate` before drawing the state again.

Layers draw in registration order above the gameplay HUD and inventory, before mouse text and the cursor.
The title menu, full-screen map, settings, photo mode, hidden HUD, and full-screen game dialogs suspend the layers.
Factories and widgets run only on clients, including clients connected to multiplayer worlds.

Mouse input goes to the topmost layer with a widget under the pointer.
An active press keeps its layer until release.
Empty space in the root state lets the game receive mouse input.
Set `IgnoresMouseInteraction` on decorative widgets whose whole subtree should let input through.
Put clickable areas in child widgets so they have explicit hit bounds.
The host routes mouse input and does not assign controller navigation.
Controls with native click handlers can appear in [remote UI automation](../ui-automation.md) and be activated by component ID.
Use the layer's registration ID as the layout `context` to distinguish widgets with the same label.
