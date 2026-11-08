using Terraria;
using Terraria.Audio;
using Terraria.UI;

namespace Aether.UI.Menu;

/// <summary>Settings → Controls (menuMode 1127); key bindings themselves are vanilla's UIManageControls.</summary>
public class UIControlsSettings : UIMenuScreen
{
    private const float Scale = 0.78f;

    public UIControlsSettings()
    {
        ItemsTop = 250f;
        ItemSpacing = 52f;
    }

    protected override void BuildItems()
    {
        AddToggle(() => Lang.menu[Main.ReversedUpDownArmorSetBonuses ? 220 : 221].Value, delegate
        {
            Main.ReversedUpDownArmorSetBonuses = !Main.ReversedUpDownArmorSetBonuses;
        }, Scale);
        AddToggle(() => ItemSlot.Options.DisableQuickTrash ? Lang.menu[253].Value : Lang.menu[ItemSlot.Options.DisableLeftShiftTrashCan ? 224 : 223].Value, CycleQuickTrash, Scale);
        AddItem(() => Lang.menu[222].Value, delegate
        {
            SoundEngine.PlaySound(10);
            Main.menuMode = 888;
            Main.MenuUI.SetState(Main.ManageControlsMenu);
        }, Scale);
        AddItem(() => Lang.menu[5].Value, GoBack, Scale, 18f);
    }

    // Quick trash: shift-click trashes → shift-click doesn't trash → off → back on.
    private static void CycleQuickTrash()
    {
        if (ItemSlot.Options.DisableQuickTrash)
        {
            ItemSlot.Options.DisableQuickTrash = false;
            ItemSlot.Options.DisableLeftShiftTrashCan = true;
        }
        else if (ItemSlot.Options.DisableLeftShiftTrashCan)
        {
            ItemSlot.Options.DisableLeftShiftTrashCan = false;
        }
        else
        {
            ItemSlot.Options.DisableQuickTrash = true;
            ItemSlot.Options.DisableLeftShiftTrashCan = false;
        }
    }

    public override void GoBack()
    {
        Main.menuMode = 11;
        SoundEngine.PlaySound(11);
    }
}
