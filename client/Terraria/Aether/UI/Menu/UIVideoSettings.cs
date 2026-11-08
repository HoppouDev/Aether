using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Localization;

namespace Aether.UI.Menu;

/// <summary>Settings → Video (menuMode 1111).</summary>
public class UIVideoSettings : UIMenuScreen
{
    private const float Scale = 0.6f;

    public UIVideoSettings()
    {
        ItemsTop = 186f;
        ItemSpacing = 30f;
    }

    protected override void BuildItems()
    {
        AddLink(() => Lang.menu[51].Value, 111, 10, Scale);
        AddLink(() => Lang.menu[52].Value, 28, 11, Scale);
        AddToggle(() => Language.GetTextValue(CachedText.Concat("UI.LightMode_", CachedText.Name(Lighting.Mode))), Lighting.NextLightMode, Scale);
        AddToggle(() => Lang.menu[Main.qaStyle switch
        {
            0 => 59,
            1 => 60,
            2 => 61,
            _ => 62
        }].Value, delegate
        {
            if (++Main.qaStyle > 3)
            {
                Main.qaStyle = 0;
            }
        }, Scale);
        AddToggle(() => Lang.menu[Main.BackgroundEnabled ? 100 : 101].Value, delegate
        {
            Main.BackgroundEnabled = !Main.BackgroundEnabled;
        }, Scale);
        AddToggle(() => Lang.menu[ChildSafety.Disabled ? 132 : 133].Value, delegate
        {
            ChildSafety.Disabled = !ChildSafety.Disabled;
        }, Scale);
        AddToggle(() => Lang.menu[Main.SettingsEnabled_MinersWobble ? 250 : 251].Value, delegate
        {
            Main.SettingsEnabled_MinersWobble = !Main.SettingsEnabled_MinersWobble;
        }, Scale);
        AddToggle(() => Language.GetTextValue(Main.SettingsEnabled_TilesSwayInWind ? "UI.TilesSwayInWindOn" : "UI.TilesSwayInWindOff"), delegate
        {
            Main.SettingsEnabled_TilesSwayInWind = !Main.SettingsEnabled_TilesSwayInWind;
        }, Scale);
        AddLink(() => Language.GetTextValue("UI.Effects"), 2008, 11, Scale);
        AddLink(() => Language.GetTextValue("AetherGraphics.Title"), UIAetherGraphicsSettings.Mode, 11, Scale);
        AddItem(() => Lang.menu[5].Value, GoBack, 1f, 8f);
    }

    public override void UpdateState()
    {
        Main.bgScroll = (int)Math.Round((1f - Main.caveParallax) * 500f);
    }

    public override void GoBack()
    {
        SoundEngine.PlaySound(11);
        Main.SaveSettings();
        Main.menuMode = 11;
    }
}
