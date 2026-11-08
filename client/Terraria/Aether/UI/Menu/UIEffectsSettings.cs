using Terraria;
using Terraria.Audio;
using Terraria.Localization;

namespace Aether.UI.Menu;

/// <summary>Settings → Video → Effects (menuMode 2008).</summary>
public class UIEffectsSettings : UIMenuScreen
{
    private const float Scale = 0.6f;

    public UIEffectsSettings()
    {
        ItemsTop = 210f;
        ItemSpacing = 28f;
    }

    private static string OnOff(string key, bool enabled)
    {
        return CachedText.Format(key, Language.GetTextValue(enabled ? "GameUI.Enabled" : "GameUI.Disabled"));
    }

    protected override void BuildItems()
    {
        AddLabel(() => Language.GetTextValue("UI.Effects"), 0.7f, -8f);
        AddToggle(() => OnOff("GameUI.StormEffects", Main.UseStormEffects), delegate
        {
            Main.UseStormEffects = !Main.UseStormEffects;
        }, Scale);
        AddToggle(() => OnOff("GameUI.ForegroundSunlightEffects", Main.ForegroundSunlightEffects), delegate
        {
            Main.ForegroundSunlightEffects = !Main.ForegroundSunlightEffects;
        }, Scale);
        AddToggle(() => OnOff("GameUI.HeatDistortion", Main.UseHeatDistortion), delegate
        {
            Main.UseHeatDistortion = !Main.UseHeatDistortion;
        }, Scale);
        AddToggle(() => CachedText.Format("GameUI.WaveQuality", Main.WaveQuality switch
        {
            1 => Language.GetTextValue("GameUI.QualityLow"),
            2 => Language.GetTextValue("GameUI.QualityMedium"),
            3 => Language.GetTextValue("GameUI.QualityHigh"),
            _ => Language.GetTextValue("GameUI.QualityOff")
        }), delegate
        {
            Main.WaveQuality = (Main.WaveQuality + 1) % 4;
        }, Scale);
        AddToggle(() => OnOff("GameUI.ScreenShake", Main.UseScreenShake), delegate
        {
            Main.UseScreenShake = !Main.UseScreenShake;
        }, Scale);
        AddToggle(() => Language.GetTextValue(Main.FlashyEffectsWorld ? "UI.FlashyEffectsWorldOn" : "UI.FlashyEffectsWorldOff"), delegate
        {
            Main.FlashyEffectsWorld = !Main.FlashyEffectsWorld;
        }, Scale);
        AddItem(() => Lang.menu[5].Value, GoBack, Scale, 24f);
    }

    public override void GoBack()
    {
        SoundEngine.PlaySound(11);
        Main.menuMode = 1111;
    }
}
