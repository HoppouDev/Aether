# ReLogic.Peripherals.RGB: keyboard and mouse lighting

The engine behind Terraria's RGB lighting (biome colors on the keyboard, health bars on the number keys, and so on).
The game's effects live in `Terraria/GameContent/RGB/`; this folder is the engine and the vendor backends.

## Engine

- `ChromaEngine` (`Main.Chroma`, created in `Terraria/Main.cs`; set up in `Terraria/Initializers/ChromaInitializer`) owns device groups and shaders.
- Device groups: `AddDeviceGroup(name, group)`, `EnableDeviceGroup` / `DisableDeviceGroup`, `EnableAllDeviceGroups`.
  The game registers `"Razer"`, `"Corsair"`, `"Logitech"` and `"SteelSeries"` and enables them per its settings.
- Shaders: `RegisterShader(shader, condition, layer)` / `UnregisterShader`.
  A `ChromaShader` overrides `Update(elapsedTime)` and `Process(device, fragment, quality, time)`, writing colors into the `Fragment` (the set of key positions being lit).
  `ChromaCondition` decides when the shader is active; `ShaderLayer` (`Bottom` → `Background` → `Biome` → … → `Alert` → `CriticalAlert` → `Menu` → `Top`) sets the stacking order, with `ShaderBlendState`/`BlendMode` controlling how layers combine.
- `[RgbProcessor(EffectDetailLevel...)]` on a shader's process methods selects the method per detail level (`EffectDetailLevel`).
- Key bindings for effects: `BindKey(key, triggerName)` returns an `RgbKey`; `HotkeyCollection` tracks them.
- `Update(totalTime)` runs the shaders and pushes colors to every enabled device; `DebugDraw` renders a preview with `ReLogic.Graphics.IDebugDrawer`.

## Backends

| Folder | Talks to | On Linux |
| --- | --- | --- |
| `RGB/Razer/` | Razer Chroma SDK (`RzChromaSDK.dll`, P/Invoke) | Not available; logs "Razer Chroma not supported" at startup |
| `RGB/Corsair/` | Corsair CUE SDK (`CUESDK_2015.dll`) | Not available; logs "Corsair RGB not supported" |
| `RGB/Logitech/` | Logitech LED SDK (`LogitechLedEnginesWrapper`) | Not available; logs "Logitech RGB not supported" |
| `RGB/SteelSeries/` | SteelSeries GameSense through `lib/SteelSeriesEngineWrapper.dll` | Depends on the SteelSeries engine running |

Those startup messages are normal and harmless: a backend whose native SDK fails to load just disables itself (`DeviceInitializationException`).
`VirtualRgbDeviceGroup` exists for previewing without hardware.
