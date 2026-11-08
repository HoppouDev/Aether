using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace Aether.UI.Game;

/// <summary>
/// One slot on its own, placed by the legacy code around it each frame: the reforge slot (context 5)
/// and the Guide's crafting slot (context 7). Hovering it hides the crafting list, as in the legacy code.
/// </summary>
public class UISingleSlot : UIGameScreen
{
    public static readonly UISingleSlot Reforge = new UISingleSlot(() => Main.reforgeItem, delegate(Item item)
    {
        Main.reforgeItem = item;
    }, 5);

    public static readonly UISingleSlot Guide = new UISingleSlot(() => Main.guideItem, delegate(Item item)
    {
        Main.guideItem = item;
    }, 7);

    private readonly UIGameItemSlot _slot;

    private Vector2 _position = new Vector2(float.NaN);

    private float _scale;

    private UISingleSlot(Func<Item> getItem, Action<Item> setItem, int context)
    {
        _slot = new UIGameItemSlot(getItem, setItem, context)
        {
            BeforeHandle = delegate
            {
                Main.craftingHide = true;
                return true;
            }
        };
    }

    public override void OnInitialize()
    {
        Append(_slot);
    }

    /// <summary>Called by Main.DrawInventory in place of the slot's legacy code; false: draw that.</summary>
    public bool Draw(int x, int y)
    {
        InGamePreview.Update();
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        Vector2 position = new Vector2(x, y);
        if (position != _position || Main.inventoryScale != _scale)
        {
            _position = position;
            _scale = Main.inventoryScale;
            _slot.Place(position, _scale);
            _slot.Recalculate();
        }
        GameScreens.Draw(this, Main.spriteBatch);
        return true;
    }
}
