# ReLogic.OS: platform services

A small service locator over the operating system.

## How it works

- `Platform.Current` is the platform object.
  The decompiled base hard-wires `new LinuxPlatform()` (Re-Logic builds one exe per OS); Aether's patch replaces that with `CreateForCurrentOS()`: `FnaWindowsPlatform` when `Environment.OSVersion` is Windows, `OsxPlatform` when `SDL_GetPlatform()` reports macOS, otherwise `LinuxPlatform` (also the fallback if SDL can't load).
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

- Adding a service: define the interface here, implement it, register it in `LinuxPlatform`, `OsxPlatform` and `FnaWindowsPlatform`, then use `Platform.Get<T>()` from Terraria.
- `Windows/FnaWindowsPlatform` (Aether's) is Windows under FNA: the SDL-based Linux clipboard and window services, `FnaIme`, and the Windows `PathService` (Documents/My Games).
  Re-Logic's `WindowsPlatform` (window-message hook, Imm32 IME, `WindowsMouseNotifier`) assumes XNA's WinForms window and is never constructed.
