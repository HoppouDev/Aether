# Localization (Terraria.Localization + Lang.cs)

All player-facing text is keyed and loaded from JSON files embedded in the exe.

## Where the text lives

- Files are in `src/Resources/` and get embedded under their file names.
  Each of the 12 cultures (`en-US`, `de-DE`, `it-IT`, `fr-FR`, `es-ES`, `ru-RU`, `zh-Hans`, `zh-Hant`, `pt-BR`, `pl-PL`, `ja-JP`, `ko-KR`) has up to seven files: `Terraria.Localization.Content.<culture>.json` plus `.Game`, `.Items`, `.Legacy`, `.NPCs`, `.Projectiles` and `.Town` variants.
- Structure is `{ "Category": { "Key": "Text" } }`, addressed as `"Category.Key"` (e.g. `ItemName.IronPickaxe`, `ItemTooltip.X`, `NPCName.X`, `ProjectileName.X`, `BuffDescription.X`, `UI.X`, `LegacyMenu.12`).
- The files are **lenient JSON** (trailing commas appear), parsed with Newtonsoft.
  Edit them as text; don't run them through a strict JSON re-serializer, which would rewrite the whole file.
- `en-US` is the fallback culture.
  `LanguageManager.ReloadLanguage` loads English first and then overlays the target culture, so a key missing in a translation shows the English text.
  A key missing everywhere shows **the key itself** (`GetText` creates a `LocalizedText` whose value is the key).

## Text features

- `{0}`, `{1}`: positional format arguments (`Language.GetTextValue(key, args)`, `LocalizedText.Format(...)`).
- `{$Category.Key}`: copies another entry's text, resolved once at load (`ProcessCopyCommandsInTexts`), e.g. `"{$ItemName.DemonAltar}"`.
- Named substitutions with an object (`LocalizedText.FormatWith(obj)`), and `$`-suffixed keys for random variations (`LanguageManager.VariationSeparatorSign`).
- Resource packs can override text: `LoadFromContentSources` reads `Localization/<culture>*.json|.csv` from active content sources after the embedded files.

## Code API

- `Language.GetText(key)` returns a `LocalizedText` that updates when the language changes (store this, not the string).
  `Language.GetTextValue(key, args...)` returns the current string.
  `Language.Exists(key)` checks whether a key is defined.
- `Lang.cs` (root namespace) holds legacy tables and name caches: `Lang.menu[]`, `Lang.inter[]`, `Lang.misc[]`, `Lang.gen[]`, `Lang.tip[]`, … map to the `LegacyMenu.N`, `LegacyInterface.N`, … keys.
  `Lang.GetItemName(id)`, `Lang.GetNPCName(...)`, `Lang.GetProjectileName(...)` read caches filled with `FillNameCacheArray<ItemID, short>("ItemName", ...)`, so **the localization key for an item is its `ItemID` constant name**.
  Renaming an ID constant breaks its text lookup.
- Networked text uses `NetworkText` (`FromKey(key, args)`, `FromLiteral`, `FromFormattable`), so each client renders it in its own language.
  Send keys, not pre-translated strings.

## Adding text

Add the key to `src/Resources/Terraria.Localization.Content.en-US*.json` (the category's natural file) at minimum; other cultures fall back to English.
Add it to translated files only if you have real translations.
