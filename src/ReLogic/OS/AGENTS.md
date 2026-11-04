# ReLogic.OS: platform services

A small service locator over the operating system.

## How it works

- `Platform.Current` is the platform object; this Linux build hard-wires it to `new LinuxPlatform()`.
  `Platform.IsLinux` / `IsWindows` / `IsOSX` read its `Type`.
- Services are registered per platform (`RegisterService<T>`) and looked up by interface: `Platform.Get<T>()`, `Platform.Has<T>()`, `Platform.IfHas<T>(action)`.
  The implementations (`Linux/`, `Base/`, …) are `internal`; Terraria only ever sees the interfaces.
- Services are registered in two steps: the constructor registers the basics, and `InitializeClientServices(windowHandle)` (called from `Main.ClientInitialize()` once the window exists) adds client-only ones such as the IME.
  A dedicated server never runs `ClientInitialize`, so it never gets those.

## Services

| Interface | Linux implementation | Used for |
| --- | --- | --- |
| `IPathService` | `Linux/PathService` | `GetStoragePath()`: `$XDG_DATA_HOME`, else `~/.local/share` (the game appends `Terraria`); also `OpenURL`, `MoveToRecycleBin`, `ExpandPathVariables` |
| `IClipboard` | `Linux/Clipboard` | `Value` / `MultiLineValue`, via SDL's clipboard |
| `IWindowService` | `Linux/WindowService` (`Base/SDLWindowService`) | Window title, icon flashing, DPI scaling, position and bounds through SDL |
| `IImeService` | `Localization/IME/FnaIme` | Text input; see [../Localization/AGENTS.md](../Localization/AGENTS.md) |
| `IMouseNotifier` | none on Linux (Windows only) | Mouse connect/disconnect events; guard with `Platform.IfHas` |

## Editing notes

- Adding a service: define the interface here, implement it for Linux (and register it in `LinuxPlatform`), then use `Platform.Get<T>()` from Terraria.
- `Windows/` and `OSX/` are present but never constructed in this build; changes there have no effect.
