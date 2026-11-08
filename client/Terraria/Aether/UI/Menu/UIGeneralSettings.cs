using Terraria;
using Terraria.Audio;
using Terraria.Localization;

namespace Aether.UI.Menu;

/// <summary>Settings → General (menuMode 112).</summary>
public class UIGeneralSettings : UIMenuScreen
{
    public UIGeneralSettings()
    {
        ItemsTop = 250f;
        ItemSpacing = 52f;
    }

    protected override void BuildItems()
    {
        AddToggle(() => Lang.menu[Main.autoSave ? 67 : 68].Value, delegate
        {
            Main.autoSave = !Main.autoSave;
        }, 0.78f);
        AddToggle(() => Lang.menu[Main.autoPause ? 69 : 70].Value, delegate
        {
            Main.autoPause = !Main.autoPause;
        }, 0.78f);
        AddToggle(() => Lang.menu[Main.mapEnabled ? 112 : 113].Value, delegate
        {
            Main.mapEnabled = !Main.mapEnabled;
        }, 0.78f);
        AddToggle(() => Lang.menu[Main.HidePassword ? 212 : 211].Value, delegate
        {
            Main.HidePassword = !Main.HidePassword;
        }, 0.78f);
        AddToggle(() => Language.GetTextValue(Main.SettingPlayWhenUnfocused ? "UI.PlayWhenUnfocusedOn" : "UI.PlayWhenUnfocusedOff"), delegate
        {
            Main.SettingPlayWhenUnfocused = !Main.SettingPlayWhenUnfocused;
        }, 0.78f);
        AddItem(() => Lang.menu[5].Value, GoBack, 0.78f, 18f);
    }

    public override void GoBack()
    {
        Main.menuMode = 11;
        SoundEngine.PlaySound(11);
    }
}
