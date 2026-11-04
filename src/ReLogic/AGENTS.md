# ReLogic: Re-Logic's framework library

The engine layer between Terraria and FNA: asset loading, fonts, RGB peripherals, platform services and small utilities.
It ships inside `Terraria.exe` as the embedded `Terraria.Libraries.ReLogic.ReLogic.dll`.
Here it's decompiled from that DLL (Linux build) and compiled by `ReLogic/ReLogic.csproj`; `Terraria.csproj` embeds the result under the same name.
Edits here become `patches/ReLogic/…` like any other `src/` edit.

## Rules for editing

- **Dependencies only point one way**: Terraria references ReLogic, never the reverse.
  Code here can't use `Terraria.*` types; expose a hook (event, interface, delegate) that Terraria fills in instead.
- It's a separate assembly: `internal` members on either side aren't visible to the other (the only `InternalsVisibleTo` is for `ReLogic.Tests`).
  Changing a public signature means patching every caller in `src/Terraria/` in the same change.
- ReLogic references only FNA, Newtonsoft.Json, Ionic.Zip.CF and SteelSeriesEngineWrapper (from `lib/`); adding a dependency means adding it to `ReLogic/ReLogic.csproj`.
- The base is the **Linux** build, which hard-wires `Platform.Current = new LinuxPlatform()`; Aether patches it to pick the platform at runtime (see [OS/AGENTS.md](OS/AGENTS.md)).
  `WindowsPlatform`, `OS/Windows/WindowService` and `Localization/IME/Windows/` assume XNA's WinForms window and stay unused.

## Modules

| Folder | What it does | Entry points |
| --- | --- | --- |
| [`Content/`](Content/AGENTS.md) | The asset system behind `Main.Assets` | `AssetRepository.Request<T>(name, mode)` returns a cached `Asset<T>` (`.Value`, `.IsLoaded`, `.State`; `Asset<T>.Empty` as a placeholder) |
| [`Content/Readers/`](Content/AGENTS.md#readers) | Turn files into objects, registered per extension in `Terraria/Initializers/AssetInitializer` | `XnbReader` (`.xnb`), `PngReader` (`.png`) |
| [`Content/Sources/`](Content/AGENTS.md#sources-and-priority) | Where assets come from | `XnaContentSource` (the game's `Content/`), `FileSystemContentSource` / `ZipContentSource` (resource packs, created in `Terraria/IO/ResourcePack`) |
| [`Graphics/`](Graphics/AGENTS.md) | Text rendering | `DynamicSpriteFont` (all in-game fonts, via `Terraria/GameContent/FontAssets`) and its `.xnb` reader |
| [`Peripherals/RGB/`](Peripherals/AGENTS.md) | Keyboard and mouse lighting | `ChromaEngine` (`Main.Chroma`), with Razer, Corsair, Logitech and SteelSeries device groups; the effects themselves are in `Terraria/GameContent/RGB/` |
| [`OS/`](OS/AGENTS.md) | Platform services | `Platform.Get<T>()` / `Platform.IsLinux`; services `IPathService` (save path), `IClipboard`, `IWindowService`, `IImeService` |
| [`Localization/IME/`](Localization/AGENTS.md) | Text input for non-Latin languages | `FnaIme` on every OS (registered by `LinuxPlatform`, `OsxPlatform` and `FnaWindowsPlatform`) |
| `Reflection/` | ID-name lookup | `IdDictionary` (`ItemID.Search` etc.: `GetName`, `GetId`, `TryGetName`) |
| `Threading/` | Parallel loops | `FastParallel.For` (used by the lighting engine and snapshots) |
| `Text/` | Text wrapping and metrics | `WrappedTextBuilder`, `TextUtilities` |
| `Utilities/` | Small value types and helpers | `SlotId` (handle for tracked sounds), `SlotVector`, `Vector2D` (double-precision vector used by world generation), `Crc32` |
| `IO/` | Console mirroring | `ConsoleOutputMirror` |

## How assets resolve

- `Request<T>` normalizes the name (`AssetPathHelper.CleanPath`), returns the cached asset if it's already loading or loaded, and otherwise loads it in the requested `AssetRequestMode`: `ImmediateLoad` (default), `AsyncLoad` or `DoNotLoad`.
- Async loads finish on the main thread through `TransferCompletedAssets()`, which `Main` calls every frame.
- The repository takes the **first** source that has the asset (`FindSourceForAsset`).
  `Terraria/GameContent/AssetSourceController` orders the sources as enabled resource packs (by `SortingOrder`) followed by vanilla, so packs override the game's files.
  Language and audio get the same list reversed (lowest to highest priority).
- Assets in packs that fail validation are rejected (`IContentValidator`, `RejectedAssetCollection`) and fall back to the next source.
