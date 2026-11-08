using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;

namespace Aether.Testing;

/// <summary>
/// Input the control server (<see cref="ControlServer" />) feeds the game in place of the real devices,
/// so tests can drive it without the window having focus and without moving the real cursor or sending
/// system input. The game reads its devices through the <c>Mouse</c>/<c>Keyboard</c>/<c>GamePad</c>
/// methods here (patched in where it calls <c>Mouse.GetState()</c> and friends):
/// <list type="bullet">
/// <item>the mouse is entirely synthetic while control is enabled (the real one is ignored);</item>
/// <item>synthetic keys are added to the real keyboard's;</item>
/// <item>a synthetic gamepad, once connected, replaces the real first gamepad.</item>
/// </list>
/// Positions are window pixels (what a screenshot shows). Only the game's main thread touches this.
/// </summary>
public static class SyntheticInput
{
    public static bool Enabled;

    public static Vector2 MousePosition;

    public static bool LeftButton;

    public static bool RightButton;

    public static bool MiddleButton;

    public static int ScrollWheel;

    public static readonly HashSet<Keys> KeysDown = new HashSet<Keys>();

    public static bool GamePadConnected;

    public static Buttons GamePadButtons;

    public static Vector2 LeftStick;

    public static Vector2 RightStick;

    public static float LeftTrigger;

    public static float RightTrigger;

    private static ButtonState State(bool down)
    {
        if (!down)
        {
            return ButtonState.Released;
        }
        return ButtonState.Pressed;
    }

    public static MouseState Mouse(MouseState real)
    {
        if (!Enabled)
        {
            return real;
        }
        return new MouseState((int)Math.Round(MousePosition.X), (int)Math.Round(MousePosition.Y), ScrollWheel, State(LeftButton), State(MiddleButton), State(RightButton), ButtonState.Released, ButtonState.Released);
    }

    public static KeyboardState Keyboard(KeyboardState real)
    {
        if (!Enabled || KeysDown.Count == 0)
        {
            return real;
        }
        HashSet<Keys> keys = new HashSet<Keys>(real.GetPressedKeys());
        keys.UnionWith(KeysDown);
        Keys[] array = new Keys[keys.Count];
        keys.CopyTo(array);
        return new KeyboardState(array);
    }

    public static GamePadState GamePad(int index, GamePadState real)
    {
        if (!Enabled || !GamePadConnected)
        {
            return real;
        }
        if (index != 0)
        {
            return default(GamePadState);
        }
        Buttons buttons = GamePadButtons;
        GamePadDPad dPad = new GamePadDPad(State((buttons & Buttons.DPadUp) != 0), State((buttons & Buttons.DPadDown) != 0), State((buttons & Buttons.DPadLeft) != 0), State((buttons & Buttons.DPadRight) != 0));
        return new GamePadState(new GamePadThumbSticks(LeftStick, RightStick), new GamePadTriggers(LeftTrigger, RightTrigger), new GamePadButtons(buttons), dPad);
    }

    /// <summary>Types a character into the game's text input, as the IME key listener does.</summary>
    public static void TypeCharacter(char character)
    {
        if (Main.keyCount < Main.keyInt.Length)
        {
            Main.keyInt[Main.keyCount] = character;
            Main.keyString[Main.keyCount] = character.ToString();
            Main.keyCount++;
        }
    }

    public static void ReleaseAll()
    {
        LeftButton = (RightButton = (MiddleButton = false));
        KeysDown.Clear();
        GamePadButtons = (Buttons)0;
        LeftStick = (RightStick = Vector2.Zero);
        LeftTrigger = (RightTrigger = 0f);
    }
}
