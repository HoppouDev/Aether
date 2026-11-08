using Terraria;
using Terraria.Audio;

namespace Aether.UI.Menu;

/// <summary>Confirms deleting the character picked in the character list (menuMode 5).</summary>
public class UIDeletePlayerMenu : UIMenuScreen
{
    protected override void BuildItems()
    {
        AddLabel(() => CachedText.Concat(Lang.menu[46].Value, " ", PlayerName(), "?"));
        AddItem(() => Lang.menu[104].Value, delegate
        {
            Main.ErasePlayer(Main.selectedPlayer);
            SoundEngine.PlaySound(10);
            Main.menuMode = 1;
        });
        AddItem(() => Lang.menu[105].Value, GoBack);
    }

    // Always set by the character list; the fallback is for previews.
    private static string PlayerName()
    {
        if (Main.selectedPlayer >= 0 && Main.selectedPlayer < Main.PlayerList.Count)
        {
            return Main.PlayerList[Main.selectedPlayer].Player.name;
        }
        return "Player";
    }

    public override void GoBack()
    {
        SoundEngine.PlaySound(11);
        Main.menuMode = 1;
    }
}
