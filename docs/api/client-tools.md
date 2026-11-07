# Client tools

Register keybinds, commands, and overlays in the mod initializer.
Use a unique ID of the form `your-mod:name` for each registration.
The name can contain lowercase ASCII letters, digits, `.`, `_`, and `-`.
A registration with an invalid or duplicate ID fails during initialization.
The API logs and disables callbacks that throw.

```csharp
Keybinds.Register("example:inspect", "F6", () => Console.WriteLine("Inspect"));
LocalCommands.Register("example:echo", text => Console.WriteLine(text));
HudOverlays.Register("example:label", canvas =>
    canvas.DrawText("Example", 16, 80, HudColor.White));
```

Keybinds fire on a keyboard press while the game accepts gameplay input.
Edit `aether-api-keybinds.json` in the save profile and restart to change a binding.
Keys use the game's keyboard key names, such as `F6` and `LeftShift`.
An invalid override falls back to the registered default.
If several mods bind the same key, each receives the press in registration order.

Enter `/example:echo hello` in the game chat to run the local command.
The callback receives the argument text, and the command stays on the client.

HUD callbacks run in registration order while an in-world interface is visible.
Canvas coordinates use the game UI scale.
`Width` and `Height` describe the canvas in those coordinates.
`DrawTexture` takes a path relative to your mod's packaged `resources/` directory.
Pass the destination position and dimensions and a tint as `HudColor`.
Textures are cached for the current world session and disposed when it ends.
Use the canvas only during the callback that received it.
