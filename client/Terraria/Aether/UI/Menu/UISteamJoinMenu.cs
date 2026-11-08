using Terraria;
using Terraria.Audio;
using Terraria.Social;

namespace Aether.UI.Menu;

/// <summary>Joining a friend through Steam (menuMode 882): status and Cancel.</summary>
public class UISteamJoinMenu : UIMenuScreen
{
    public UISteamJoinMenu()
    {
        ItemsTop = 300f;
    }

    protected override void BuildItems()
    {
        AddLabel(() => Main.statusText);
        AddItem(() => Lang.menu[6].Value, GoBack);
    }

    public override void GoBack()
    {
        SoundEngine.PlaySound(11);
        Main.menuMode = 0;
        Main.netMode = 0;
        if (SocialAPI.Network != null)
        {
            SocialAPI.Network.CancelJoin();
        }
    }
}
