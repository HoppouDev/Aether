using Terraria;
using Terraria.Audio;
using Terraria.Localization;

namespace Aether.UI.Menu;

/// <summary>
/// Language selection: the first-run screen (menuMode 1212, picking one continues to the main menu)
/// or Settings → Language (1213, which stays open and has a back button).
/// </summary>
public class UILanguageMenu : UIMenuScreen
{
    // In LanguageManager's culture ID order (1 = English … 12 = Traditional Chinese).
    private static readonly string[] Languages = new string[12] { "English", "German", "Italian", "French", "Spanish", "Russian", "Chinese", "Portuguese", "Polish", "Japanese", "Korean", "ChineseTraditional" };

    private readonly bool _firstRun;

    public UILanguageMenu(bool firstRun)
    {
        _firstRun = firstRun;
        ItemsTop = 200f;
        ItemSpacing = (firstRun ? 30f : 26f);
    }

    protected override void BuildItems()
    {
        AddLabel(() => Lang.menu[102].Value, _firstRun ? 1f : 0.85f, -20f);
        for (int i = 0; i < Languages.Length; i++)
        {
            int culture = i + 1;
            string key = "Language." + Languages[i];
            AddItem(() => Language.GetTextValue(key), delegate
            {
                Select(culture);
            }, 0.65f);
        }
        if (!_firstRun)
        {
            AddItem(() => Lang.menu[5].Value, GoBack, 0.8f, 9f);
        }
    }

    private void Select(int culture)
    {
        Main.changeTheTitle = true;
        LanguageManager.Instance.SetLanguage(culture);
        if (_firstRun)
        {
            Main.menuMode = 0;
            SoundEngine.PlaySound(10);
        }
        else
        {
            SoundEngine.PlaySound(12);
        }
        Main.SaveSettings();
    }

    public override void GoBack()
    {
        if (!_firstRun)
        {
            Main.menuMode = 11;
            SoundEngine.PlaySound(11);
        }
    }
}
