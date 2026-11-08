using Terraria;
using Terraria.Audio;
using Terraria.GameInput;

namespace Aether.UI.Menu;

/// <summary>Settings → Cursor (menuMode 1125).</summary>
public class UICursorSettings : UIMenuScreen
{
    private const float Scale = 0.73f;

    public UICursorSettings()
    {
        ItemsTop = 232f;
        ItemSpacing = 38f;
    }

    protected override void BuildItems()
    {
        AddItem(() => Lang.menu[64].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.selColor = Main.mouseColor;
            Main.mouseColorSlider.SetHSL(Main.mouseColor);
            Main.menuMode = 25;
        }, Scale);
        AddItem(() => Lang.menu[217].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.selColor = Main.MouseBorderColor;
            // Vanilla seeds the border sliders from the cursor colour here.
            Main.mouseBorderColorSlider.SetHSL(Main.mouseColor);
            Main.menuMode = 252;
        }, Scale);
        AddToggle(() => Lang.menu[Main.cSmartCursorModeIsToggleAndNotHold ? 121 : 122].Value, delegate
        {
            Main.cSmartCursorModeIsToggleAndNotHold = !Main.cSmartCursorModeIsToggleAndNotHold;
        }, Scale);
        AddToggle(() => Lang.menu[Player.SmartCursorSettings.SmartAxeAfterPickaxe ? 214 : 213].Value, delegate
        {
            Player.SmartCursorSettings.SmartAxeAfterPickaxe = !Player.SmartCursorSettings.SmartAxeAfterPickaxe;
        }, Scale);
        AddToggle(() => Lang.menu[Player.SmartCursorSettings.SmartBlocksEnabled ? 215 : 216].Value, delegate
        {
            Player.SmartCursorSettings.SmartBlocksEnabled = !Player.SmartCursorSettings.SmartBlocksEnabled;
        }, Scale);
        AddToggle(() => LockOnHelper.UseMode switch
        {
            LockOnHelper.LockOnMode.FocusTarget => Lang.menu[232].Value,
            LockOnHelper.LockOnMode.TargetClosest => Lang.menu[233].Value,
            LockOnHelper.LockOnMode.ThreeDS => Lang.menu[234].Value,
            _ => ""
        }, LockOnHelper.CycleUseModes, Scale);
        AddItem(() => Lang.menu[5].Value, GoBack, Scale, 18f);
    }

    public override void GoBack()
    {
        Main.menuMode = 11;
        SoundEngine.PlaySound(11);
    }
}
