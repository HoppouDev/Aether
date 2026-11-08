using Terraria;
using Terraria.Audio;

namespace Aether.UI.Menu;

/// <summary>Settings → Video → Resolution (menuMode 111). Changes apply on Apply (or the fullscreen toggle).</summary>
public class UIResolutionSettings : UIMenuScreen
{
    private const float Scale = 0.85f;

    public UIResolutionSettings()
    {
        ItemsTop = 210f;
        ItemSpacing = 55f;
    }

    protected override void BuildItems()
    {
        AddToggle(() => CachedText.Concat(Lang.menu[73].Value, ": ", CachedText.Number(Main.PendingResolutionWidth), "x", CachedText.Number(Main.PendingResolutionHeight)), NextResolution, Scale);
        if (Main.IsBorderlessDisplayAvailable())
        {
            AddToggle(() => Lang.menu[Main.PendingBorderlessState ? 245 : 246].Value, delegate
            {
                Main.PendingBorderlessState = !Main.PendingBorderlessState;
            }, Scale);
        }
        // Resizing the window mid-draw isn't safe; DrawMenu defers it to the end of the frame, this
        // to the next update.
        AddItem(() => Lang.menu[Main.graphics.IsFullScreen ? 49 : 50].Value, delegate
        {
            Main.QueueMainThreadAction(Main.ToggleFullScreen);
        }, Scale);
        AddItem(() => Lang.menu[134].Value, Apply, Scale, 100f);
        AddItem(() => Lang.menu[5].Value, GoBack, Scale, 100f);
    }

    private static void NextResolution()
    {
        int current = 0;
        for (int i = 0; i < Main.numDisplayModes; i++)
        {
            if (Main.displayWidth[i] == Main.PendingResolutionWidth && Main.displayHeight[i] == Main.PendingResolutionHeight)
            {
                current = i;
                break;
            }
        }
        int next = (current + 1) % Main.numDisplayModes;
        Main.PendingResolutionWidth = Main.displayWidth[next];
        Main.PendingResolutionHeight = Main.displayHeight[next];
    }

    private static void Apply()
    {
        if (Main.graphics.IsFullScreen || Main.PendingBorderlessState != Main.screenBorderless || Main.PendingResolutionWidth != Main.screenWidth || Main.PendingResolutionHeight != Main.screenHeight)
        {
            Main.QueueMainThreadAction(delegate
            {
                Main.screenBorderless = Main.PendingBorderlessState;
                Main.screenBorderlessPendingResizes = (Main.screenBorderless ? 6 : 0);
                Main.SetResolution(Main.PendingResolutionWidth, Main.PendingResolutionHeight);
            });
        }
        SoundEngine.PlaySound(11);
        Main.menuMode = 1111;
    }

    public override void GoBack()
    {
        Main.PendingResolutionWidth = Main.graphics.PreferredBackBufferWidth;
        Main.PendingResolutionHeight = Main.graphics.PreferredBackBufferHeight;
        Main.PendingBorderlessState = Main.screenBorderless;
        Main.menuMode = 1111;
        SoundEngine.PlaySound(11);
    }
}
