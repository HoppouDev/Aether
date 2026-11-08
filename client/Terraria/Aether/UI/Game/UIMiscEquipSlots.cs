using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;

namespace Aether.UI.Game;

/// <summary>
/// The open inventory's misc equipment page (Main.DrawInventory, EquipPage 2): pet, light pet,
/// minecart, mount and hook slots with their dyes, and the pet/light visibility and minecart upgrade
/// toggles. The buffs listed below them stay legacy.
/// </summary>
public class UIMiscEquipSlots : UIGameScreen
{
    public static readonly UIMiscEquipSlots Instance = new UIMiscEquipSlots();

    private static readonly int[] Contexts = new int[5] { 19, 20, 18, 17, 16 };

    private int _top;

    // What the slots were last built for.
    private bool _built;

    private int _builtWidth;

    private int _builtTop;

    private float _builtScale;

    /// <summary>Called by Main.DrawInventory in place of the page's slot loop; false: draw the legacy one.</summary>
    public static bool Draw(int top)
    {
        InGamePreview.Update();
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        Instance._top = top;
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    public override void UpdateState()
    {
        if (!_built || _builtWidth != Main.screenWidth || _builtTop != _top || _builtScale != Main.inventoryScale)
        {
            _built = true;
            _builtWidth = Main.screenWidth;
            _builtTop = _top;
            _builtScale = Main.inventoryScale;
            Build();
            Recalculate();
        }
    }

    private void Build()
    {
        RemoveAllChildren();
        float scale = Main.inventoryScale;
        for (int column = 0; column < 2; column++)
        {
            for (int i = 0; i < 5; i++)
            {
                Rectangle area = new Rectangle(Main.screenWidth - 92 - column * 47, _top + i * 47, (int)(TextureAssets.InventoryBack.Width() * scale), (int)(TextureAssets.InventoryBack.Height() * scale));
                UIGameItemSlot slot = (column == 0) ? new UIGameItemSlot(() => Main.LocalPlayer.miscEquips, i, Contexts[i]) : new UIGameItemSlot(() => Main.LocalPlayer.miscDyes, i, 33);
                slot.ExcludeFarEdges = true;
                slot.BeforeHandle = delegate
                {
                    Main.armorHide = true;
                    return true;
                };
                slot.Place(new Vector2(area.X, area.Y), scale);
                Append(slot);
                if (column != 0)
                {
                    continue;
                }
                Rectangle tick = new Rectangle(area.Left + 34, area.Top - 2, TextureAssets.InventoryTickOn.Width(), TextureAssets.InventoryTickOn.Height());
                if (i < 2)
                {
                    int misc = i;
                    UIEquipToggle toggle = new UIEquipToggle(() => Main.LocalPlayer.hideMisc[misc] ? TextureAssets.InventoryTickOff.Value : TextureAssets.InventoryTickOn.Value, () => !Main.LocalPlayer.hideMisc[misc], delegate
                    {
                        if (misc == 0)
                        {
                            Main.LocalPlayer.TogglePet();
                        }
                        else
                        {
                            Main.LocalPlayer.ToggleLight();
                        }
                    }, (int state) => Lang.inter[58 + state].Value, Color.White * 0.7f);
                    toggle.Place(tick);
                    Append(toggle);
                }
                else if (i == 2)
                {
                    // "On" (state 1) here means the upgrade is off, as in the legacy code.
                    UIEquipToggle toggle = new UIEquipToggle(() => TextureAssets.Extra[Main.LocalPlayer.enabledSuperCart ? 255 : 256].Value, () => !Main.LocalPlayer.enabledSuperCart, delegate
                    {
                        Main.LocalPlayer.enabledSuperCart = !Main.LocalPlayer.enabledSuperCart;
                    }, (int state) => Language.GetTextValue((state == 1) ? "GameUI.SuperCartDisabled" : "GameUI.SuperCartEnabled"), Color.White)
                    {
                        IsAvailable = () => Main.LocalPlayer.unlockedSuperCart
                    };
                    toggle.Place(tick);
                    Append(toggle);
                }
            }
        }
    }
}
