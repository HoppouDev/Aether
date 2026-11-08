using Terraria;
using Terraria.Audio;

namespace Aether.UI.Menu;

/// <summary>The title screen's settings menu (menuMode 11); its pages are still legacy screens.</summary>
public class UISettingsMenu : UIMenuScreen
{
    public UISettingsMenu()
    {
        ItemsTop = 210f;
        ItemSpacing = 37f;
    }

    protected override void BuildItems()
    {
        AddPage(114, 112);
        AddPage(210, 1112);
        AddPage(63, 1111);
        AddPage(65, 26, 11);
        AddPage(218, 1125);
        AddPage(219, 1127);
        AddPage(103, 1213);
        AddItem(Lang.menu[5].Value, GoBack, 0.75f, 8f);
    }

    private void AddPage(int text, int menuMode, int sound = 10)
    {
        AddItem(Lang.menu[text].Value, delegate
        {
            SoundEngine.PlaySound(sound);
            Main.menuMode = menuMode;
        }, 0.75f);
    }

    public override void GoBack()
    {
        SoundEngine.PlaySound(11);
        Main.menuMode = 0;
        Main.SaveSettings();
    }
}
