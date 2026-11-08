using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Game;

/// <summary>
/// The open inventory's NPC housing page (Main.DrawNPCHousesInUI, EquipPage 1): the housing query
/// icon and one head per town NPC type present, in columns down the right edge; clicking one picks it
/// up to assign it a house.
/// </summary>
public class UIHousingPage : UIGameScreen
{
    public static readonly UIHousingPage Instance = new UIHousingPage();

    private readonly int[] _npcByHead = new int[NPCHeadID.Count];

    private readonly List<int> _typesWithHead = new List<int>();

    private readonly List<UIHousingIcon> _icons = new List<UIHousingIcon>();

    private int _top;

    private int _shown;

    /// <summary>Set by the hovered icon: what the mouse text says.</summary>
    internal string HoverText = "";

    /// <summary>Called by Main.DrawNPCHousesInUI in place of its body.</summary>
    public static bool Draw(int inventoryTop)
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        Instance._top = inventoryTop;
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    public override void UpdateState()
    {
        float scale = Main.inventoryScale;
        int columnX = Main.screenWidth - 64 - 28;
        if (Main.mouseX > columnX && Main.mouseX < (int)((float)columnX + 56f * scale) && Main.mouseY > _top && Main.mouseY < (int)((float)_top + 448f * scale) && !PlayerInput.IgnoreMouseInterface)
        {
            Main.LocalPlayer.mouseInterface = true;
        }
        // One head per town NPC type: the first NPC of each type that has a head the page can show.
        _typesWithHead.Clear();
        for (int i = 0; i < _npcByHead.Length; i++)
        {
            _npcByHead[i] = -1;
        }
        for (int j = 0; j < Main.maxNPCs; j++)
        {
            NPC npc = Main.npc[j];
            if (npc.active && !_typesWithHead.Contains(npc.type))
            {
                int head = TownNPCProfiles.GetHeadIndexSafe(npc);
                if (head > 0 && head <= NPCHeadID.Count && !NPCHeadID.Sets.CannotBeDrawnInHousingUI[head] && _npcByHead[head] == -1)
                {
                    _npcByHead[head] = j;
                    _typesWithHead.Add(npc.type);
                }
            }
        }
        Main.hidePVPAndTeamIcons = false;
        UILinkPointNavigator.Shortcuts.NPCS_IconsPerColumn = 1;
        UILinkPointNavigator.Shortcuts.NPCS_SelectedNPC = -2;
        Vector2 slotSize = TextureAssets.InventoryBack.Size();
        int index = 0;
        int yShift = 0;
        int xShift = 0;
        int total = 0;
        int inColumn = 0;
        foreach (int head in NPCHeadID.Sets.HeadListOrder)
        {
            if (head != 0 && _npcByHead[head] == -1)
            {
                continue;
            }
            int y = (int)((float)_top + (float)(index * 56) * scale) + yShift;
            // A new column, leftwards, once the column reaches the bottom of the screen.
            if (y > Main.screenHeight - 80)
            {
                xShift -= 48;
                yShift -= y - _top;
                y = (int)((float)_top + (float)(index * 56) * scale) + yShift;
                UILinkPointNavigator.Shortcuts.NPCS_IconsPerColumn = inColumn;
                if (xShift <= -144)
                {
                    Main.hidePVPAndTeamIcons = true;
                }
                inColumn = 0;
            }
            if (index >= _icons.Count)
            {
                UIHousingIcon icon = new UIHousingIcon(this)
                {
                    IgnoresMouseInteraction = true
                };
                _icons.Add(icon);
                Append(icon);
            }
            _icons[index].Set(head, (head != 0) ? _npcByHead[head] : (-1), index, new Vector2(columnX + xShift, y), new Vector2((int)slotSize.X, (int)slotSize.Y) * scale);
            index++;
            total++;
            inColumn++;
        }
        _shown = index;
        if (UILinkPointNavigator.Shortcuts.NPCS_IconsPerColumn < inColumn)
        {
            UILinkPointNavigator.Shortcuts.NPCS_IconsPerColumn = inColumn;
        }
        UILinkPointNavigator.Shortcuts.NPCS_IconsTotal = total;
    }

    protected override void DrawChildren(SpriteBatch spriteBatch)
    {
        HoverText = "";
        for (int i = 0; i < _shown; i++)
        {
            _icons[i].DrawIcon(spriteBatch);
        }
        if (HoverText != "" && Main.mouseItem.type == 0)
        {
            Main.instance.MouseText(HoverText, 0, 0);
        }
        PlayerInput.UpdateHousingCursor();
    }
}

/// <summary>One housing page icon: the housing query (head 0) or a town NPC's head.</summary>
public class UIHousingIcon : UIElement
{
    private readonly UIHousingPage _page;

    private int _head;

    private int _npc;

    private int _index;

    private Vector2 _position;

    private Vector2 _size;

    public UIHousingIcon(UIHousingPage page)
    {
        _page = page;
    }

    public void Set(int head, int npc, int index, Vector2 position, Vector2 size)
    {
        _head = head;
        _npc = npc;
        _index = index;
        _position = position;
        _size = size;
    }

    public void DrawIcon(SpriteBatch spriteBatch)
    {
        float scale = Main.inventoryScale;
        // Edges included, as in the legacy hit test.
        if (Main.mouseX >= _position.X && (float)Main.mouseX <= _position.X + _size.X && Main.mouseY >= _position.Y && (float)Main.mouseY <= _position.Y + _size.Y)
        {
            Main.mouseText = true;
            _page.HoverText = (_head != 0) ? Main.npc[_npc].FullName : Lang.inter[8].Value;
            UILinkPointNavigator.Shortcuts.NPCS_SelectedNPC = _npc;
            if (!PlayerInput.IgnoreMouseInterface)
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeftRelease && Main.mouseLeft && !PlayerInput.UsingGamepadUI && Main.mouseItem.type == 0)
                {
                    SoundEngine.PlaySound(12);
                    if (_head == 0)
                    {
                        Main.instance.SetMouseNPC_ToHousingQuery();
                    }
                    else
                    {
                        Main.instance.SetMouseNPC(_npc, Main.npc[_npc].type);
                    }
                    Main.mouseLeftRelease = false;
                }
            }
        }
        Vector2 slotSize = TextureAssets.InventoryBack.Size();
        UILinkPointNavigator.SetPosition(600 + _index, _position + slotSize * 0.75f);
        bool selected = UILinkPointNavigator.CurrentPoint - 600 == _index;
        spriteBatch.Draw(selected ? TextureAssets.InventoryBack14.Value : TextureAssets.InventoryBack11.Value, _position, null, selected ? Color.White : Main.inventoryBack, 0f, default(Vector2), scale, SpriteEffects.None, 0f);
        Texture2D head = TextureAssets.NpcHead[_head].Value;
        Vector2 headSize = head.Size();
        float largest = Math.Max(headSize.X, headSize.Y);
        float headScale = (largest > 36f) ? (36f / largest) : 1f;
        spriteBatch.Draw(head, _position + new Vector2(26f, 26f) * scale, null, Color.White, 0f, headSize / 2f, headScale, SpriteEffects.None, 0f);
    }
}
