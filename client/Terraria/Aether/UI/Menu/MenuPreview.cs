using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;

namespace Aether.UI.Menu;

/// <summary>
/// Shows migrated title-menu screens one at a time, for checking them in isolation: the gallery
/// (<see cref="GalleryMode" />) lists them, and previewing one opens it with sample state, makes its
/// items inert (a click is reported on the overlay instead of acted on), and returns to the gallery on
/// the menu's back command (Esc, or the gamepad's back button). Tab switches the preview between the
/// retained screen and the legacy DrawMenu branch for the same mode, whose clicks and back are live.
/// Start with the launch argument <c>-aetherscreens</c> (gallery) or <c>-aetherscreens MODE</c>.
/// </summary>
public static class MenuPreview
{
    public const int GalleryMode = 1700000;

    private const int None = int.MinValue;

    private static int _previewedMode = None;

    private static bool _launchChecked;

    private static bool _tabWasDown;

    private static string _lastClick;

    public static bool ShowLegacy;

    public static bool IsPreviewing
    {
        get
        {
            if (_previewedMode != None)
            {
                return Main.menuMode == _previewedMode;
            }
            return false;
        }
    }

    public static void Open(int mode)
    {
        Seed(mode);
        _previewedMode = mode;
        _lastClick = null;
        Main.menuMode = mode;
    }

    public static void ReturnToGallery()
    {
        SoundEngine.PlaySound(11);
        _previewedMode = None;
        ShowLegacy = false;
        Main.menuMode = GalleryMode;
    }

    /// <summary>Runs action, or while previewing only reports it (what is the item or key that would act).</summary>
    public static void Act(string what, System.Action action)
    {
        // Only retained screens call this, and they aren't updated while the legacy view is shown.
        if (IsPreviewing)
        {
            SoundEngine.PlaySound(12);
            _lastClick = what;
        }
        else
        {
            action();
        }
    }

    /// <summary>Called by MenuScreens at the start of every title-menu frame.</summary>
    public static void Update()
    {
        if (!_launchChecked && Program.LoadedEverything)
        {
            _launchChecked = true;
            if (Program.LaunchParameters.TryGetValue("-aetherscreens", out var value))
            {
                if (int.TryParse(value, out var mode) && MenuScreens.Owns(mode))
                {
                    Open(mode);
                }
                else
                {
                    if (value.Length > 0)
                    {
                        Aether.Modding.AetherLog.For("Aether.Screens").Warning("-aetherscreens: {Mode} isn't a migrated menuMode; opening the gallery.", value);
                    }
                    Main.menuMode = GalleryMode;
                }
            }
        }
        if (_previewedMode != None && Main.menuMode != _previewedMode)
        {
            // The screen moved on by itself (e.g. Enter in a text field, or a vanilla screen it opened).
            _previewedMode = None;
            ShowLegacy = false;
        }
        // Esc needs no handling here: it's the menu's back command, which MenuScreens turns into
        // ReturnToGallery while previewing (and the legacy screen handles itself).
        bool tabDown = Main.keyState.IsKeyDown(Keys.Tab);
        if (IsPreviewing && tabDown && !_tabWasDown)
        {
            SoundEngine.PlaySound(12);
            ShowLegacy = !ShowLegacy;
            _lastClick = null;
        }
        _tabWasDown = tabDown;
    }

    public static void DrawOverlay(SpriteBatch spriteBatch)
    {
        if (!IsPreviewing)
        {
            return;
        }
        string text = (ShowLegacy ? ("Preview: menuMode " + _previewedMode + ", legacy DrawMenu (clicks and Esc are live)\n[Tab] retained version") : ("Preview: menuMode " + _previewedMode + ", " + MenuScreens.Describe(MenuScreens.ShownMode(_previewedMode)) + "\n[Esc] gallery   [Tab] legacy version"));
        if (_lastClick != null)
        {
            text = text + "\nClicked: " + _lastClick;
        }
        Utils.DrawBorderString(spriteBatch, text, new Vector2(10f, 10f), Main.OurFavoriteColor, 0.9f);
    }

    /// <summary>Sample state for screens that show something set up by the flow that leads to them.</summary>
    private static void Seed(int mode)
    {
        switch (mode)
        {
        case 10:
        case 100:
            Main.statusText = "Preview: loading world data";
            break;
        case 14:
            Main.statusText = "Preview: connecting to 127.0.0.1:7777\nsecond line";
            break;
        case 15:
            Main.statusText = "Preview: the connection failed with an error message that is long enough to need the small print";
            break;
        case 882:
            Main.statusText = "Preview: joining a friend";
            break;
        case 1000000:
            Main.statusText = "Preview: the world was rejected";
            break;
        case 1000001:
            Main.instance.RejectionMenuInfo = new RejectionMenuInfo
            {
                TextToShow = "Preview: custom rejection text"
            };
            break;
        case 25:
            Main.selColor = Main.mouseColor;
            break;
        case 252:
            Main.selColor = Main.MouseBorderColor;
            break;
        }
    }
}
