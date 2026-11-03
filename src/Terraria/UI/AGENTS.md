# UI (Terraria.UI + GameContent/UI)

Terraria runs **two UI systems side by side**:

1. **Legacy immediate-mode UI**: the main menu, inventory, hotbar, chests, NPC chat, map and mouse text, drawn by hand from `Main.cs` (`DrawMenu` ~line 49320, `DrawInterface` ~line 45865).
2. **Retained element tree** (`UIElement`/`UIState`/`UserInterface`): used for newer screens such as character/world creation and selection, bestiary, achievements, controls, resource packs, workshop and emotes.

Both draw through `SpriteBatch` in screen space and scale with `Main.UIScale`.

## Retained UI framework (this folder)

- `UIElement`: layout through `Left`/`Top`/`Width`/`Height` (`StyleDimension`: pixels plus a percent of the parent), `HAlign`/`VAlign`, `SetPadding`.
  Children: `Append`, `RemoveChild`, `RemoveAllChildren`.
  Override `OnInitialize` (build children), `OnActivate`/`OnDeactivate`, `Update`, `DrawSelf` (draw this element) / `DrawChildren`.
  Input comes through events (`OnLeftClick`, `OnRightClick`, `OnMouseOver`, `OnMouseOut`, `OnLeftMouseDown`, …) or by overriding `LeftClick` and friends.
- After changing layout properties at runtime, call `Recalculate()` on the element (or its parent); otherwise the change won't show.
- `UIState`: a root element (a full screen).
  `UserInterface`: hosts one active state (`SetState(state)`; `null` clears it).
- Hosts on `Main`: `Main.MenuUI` (title-menu screens, shown while `Main.menuMode == 888`, e.g. `Main.GoToWorldSelect()`) and `Main.InGameUI` (in-game full screens).
  In game, open and close a screen with `IngameFancyUI.OpenUIState(state)` / `IngameFancyUI.Close()`.

## Other files here

- `ItemSlot.cs`: draws and handles every inventory-style slot (`ItemSlot.Draw(spriteBatch, inv, context, slot, position)`); `context` selects the slot type (inventory, armor, chest, shop, …).
  `ChestUI.cs`, `CraftingUI.cs`, `ItemSorting.cs`, `ItemTooltip.cs` / `TooltipProcessor.cs` cover the rest of inventory handling.
- `GameInterfaceLayer` / `LegacyGameInterfaceLayer`: the in-game HUD is an ordered list of ~43 named layers (`"Vanilla: Inventory"`, `"Vanilla: Hotbar"`, `"Vanilla: Mouse Text"`, …) built in `Main.SetupDrawInterfaceLayers`.
  To add HUD drawing, insert a layer at the right position rather than patching the middle of `DrawInterface`.
- `Gamepad/`: controller navigation.
  `UILinkPoint`s on the legacy UI and `UILinkPointNavigator`; retained screens wire their elements to link points so they're usable with a gamepad.
  Keep this in mind when adding buttons.
- `Chat/`: chat text rendering and snippet parsing (`ChatManager`, `TextSnippet`, `ITagHandler`).
  The `[tag]` handlers themselves (color, item, name, achievement, …) are in `GameContent/UI/Chat/`, registered in `Initializers/ChatInitializer`.
- `InGamePopups.cs`, `InGameNotificationsTracker.cs`, `IngameFancyUI.cs`, `IngameUIWindows.cs`.

## GameContent/UI

- `States/`: the concrete screens (`UICharacterSelect`, `UICharacterCreation`, `UIWorldSelect`, `UIWorldCreation`, `UIWorldCreationAdvanced`, `UIBestiaryTest`, `UIAchievementsMenu`, `UIManageControls`, `UIResourcePackSelectionMenu`, `UIWorkshopHub`, `UIVirtualKeyboard`, …).
- `Elements/`: reusable widgets (~74): `UIPanel`, `UIText`, `UITextPanel`, `UIImage`, `UIImageButton`, `UIList`, `UIScrollbar`, sliders, list items for characters/worlds/bestiary/creative.
  Build new screens from these.
- `ResourceSets/` (health/mana bar styles), `BigProgressBar/` (boss health bars), `Minimap/`, `Chat/`, `NewCraftingUI.cs`, `NPCChatPanel.cs`, `EmoteBubble.cs`, popup text, custom currency.

## Conventions

- All user-visible text goes through localization (`Language.GetTextValue("UI.SomeKey")`, `Lang.menu[...]` for legacy menu strings); see [../Localization/AGENTS.md](../Localization/AGENTS.md).
  Don't hard-code English.
- Textures come from `Main.Assets.Request<Texture2D>("Images/UI/...")` or `TextureAssets`, never loaded directly from disk.
