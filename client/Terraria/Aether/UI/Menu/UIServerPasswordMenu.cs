using System;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;

namespace Aether.UI.Menu;

/// <summary>
/// A server password, typed in place (with gamepad hints on, in the virtual keyboard instead):
/// menuMode 31 when the server we're joining asks for one, 30 when hosting (Host &amp; Play).
/// </summary>
public class UIServerPasswordMenu : UIMenuScreen
{
    private readonly int _mode;

    private readonly int _prompt;

    private readonly Action _submit;

    private readonly UIVirtualKeyboard.KeyboardSubmitEvent _keyboardSubmit;

    private readonly Action _cancel;

    public UIServerPasswordMenu(bool hosting)
    {
        if (hosting)
        {
            _mode = 30;
            _prompt = 7;
            _submit = Main.HostAndPlay;
            _keyboardSubmit = Main.instance.OnSubmitServerPassword;
            _cancel = Main.ExitServerPasswordMenu;
        }
        else
        {
            _mode = 31;
            _prompt = 3;
            _submit = Main.OnSubmitServerPasswordFromRequest;
            _keyboardSubmit = Main.OnSubmitServerPasswordFromRequest;
            _cancel = Main.CanceledGivingServerPassword;
        }
    }

    protected override void BuildItems()
    {
        AddLabel(() => Lang.menu[_prompt].Value);
        AddLabel(() => WithCursor(Main.HidePassword ? new string('*', Netplay.ServerPassword.Length) : Netplay.ServerPassword), 1f, -20f).HideAutomationLabel = true;
        AddItem(() => Lang.menu[4].Value, _submit, 1f, 20f);
        AddItem(() => Lang.menu[5].Value, GoBack);
    }

    public override void UpdateState()
    {
        // In a preview, show the keyboard version of the screen rather than leaving it.
        if (PlayerInput.SettingsForUI.ShowGamepadHints && !MenuPreview.IsPreviewing)
        {
            UIVirtualKeyboard uIVirtualKeyboard = new UIVirtualKeyboard(Lang.menu[_prompt].Value, Netplay.ServerPassword, _keyboardSubmit, _cancel, 0, allowEmpty: true);
            uIVirtualKeyboard.HideContents = Main.HidePassword;
            Main.MenuUI.SetState(uIVirtualKeyboard);
            Main.menuMode = 888;
            return;
        }
        Netplay.ServerPassword = ReadTextInput(Netplay.ServerPassword);
        BlinkCursor();
        // Hosting also submits by itself when autoPass is set.
        if (Main.menuMode == _mode && (Main.inputTextEnter || (_mode == 30 && Main.autoPass)))
        {
            Act("Enter", _submit);
        }
    }

    public override void GoBack()
    {
        _cancel();
    }
}
