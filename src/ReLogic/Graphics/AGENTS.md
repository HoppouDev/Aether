# ReLogic.Graphics: fonts and debug drawing

## DynamicSpriteFont

All in-game text is drawn with `DynamicSpriteFont`, Re-Logic's replacement for XNA's `SpriteFont`.

- Fonts are `.xnb` assets read by `DynamicSpriteFontReader` (a FNA `ContentTypeReader`), loaded in `Terraria/Initializers/AssetInitializer` into `Terraria/GameContent/FontAssets` (`FontAssets.MouseText`, `ItemStack`, `DeathText`, …) from paths like `"Fonts/Mouse_Text"`.
- A font is split into texture pages (`FontPage`); glyph lookup falls back to a default character for unsupported characters (`IsCharacterSupported`, `AreCharactersSupported`).
- Drawing: the `SpriteBatch.DrawString(font, ...)` extension methods in `DynamicSpriteFontExtensionMethods` (same overload shapes as XNA's), or `DrawCustomFast` with a per-character callback.
- Layout: `MeasureString`, `GetCharacterMetrics` (`ReLogic.Text.GlyphMetrics`), `CreateWrappedText` (culture-aware word wrapping), `CreateCroppedText`.
- Aether: `SetCharacterImage` draws a character from another texture (a finer rendering or a distance field, which FNA's `Texture.EffectEXT` can shade) at a texel scale and fractional offset, keeping its spacing; each character's data carries that `Offset` and `Scale`, which the draw paths use in place of its padding.
- Most UI text in the game is drawn indirectly through `Terraria/UI/Chat/ChatManager` (which adds `[tag]` snippets and shadows) and `Utils.DrawBorderString*`; change those before touching the font itself.

## Debug drawing

`IDebugDrawer` / `BasicDebugDrawer` is a minimal line-and-box drawer; the RGB engine uses it for `ChromaEngine.DebugDraw`.
