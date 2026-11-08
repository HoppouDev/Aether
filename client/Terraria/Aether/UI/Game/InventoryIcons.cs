using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.GameContent;
using Terraria.Graphics.Capture;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Game;

/// <summary>A screen of several icon buttons, drawn from the legacy method it replaces.</summary>
public class UIIconButtonGroup : UIGameScreen
{
    public readonly List<UIIconButton> Buttons = new List<UIIconButton>();

    // Appended when the screen first opens: groups are built in static initializers, which the game
    // runs at startup (Program.ForceLoadAssembly) before there's a UserInterface to lay them out in.
    public UIIconButtonGroup Add(UIIconButton button)
    {
        Buttons.Add(button);
        return this;
    }

    public override void OnInitialize()
    {
        foreach (UIIconButton button in Buttons)
        {
            Append(button);
        }
    }

    /// <summary>Called by the legacy method in place of its body; false: run the legacy body.</summary>
    public bool Draw()
    {
        InGamePreview.Update();
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        GameScreens.Draw(this, Main.spriteBatch);
        return true;
    }
}

/// <summary>
/// The open inventory's other icon groups: the equipment page icons (Main.DrawPageIcons), PvP and team
/// selection (Main.DrawPVPIcons), equipment loadouts (Main.DrawLoadoutButtons) and the map style
/// buttons (in Main.DrawInventory).
/// </summary>
public static class InventoryIcons
{
    // --- equipment page icons: misc equipment (2), NPC housing (1), camera mode (3) ---------------

    private static int _pageIconsY;

    private static readonly int[] PageOrder = new int[3] { 2, 1, 3 };

    public static readonly UIIconButtonGroup PageIcons = BuildPageIcons();

    /// <summary>Called by Main.DrawPageIcons in place of its body; hovered is its return value (-1: none).</summary>
    public static bool DrawPageIcons(int yPos, out int hovered)
    {
        hovered = -1;
        _pageIconsY = yPos;
        if (!PageIcons.Draw())
        {
            return false;
        }
        for (int i = 0; i < 3; i++)
        {
            if (PageIcons.Buttons[i].IsHovered)
            {
                hovered = PageOrder[i];
            }
        }
        ItemSlot.SelectEquipPage(Main.mouseItem);
        if (Main.EquipPage == -1)
        {
            Main.EquipPage = Main.EquipPageSelected;
        }
        return true;
    }

    private static Texture2D PageTexture(int page)
    {
        return page switch
        {
            2 => TextureAssets.EquipPage[(Main.EquipPage == 2) ? 3 : 2].Value,
            1 => TextureAssets.EquipPage[(Main.EquipPage == 1) ? 5 : 4].Value,
            _ => TextureAssets.EquipPage[(Main.EquipPage == 3) ? 10 : 8].Value
        };
    }

    private static UIIconButtonGroup BuildPageIcons()
    {
        UIIconButtonGroup group = new UIIconButtonGroup();
        for (int i = 0; i < 3; i++)
        {
            int page = PageOrder[i];
            int index = i;
            group.Add(new UIIconButton
            {
                Area = delegate
                {
                    Texture2D texture = PageTexture(page);
                    return new Rectangle(Main.screenWidth - 80 - 48 * index, _pageIconsY, texture.Width, texture.Height);
                },
                // Dyes can be dropped on the misc page's icon to switch to it.
                CanHover = () => Main.mouseItem.stack < 1 || (page == 2 && Main.mouseItem.dye > 0),
                ConsumesClick = false,
                // Main.DrawInventory ticks for these itself.
                TickOnHover = false,
                OnPress = delegate
                {
                    if (page == 3)
                    {
                        if (Main.CaptureModeDisabled)
                        {
                            return;
                        }
                        if (Terraria.GameInput.PlayerInput.UsingGamepad)
                        {
                            CaptureInterface.QuickScreenshot();
                        }
                        else
                        {
                            CaptureManager.Instance.Active = true;
                            Main.blockMouse = true;
                        }
                    }
                    else
                    {
                        Main.EquipPageSelected = (Main.EquipPageSelected != page) ? page : 0;
                    }
                    SoundEngine.PlaySound(10);
                },
                DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
                {
                    Vector2 position = new Vector2(area.X, area.Y);
                    Texture2D texture = PageTexture(page);
                    if (hovered && !(page == 3 && Main.CaptureModeDisabled))
                    {
                        Texture2D highlight = TextureAssets.EquipPage[page switch
                        {
                            2 => 6,
                            1 => 7,
                            _ => 9
                        }].Value;
                        spriteBatch.Draw(highlight, position, null, Main.OurFavoriteColor, 0f, (page == 3) ? Vector2.Zero : new Vector2(2f), 0.9f, SpriteEffects.None, 0f);
                    }
                    spriteBatch.Draw(texture, position, null, (page == 3 && Main.CaptureModeDisabled) ? Color.Red : Color.White, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0f);
                    UILinkPointNavigator.SetPosition(305 + index, position + texture.Size() * 0.75f);
                }
            });
        }
        return group;
    }

    // --- PvP and team selection ------------------------------------------------------------------

    public static readonly UIIconButtonGroup PvpIcons = BuildPvpIcons();

    /// <summary>Called by Main.DrawPVPIcons in place of its body.</summary>
    public static bool DrawPvpIcons()
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        if (Main.EquipPage != 1)
        {
            Main.hidePVPAndTeamIcons = false;
        }
        if (!Main.hidePVPAndTeamIcons)
        {
            Main.inventoryScale = 0.6f;
            PvpIcons.Draw();
        }
        return true;
    }

    // The PvP toggle's spot; the team buttons start 60 below it and 10 to its left.
    private static Point PvpOrigin()
    {
        int step = (int)(52f * 0.6f);
        int x = 707 - step * 4 + Main.screenWidth - 800 + ((Main.EquipPage == 2) ? (step + step / 2) : 0);
        int y = 114 + Main.mH + step * 2 + step / 2 - 12;
        return new Point(x, y);
    }

    private static bool CanUseTeams => Main.teamCooldown == 0 || Terraria.GameInput.PlayerInput.UsingGamepad;

    private static float TeamAlpha => (Main.teamCooldown == 0) ? 1f : 0.25f;

    private static readonly string[] TeamNames = new string[6] { "None", "Red", "Green", "Blue", "Yellow", "Pink" };

    private static UIIconButtonGroup BuildPvpIcons()
    {
        UIIconButtonGroup group = new UIIconButtonGroup();
        group.Add(new UIIconButton
        {
            Area = delegate
            {
                Point origin = PvpOrigin();
                return Main.ShouldPVPDraw ? new Rectangle(origin.X - 6, origin.Y - 1, 31, 38) : Rectangle.Empty;
            },
            ConsumesClick = false,
            TickOnHover = false,
            WhileHovered = delegate
            {
                if (Main.teamCooldown == 0)
                {
                    Main.instance.MouseTextHackZoom(Language.GetTextValue(Main.LocalPlayer.hostile ? "UI.PvpIcon_Active" : "UI.PvpIcon_Inactive"));
                }
            },
            OnPress = delegate
            {
                if (Main.teamCooldown == 0)
                {
                    Main.teamCooldown = Main.teamCooldownLen;
                    SoundEngine.PlaySound(12);
                    Main.LocalPlayer.hostile = !Main.LocalPlayer.hostile;
                    NetMessage.SendData(30, -1, -1, null, Main.myPlayer);
                }
            },
            DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
            {
                if (!Main.ShouldPVPDraw)
                {
                    return;
                }
                Point origin = PvpOrigin();
                Texture2D texture = TextureAssets.Pvp[0].Value;
                Rectangle frame = texture.Frame(2, 7);
                frame.Location = new Point(frame.Width * (Main.LocalPlayer.hostile ? 1 : 0), frame.Height * Main.LocalPlayer.team);
                Vector2 position = new Vector2(origin.X - 10, origin.Y);
                spriteBatch.Draw(texture, position, frame, Color.White * TeamAlpha);
                if (hovered && CanUseTeams)
                {
                    frame.Y = frame.Height * PlayerTeamID.Count;
                    spriteBatch.Draw(texture, position, frame, Color.White);
                }
                UILinkPointNavigator.SetPosition(1550, position + frame.Size() * 0.75f);
            }
        });
        for (int i = 0; i < 6; i++)
        {
            int team = i;
            group.Add(new UIIconButton
            {
                Area = delegate
                {
                    if (!Main.ShouldTeamSelectDraw)
                    {
                        return Rectangle.Empty;
                    }
                    Point origin = PvpOrigin();
                    Rectangle frame = TextureAssets.Pvp[1].Frame(6);
                    // The legacy loop narrows its shared rectangle after the first button.
                    return new Rectangle(origin.X - 10 + team % 2 * 20, origin.Y + 60 + team / 2 * 20, (team == 0) ? frame.Width : (frame.Width - 2), frame.Height);
                },
                ConsumesClick = false,
                TickOnHover = false,
                WhileHovered = delegate
                {
                    if (Main.teamCooldown == 0)
                    {
                        Main.instance.MouseTextHackZoom(Language.GetTextValue("UI.TeamIcon" + TeamNames[team] + ((Main.LocalPlayer.team == team) ? "_Active" : "_Inactive")));
                    }
                },
                OnPress = delegate
                {
                    Player player = Main.LocalPlayer;
                    if (player.team == team || Main.teamCooldown != 0)
                    {
                        return;
                    }
                    if (!player.TeamChangeAllowed())
                    {
                        Main.NewText(Lang.misc[84].Value, ChatColors.ServerMessage);
                        return;
                    }
                    Main.teamCooldown = Main.teamCooldownLen;
                    SoundEngine.PlaySound(12);
                    player.team = team;
                    if (Main.netMode != 0)
                    {
                        NetMessage.SendData(157, -1, -1, null, Main.myPlayer);
                    }
                    else if (Main.teamBasedSpawnsSeed)
                    {
                        player.Spawn(PlayerSpawnContext.TeamSwap);
                    }
                },
                DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
                {
                    if (!Main.ShouldTeamSelectDraw)
                    {
                        return;
                    }
                    Rectangle frame = TextureAssets.Pvp[1].Frame(6);
                    frame.X = frame.Width * team;
                    frame.Width -= 2;
                    Vector2 position = new Vector2(area.X, area.Y);
                    if (hovered && CanUseTeams)
                    {
                        spriteBatch.Draw(TextureAssets.Pvp[2].Value, position + new Vector2(-2f), Color.White);
                    }
                    spriteBatch.Draw(TextureAssets.Pvp[1].Value, position, frame, Color.White * TeamAlpha);
                    UILinkPointNavigator.SetPosition(1550 + team + 1, position + new Vector2(frame.Width, frame.Height) * 0.75f);
                }
            });
        }
        return group;
    }

    // --- equipment loadouts ----------------------------------------------------------------------

    private static int _loadoutsTop;

    private static UIIconButtonGroup _loadouts;

    /// <summary>Called by Main.DrawLoadoutButtons in place of its body.</summary>
    public static bool DrawLoadouts(int inventoryTop)
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        _loadoutsTop = inventoryTop;
        int count = Main.LocalPlayer.Loadouts.Length;
        if (_loadouts == null || _loadouts.Buttons.Count != count)
        {
            _loadouts = new UIIconButtonGroup();
            for (int i = 0; i < count; i++)
            {
                _loadouts.Add(LoadoutButton(i));
            }
        }
        return _loadouts.Draw();
    }

    private static UIIconButton LoadoutButton(int index)
    {
        return new UIIconButton
        {
            Area = () => new Rectangle(Main.screenWidth - 58 + 14 + 4, _loadoutsTop + 36 * index, 32, 32),
            ConsumesClick = false,
            WhileHovered = delegate
            {
                if (!Main.mouseText)
                {
                    Main.instance.MouseText(Language.GetTextValue("UI.Loadout" + (index + 1)), 0, 0);
                    Main.mouseText = true;
                }
            },
            OnPress = delegate
            {
                Main.LocalPlayer.TrySwitchingLoadout(index);
            },
            DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
            {
                Texture2D texture = TextureAssets.Extra[259].Value;
                Vector2 center = area.Center.ToVector2();
                Rectangle frame = texture.Frame(3, 3, (index == Main.LocalPlayer.CurrentLoadoutIndex) ? 1 : 0, index);
                spriteBatch.Draw(texture, center, frame, Color.White, 0f, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
                if (hovered)
                {
                    frame = texture.Frame(3, 3, 2, index);
                    spriteBatch.Draw(texture, center, frame, Main.OurFavoriteColor, 0f, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
                }
                UILinkPointNavigator.SetPosition(312 + index, center);
            }
        };
    }

    // --- map style buttons: full map, minimap off, minimap, overlay map ----------------------------

    public static readonly UIIconButtonGroup MapButtons = BuildMapButtons();

    // Along the top right, or (on narrow screens) a column at the right edge, as the legacy code places them.
    private static Rectangle MapButtonArea(int index)
    {
        if (Main.screenWidth < 940)
        {
            return new Rectangle(Main.screenWidth - 40, -200 + index * 32, 32, 30);
        }
        return new Rectangle(Main.screenWidth - 440 + index * 32, 40, 32, 30);
    }

    private static UIIconButtonGroup BuildMapButtons()
    {
        UIIconButtonGroup group = new UIIconButtonGroup();
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            group.Add(new UIIconButton
            {
                Area = () => MapButtonArea(index),
                IncludeFarEdges = true,
                ConsumesClick = false,
                OnPress = delegate
                {
                    if (index == 0)
                    {
                        Main.playerInventory = false;
                        Main.LocalPlayer.SetTalkNPC(-1);
                        Main.npcChatCornerItem = 0;
                        SoundEngine.PlaySound(10);
                        Main.mapFullscreenScale = 2.5f;
                        Main.MapPylonTile = new Terraria.DataStructures.Point16(-1, -1);
                        Main.mapFullscreen = true;
                        Main.resetMapFull = true;
                    }
                    else
                    {
                        Main.mapStyle = index - 1;
                        SoundEngine.PlaySound(12);
                    }
                },
                DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
                {
                    int alpha = hovered ? 255 : ((index > 0 && Main.mapStyle == index - 1) ? 200 : 120);
                    Texture2D texture = TextureAssets.MapIcon[index + (hovered ? 4 : 0)].Value;
                    spriteBatch.Draw(texture, new Vector2(area.X, area.Y), new Color(alpha, alpha, alpha, alpha));
                }
            });
        }
        return group;
    }
}
