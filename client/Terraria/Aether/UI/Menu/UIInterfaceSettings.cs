using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.Localization;
using Terraria.UI;

namespace Aether.UI.Menu;

/// <summary>Settings → Interface (menuMode 1112).</summary>
public class UIInterfaceSettings : UIMenuScreen
{
    private const float Scale = 0.6f;

    public UIInterfaceSettings()
    {
        ItemsTop = 210f;
        ItemSpacing = 28f;
    }

    protected override void BuildItems()
    {
        AddToggle(() => Lang.menu[Main.showItemText ? 71 : 72].Value, delegate
        {
            Main.showItemText = !Main.showItemText;
        }, Scale);
        AddToggle(() => CachedText.Concat(Lang.menu[123].Value, " ", Lang.menu[124 + Main.invasionProgressMode].Value), delegate
        {
            Main.invasionProgressMode = (Main.invasionProgressMode + 1) % 3;
        }, Scale);
        AddToggle(() => Lang.menu[Main.placementPreview ? 128 : 129].Value, delegate
        {
            Main.placementPreview = !Main.placementPreview;
        }, Scale);
        AddToggle(() => Lang.inter[ItemSlot.Options.HighlightNewItems ? 117 : 116].Value, delegate
        {
            ItemSlot.Options.HighlightNewItems = !ItemSlot.Options.HighlightNewItems;
        }, Scale);
        AddToggle(() => Lang.menu[Main.MouseShowBuildingGrid ? 229 : 230].Value, delegate
        {
            Main.MouseShowBuildingGrid = !Main.MouseShowBuildingGrid;
        }, Scale);
        AddToggle(() => Lang.menu[Main.GamepadDisableInstructionsDisplay ? 241 : 242].Value, delegate
        {
            Main.GamepadDisableInstructionsDisplay = !Main.GamepadDisableInstructionsDisplay;
        }, Scale);
        // DrawMenu cycles these two without a sound.
        AddItem(() => CachedText.Format("UI.SelectMapBorder", Language.GetTextValue(CachedText.Concat("UI.MinimapFrame_", Main.MinimapFrameManagerInstance.ActiveSelectionKeyName))), delegate
        {
            Main.MinimapFrameManagerInstance.CycleSelection();
        }, Scale);
        AddItem(() => CachedText.Format("UI.SelectHealthStyle", Language.GetTextValue(CachedText.Concat("UI.HealthManaStyle_", Main.ResourceSetsManager.ActiveSetKeyName))), delegate
        {
            Main.ResourceSetsManager.CycleResourceSet();
        }, Scale);
        AddToggle(() => Main.DialoguePortraitPreference switch
        {
            Main.DialoguePortraitDrawOption.CloseUp => Language.GetTextValue("UI.PortraitsCloseUp"),
            Main.DialoguePortraitDrawOption.FullBodyRetro => Language.GetTextValue("UI.PortraitsFullBody"),
            Main.DialoguePortraitDrawOption.Disabled => Language.GetTextValue("UI.PortraitsDisabled"),
            _ => Language.GetTextValue("UI.PortraitsDetailed")
        }, Main.CycleNPCPortraitMode, Scale);
        AddItem(() => Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff"), BigProgressBarSystem.ToggleShowText, Scale);
        AddToggle(() => Language.GetTextValue(Main.FlashyEffectsInterface ? "UI.FlashyEffectsInterfaceOn" : "UI.FlashyEffectsInterfaceOff"), delegate
        {
            Main.FlashyEffectsInterface = !Main.FlashyEffectsInterface;
        }, Scale);
        AddItem(() => Lang.menu[5].Value, GoBack, Scale, 16f);
    }

    public override void GoBack()
    {
        Main.menuMode = 11;
        SoundEngine.PlaySound(11);
    }
}
