using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.States;
using Terraria.Graphics.Effects;
using Terraria.Localization;
using Terraria.Social;

namespace Aether.UI.Menu;

/// <summary>The title screen's main menu (menuMode 0).</summary>
public class UIMainMenu : UIMenuScreen
{
    public UIMainMenu()
    {
        ItemsTop = 220f;
        ItemSpacing = 45f;
    }

    protected override void BuildItems()
    {
        AddItem(Lang.menu[12].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.ClearPendingPlayerSelectCallbacks();
            Main.menuMode = 1;
        });
        AddItem(Lang.menu[13].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.menuMode = 12;
        });
        AddItem(Lang.menu[131].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.menuMode = 888;
            Main.MenuUI.SetState(Main.AchievementsMenu);
        });
        if (SocialAPI.Workshop != null)
        {
            AddItem(Language.GetText("UI.Workshop").Value, delegate
            {
                SoundEngine.PlaySound(10);
                Main.menuMode = 888;
                UIWorkshopHub uIWorkshopHub = new UIWorkshopHub(null);
                uIWorkshopHub.EnterHub();
                Main.MenuUI.SetState(uIWorkshopHub);
            });
        }
        else
        {
            AddItem(Language.GetText("UI.ResourcePacks").Value, delegate
            {
                SoundEngine.PlaySound(10);
                Main.OpenResourcePacksMenu(null);
            });
        }
        AddItem(Language.GetTextValue("AetherMods.Title"), UIModList.Open);
        AddItem(Lang.menu[14].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.menuMode = 11;
        });
        AddItem(Language.GetText("UI.Credits").Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.menuMode = 3000;
            SkyManager.Instance.Activate("CreditsRoll", default);
        });
        AddItem(Lang.menu[15].Value, delegate
        {
            Main.GameAskedToQuit = true;
        });
    }

    public override void UpdateState()
    {
        // DrawMenu resets this state every frame the main menu is shown.
        Main._blockFancyUIWhileLoading = false;
        Main._pendingCharacterSelect = null;
        DD2Event.Ongoing = false;
        Main.eclipse = false;
        Main.pumpkinMoon = false;
        Main.snowMoon = false;
        Main.ServerSideCharacter = false;
        Main.menuMultiplayer = false;
        Main.menuServer = false;
        Main.netMode = 0;
        Main.ClearVisualPostProcessEffects();
    }
}
