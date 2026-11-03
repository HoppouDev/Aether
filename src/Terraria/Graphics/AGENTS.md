# Graphics (Terraria.Graphics)

Rendering runs on **FNA** (`Microsoft.Xna.Framework.*`, from `lib/FNA.dll`), with assets loaded through **ReLogic** (`ReLogic.Content`).
Most world drawing is still in `Main.cs` (`DoDraw`) and `GameContent/Drawing/` (`TileDrawing`, `WallDrawing`, backgrounds).
This folder holds the shared infrastructure.

## Coordinates and frame state

- World space is pixels (16 px per tile).
  Draw at `worldPos - Main.screenPosition` into `Main.spriteBatch`.
  Screen size is `Main.screenWidth`/`screenHeight`.
- `Main.GameViewMatrix` (`SpriteViewMatrix`) carries zoom and the transform for world drawing; pass its matrix when starting a world-space batch.
  `Camera` / `VirtualCamera` / `CameraModifiers/` handle screen shake and pans.
- Wrong `SpriteBatch.Begin` parameters (sort mode, blend state, sampler, matrix) are the most common rendering bug.
  Copy them from the code around the place you're drawing.

## Shaders and effects

- Three compiled effect files ship in the game's `Content/` folder (`PixelShader.xnb`, `ScreenShader.xnb`, `TileShader.xnb`), held in `Main.PixelShaderRef` / `ScreenShaderRef` / `TileShaderRef`.
  Each effect is a **pass name** inside one of these.
  You can't add a pass without rebuilding the `.xnb`; new effects need their own compiled `Effect` asset.
- `Shaders/GameShaders`: `GameShaders.Armor` (dyes, applied per draw by shader index), `GameShaders.Hair` and `GameShaders.Misc[name]` (`MiscShaderData`, used by projectile trails etc.).
  Registered in `Initializers/DyeInitializer`.
- `Effects/`: full-screen effects and skies, keyed by string:
  - `Filters.Scene["Name"]`: post-process `Filter`s wrapping `ScreenShaderData`, activated with `Filters.Scene.Activate("Name", position)`.
  - `SkyManager.Instance["Name"]`: `CustomSky` backgrounds (implementations in `GameContent/Skies/`).
  - `Overlays.Scene`: `Overlay`s drawn over the scene.
  - All registered in `Initializers/ScreenEffectInitializer`.
- `VertexStrip`, `TileBatch` and the `*Drawer` structs (`RainbowRodDrawer`, `FinalFractalHelper`, `EmpressBladeDrawer`, …) draw custom-vertex trails for specific weapons.

## Lighting

- `Light/`: two engines, selected through `Lighting.Mode` in `../Lighting.cs`: `LightingEngine` (used for `LightMode.Color`, the default; `LightMap` filled by `TileLightScanner`) and `LegacyLighting` (used for the `White`, `Retro` and `Trippy` modes).
- Gameplay code adds light through `Lighting.AddLight(position, r, g, b)` (world-pixel position) or `Lighting.AddLight(i, j, torchID, amount)`.
  Read light with `Lighting.GetColor(x, y)` when tinting sprites.

## Particles and capture

- `Renderers/`: particle system (`ParticleRenderer`, pooled `ParticlePool<T>`, particle types such as `PrettySparkleParticle`, `FadingParticle`).
  World particle renderers hang off `Main` (`Main.ParticleSystem_World_OverPlayers`, …).
  Networked one-shot effects go through `GameContent/Drawing/ParticleOrchestrator`.
  Player rendering lives here too (`LegacyPlayerRenderer`, `IPlayerRenderer`; draw layers in `DataStructures/PlayerDrawLayers`).
- `Capture/`: the camera-mode screenshot/capture system.

## Notes

- Textures: use the `TextureAssets` handles or `Main.Assets.Request<Texture2D>("Images/...")` (paths relative to `Content/`, no extension; the `.xnb` files are in the game folder, not this repo).
  Check `asset.IsLoaded` or request with `AssetRequestMode.ImmediateLoad` when the texture is needed right away.
- Nothing graphical runs on a dedicated server (`Main.dedServ`).
  Keep draw/asset code out of paths the server executes.
