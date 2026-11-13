using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;

namespace Aether.Testing;

/// <summary>
/// Input the control server (<see cref="ControlServer" />) feeds the game in place of the real devices,
/// so tests can drive it without the window having focus and without moving the real cursor or sending
/// system input. <see cref="Install" /> makes the methods here FNA's state filters (Aether's FNA patch:
/// <c>Mouse</c>/<c>Keyboard</c>/<c>GamePad.StateFilterEXT</c>), so everything that reads FNA's input
/// (the game, Aether, the profiler overlay) sees it:
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

    private static readonly HashSet<Keys> HeldKeys = new HashSet<Keys>();

    // The last merged keyboard state, rebuilt only when the real keys or the held ones change:
    // the game reads the keyboard several times a frame.
    private static KeyboardState _realKeys;

    private static KeyboardState _mergedKeys;

    private static bool _keysChanged;

    public static IReadOnlyCollection<Keys> KeysDown => HeldKeys;

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

    /// <summary>Makes this FNA's input: what Mouse, Keyboard and GamePad.GetState return pass through it.</summary>
    public static void Install()
    {
        Microsoft.Xna.Framework.Input.Mouse.StateFilterEXT = Mouse;
        Microsoft.Xna.Framework.Input.Keyboard.StateFilterEXT = Keyboard;
        Microsoft.Xna.Framework.Input.GamePad.StateFilterEXT = (player, real) => GamePad((int)player, real);
    }

    public static void HoldKeys(IEnumerable<Keys> keys)
    {
        HeldKeys.UnionWith(keys);
        _keysChanged = true;
    }

    public static void ReleaseKeys(IEnumerable<Keys> keys)
    {
        HeldKeys.ExceptWith(keys);
        _keysChanged = true;
    }

    private static MouseState Mouse(MouseState real)
    {
        if (!Enabled)
        {
            return real;
        }
        return new MouseState((int)Math.Round(MousePosition.X), (int)Math.Round(MousePosition.Y), ScrollWheel, State(LeftButton), State(MiddleButton), State(RightButton), ButtonState.Released, ButtonState.Released);
    }

    private static KeyboardState Keyboard(KeyboardState real)
    {
        if (!Enabled || HeldKeys.Count == 0)
        {
            return real;
        }
        if (_keysChanged || real != _realKeys)
        {
            HashSet<Keys> keys = new HashSet<Keys>(real.GetPressedKeys());
            keys.UnionWith(HeldKeys);
            Keys[] array = new Keys[keys.Count];
            keys.CopyTo(array);
            _realKeys = real;
            _mergedKeys = new KeyboardState(array);
            _keysChanged = false;
        }
        return _mergedKeys;
    }

    private static GamePadState GamePad(int index, GamePadState real)
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
        HeldKeys.Clear();
        _keysChanged = true;
        GamePadButtons = (Buttons)0;
        LeftStick = (RightStick = Vector2.Zero);
        LeftTrigger = (RightTrigger = 0f);
    }
}
