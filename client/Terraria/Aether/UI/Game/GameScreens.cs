using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace Aether.UI.Game;

/// <summary>
/// Hosts Aether's retained-mode replacements for the in-game (immediate-mode) UI. Each legacy entry
/// point hands its drawing to <see cref="Draw" /> with the screen that replaces it, so draw order stays
/// as it was; every screen has its own UserInterface (several are drawn each frame). Like the title
/// menu's MenuScreens, they hand UserInterface.ActiveInstance back afterwards, since vanilla code uses
/// it to mean its own UIs.
/// <para>
/// A screen's layout is recalculated when it opens and when the UI's size changes (resolution, UI
/// scale), not every frame: a screen that moves or rebuilds its elements recalculates them itself
/// (usually at the end of <see cref="UIGameScreen.UpdateState" />). Elements that are placed while
/// drawing and hit-test themselves don't need it.
/// </para>
/// </summary>
public static class GameScreens
{
    private class Host
    {
        public readonly UserInterface UI = new UserInterface();

        public Vector2 Size;
    }

    private static readonly Dictionary<UIGameScreen, Host> Hosts = new Dictionary<UIGameScreen, Host>();

    /// <summary>Updates and draws screen; call from the legacy draw it replaces, in UI zoom.</summary>
    public static void Draw(UIGameScreen screen, SpriteBatch spriteBatch)
    {
        long allocated = Aether.Testing.AllocationProfiler.Active ? Aether.Testing.AllocationProfiler.Now() : 0;
        UserInterface previous = UserInterface.ActiveInstance;
        if (!Hosts.TryGetValue(screen, out var host))
        {
            host = new Host();
            Hosts[screen] = host;
        }
        UserInterface ui = host.UI;
        // Not Use(): it recalculates on every switch of instance, which here is twice a frame per
        // screen (and the second time, for whatever UI was active before).
        UserInterface.ActiveInstance = ui;
        CalculatedStyle dimensions = ui.GetDimensions();
        Vector2 size = new Vector2(dimensions.Width, dimensions.Height);
        if (ui.CurrentState != screen)
        {
            // SetState lays the screen out.
            ui.SetState(screen);
            host.Size = size;
        }
        else if (size != host.Size)
        {
            host.Size = size;
            ui.Recalculate();
        }
        // Some pieces (the notification popups) are also drawn on the title menu, before the
        // in-game interface has ever set its game time.
        GameTime gameTime = Main._drawInterfaceGameTime ?? Main.gameTimeCache;
        screen.UpdateState();
        int wheel = PlayerInput.ScrollWheelDeltaForUI;
        ui.Update(gameTime);
        // The screen covers the whole UI but is only a container: with the mouse over nothing else on
        // it, the wheel goes on to the next UI (the title menu's achievements list, a fancy UI), as it
        // did past the legacy draw the screen replaces.
        if (wheel != 0 && PlayerInput.ScrollWheelDeltaForUI == 0 && screen.GetElementAt(ui.MousePosition) == screen)
        {
            PlayerInput.ScrollWheelDeltaForUI = wheel;
        }
        ui.Draw(spriteBatch, gameTime);
        UserInterface.ActiveInstance = previous;
        if (Aether.Testing.AllocationProfiler.Active)
        {
            Aether.Testing.AllocationProfiler.Add(screen, Aether.Testing.AllocationProfiler.Now() - allocated);
        }
    }

    /// <summary>Drops screen, so its next Draw opens it afresh (input reset, rebuilt).</summary>
    public static void Close(UIGameScreen screen)
    {
        if (Hosts.TryGetValue(screen, out var host))
        {
            host.UI.SetState(null);
        }
    }
}
