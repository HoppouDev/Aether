using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Localization;

namespace Aether.UI.Menu;

/// <summary>
/// Connection status with Cancel (menuMode 14, and whatever the mode while <c>netMode</c> is 1):
/// connecting, joining, receiving the world. The status text shrinks as it gains lines.
/// </summary>
public class UIConnectingMenu : UIMenuScreen
{
    private UIMenuItem _status;

    private UIMenuItem _cancel;

    private int _extraLines = -1;

    public UIConnectingMenu()
    {
        ItemsTop = 300f;
    }

    protected override void BuildItems()
    {
        _status = AddLabel(() => Main.statusText);
        _cancel = AddItem(() => Lang.menu[6].Value, GoBack);
        _extraLines = -1;
    }

    public override void UpdateState()
    {
        Main.gameTips.Update();
        int extraLines = Main.statusText.Split('\n').Length - 1;
        if (extraLines != _extraLines)
        {
            _extraLines = extraLines;
            _status.TextScale = 1f - extraLines * 0.04f;
            _status.Top.Set(ItemsTop - extraLines * 18, 0f);
            _cancel.Top.Set(ItemsTop + ItemSpacing + extraLines * 28, 0f);
            _status.Recalculate();
            _cancel.Recalculate();
        }
        if (Netplay.IsHostAndPlay && Main.tServer != null && Main.tServer.HasExited)
        {
            Main.statusText = Language.GetTextValue("Error.ServerStartFailed");
        }
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Main.gameTips.Draw();
    }

    public override void GoBack()
    {
        Netplay.InvalidateAllOngoingIPSetAttempts();
        Netplay.Disconnect = true;
        Netplay.Connection.Socket.Close();
        SoundEngine.PlaySound(11);
        Main.menuMode = 0;
        Main.netMode = 0;
        if (Main.tServer != null)
        {
            try
            {
                Main.tServer.Kill();
                Main.tServer = null;
            }
            catch
            {
            }
        }
    }
}
