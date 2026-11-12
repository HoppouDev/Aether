using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.UI;

namespace Aether.UI.Menu;

/// <summary>
/// A title-menu screen laid out like DrawMenu's: a centred column of <see cref="UIMenuItem" />s, item
/// <c>i</c> at <c>ItemsTop + ItemSpacing * i</c> (plus its own offset). Items are rebuilt every time
/// the screen opens, so they can depend on the game's state.
/// </summary>
public abstract class UIMenuScreen : UIState
{
    protected float ItemsTop = 250f;

    protected float ItemSpacing = 80f;

    private int _itemCount;

    protected abstract void BuildItems();

    /// <summary>
    /// The legacy branch's per-frame work (state it resets, typing, Enter, redirects), run by MenuScreens
    /// each frame before input is handled, as DrawMenu does it before acting on a click. Code here must
    /// not go in Update: UserInterface handles clicks before updating the state, so it would undo them.
    /// </summary>
    public virtual void UpdateState()
    {
    }

    /// <summary>The gamepad's back button.</summary>
    public virtual void GoBack()
    {
    }

    public override void OnActivate()
    {
        Rebuild();
    }

    protected void Rebuild()
    {
        RemoveAllChildren();
        _itemCount = 0;
        BuildItems();
        Recalculate();
    }

    protected UIMenuItem AddItem(Func<string> text, Action onClick, float scale = 1f, float offset = 0f)
    {
        UIMenuItem item = new UIMenuItem(text, onClick)
        {
            TextScale = scale,
            HAlign = 0.5f
        };
        item.Top.Set(ItemsTop + ItemSpacing * _itemCount + offset, 0f);
        _itemCount++;
        Append(item);
        return item;
    }

    protected UIMenuItem AddItem(string text, Action onClick, float scale = 1f, float offset = 0f)
    {
        return AddItem(() => text, onClick, scale, offset);
    }

    /// <summary>An item that changes a setting in place (DrawMenu plays the tick sound for these).</summary>
    protected UIMenuItem AddToggle(Func<string> text, Action change, float scale = 1f, float offset = 0f)
    {
        return AddItem(text, delegate
        {
            SoundEngine.PlaySound(12);
            change();
        }, scale, offset);
    }

    /// <summary>An item that opens another menuMode, with DrawMenu's sound for it.</summary>
    protected UIMenuItem AddLink(Func<string> text, int menuMode, int sound = 10, float scale = 1f, float offset = 0f)
    {
        return AddItem(text, delegate
        {
            SoundEngine.PlaySound(sound);
            Main.menuMode = menuMode;
        }, scale, offset);
    }

    protected UIMenuItem AddLabel(Func<string> text, float scale = 1f, float offset = 0f)
    {
        UIMenuItem uIMenuItem = AddItem(text, null, scale, offset);
        uIMenuItem.IsLabel = true;
        return uIMenuItem;
    }

    /// <summary>
    /// Does something the screen starts by itself (Enter in a text field, a redirect to the virtual
    /// keyboard) rather than through an item click; in a preview it's only reported (see MenuPreview).
    /// </summary>
    protected static void Act(string what, Action action)
    {
        MenuPreview.Act(what, action);
    }

    /// <summary>Applies this frame's typing to text, like DrawMenu's text fields.</summary>
    protected static string ReadTextInput(string text)
    {
        PlayerInput.WritingText = true;
        Main.instance.HandleIME();
        string inputText = Main.GetInputText(text);
        if (inputText != text)
        {
            SoundEngine.PlaySound(12);
        }
        return inputText;
    }

    /// <summary>A text field's value with DrawMenu's blinking cursor.</summary>
    protected static string WithCursor(string text)
    {
        return text + ((Main.instance.textBlinkerState == 1) ? "|" : " ");
    }

    protected static void BlinkCursor()
    {
        Main.instance.textBlinkerCount += Aether.Graphics.VisualAnimationTime.WholeTicks;
        if (Main.instance.textBlinkerCount >= 20)
        {
            Main.instance.textBlinkerState = 1 - Main.instance.textBlinkerState;
            Main.instance.textBlinkerCount = 0;
        }
    }
}
