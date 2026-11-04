# ReLogic.Content: the asset system

Everything the game loads from disk (textures, sounds, fonts, effects) goes through here: `Main.Assets` is an `AssetRepository`, created in `Terraria/Initializers/AssetInitializer`.

## API

- `IAssetRepository.Request<T>(name, mode = ImmediateLoad)` returns an `Asset<T>`, cached by name; requesting a name twice returns the same object.
  The name is a path relative to the content root without extension (`"Images/Item_1"`); `AssetPathHelper.CleanPath` normalizes separators to `\` and resolves `.`/`..`.
- `Asset<T>`: `.Value` (null until loaded), `.IsLoaded`, `.State` (`NotLoaded`, `Loading`, `Loaded`), `.Name`, `.Source`.
  `Asset<T>.Empty` is a shared never-loaded placeholder; the game uses it as an initial value (e.g. `Main.ScreenShaderRef`).
- `AssetRequestMode`: `ImmediateLoad` (load synchronously now), `AsyncLoad` (queue on `AsyncAssetLoader`), `DoNotLoad` (create the handle only).
- `TransferCompletedAssets()` hands finished async loads over on the main thread; `Main` calls it every frame.
  Don't read `.Value` of an async asset on another thread.
- Handlers on the repository: `AssetLoadFailHandler` (the game sets `Main.OnceFailedLoadingAnAsset`), `AssetValueUpdatedHandler`, and the watcher handlers below.

## Sources and priority

- `IContentSource` (`HasAsset`, `OpenStream`, `GetExtension`, `GetAllAssetsStartingWith`, rejection tracking) abstracts where files come from:
  - `Sources/XnaContentSource`: the game's `Content/` folder; indexes every `*.xnb` file at construction.
  - `Sources/FileSystemContentSource`, `Sources/ZipContentSource`: resource packs (folder or zip), created by `Terraria/IO/ResourcePack`.
  - `Sources/XnaDirectContentSource`: another `.xnb` directory source, which can rescan its folder (`Refresh()`).
    Neither XNA source supports the hot-reload watcher (`FileWatcherPath` is null).
- `FindSourceForAsset` takes the **first** source that has the asset.
  `Terraria/GameContent/AssetSourceController` passes enabled resource packs (ordered by `SortingOrder`) followed by vanilla, so packs win.
- `SetSources(...)` finishes pending async loads, swaps the source list and reloads every asset whose winning source changed (`ReloadAssetsIfSourceChanged`); this is how enabling a resource pack takes effect without a restart.
- A source can reject an asset (`IContentValidator`, `IRejectionReason`, `RejectedAssetCollection`); the repository then falls back to the next source.
  Vanilla validation compares pack textures against `Terraria.IO.Data.ResourcePacksDefaultInfo.tsv` (sizes must match).

## Readers

- `AssetReaderCollection` maps file extensions to `IAssetReader`s; the game registers `Readers/PngReader` for `.png` and `Readers/XnbReader` for `.xnb`.
- `PngReader` loads with `Texture2D.FromStream` and then **premultiplies alpha** (`Color.FromNonPremultiplied`), matching how XNB textures are stored.
  Textures created another way need the same treatment or they'll blend wrongly.
- `XnbReader` runs FNA's `ContentManager` machinery per thread (`InternalContentManager`), so any XNA/FNA content type reader works, including `ReLogic.Graphics.DynamicSpriteFontReader`.
- To support a new file type, write an `IAssetReader` and register it with the extension in `AssetInitializer`.

## Hot reload

`EnableAssetWatcher()` (enabled when the game's `Setting_AssetFileWatcherEnabled` is on) watches the sources' directories (`FileChangeWatcher`, `AssetChangeWatcher`) and reloads changed assets in place, reporting through `AssetWatcherValueUpdatedHandler` / `AssetWatcherUpdateFailedHandler`.
