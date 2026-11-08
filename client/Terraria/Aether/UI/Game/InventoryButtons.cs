using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.States;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Game;

/// <summary>
/// The open inventory's icon buttons (Main.DrawInventory and the helpers it calls): bestiary and
/// emotes, quick stack to nearby chests and sort inventory, and the hotbar lock.
/// </summary>
public static class InventoryButtons
{
    public static readonly UIIconButtonScreen Bestiary = SideButton(498, 310, TextureAssets.BestiaryMenuButton, "GameUI.Bestiary", delegate
    {
        IngameFancyUI.OpenUIState(Main.BestiaryUI);
        Main.BestiaryUI.OnOpenPage();
    });

    public static readonly UIIconButtonScreen Emotes = SideButton(534, 309, TextureAssets.EmoteMenuButton, "GameUI.Emote", delegate
    {
        IngameFancyUI.OpenUIState(new UIEmotesMenu());
    });

    public static readonly UIIconButtonScreen QuickStackAll = new UIIconButtonScreen(new UIIconButton
    {
        Area = () => new Rectangle(498, 244, TextureAssets.ChestStack[0].Width(), TextureAssets.ChestStack[0].Height()),
        IncludeFarEdges = true,
        CanHover = () => !Main.LocalPlayerHasPendingInventoryActions(),
        OnPress = delegate
        {
            Main.LocalPlayer.QuickStackAllChests();
            SoundEngine.PlaySound(12);
        },
        // Right-click switches between quick stacking and smart stacking.
        OnRightPress = delegate
        {
            Player.Settings.CycleQuickStackMode();
            SoundEngine.PlaySound(10);
            Main.SaveSettings();
        },
        WhileHovered = delegate
        {
            if (!Main.mouseText)
            {
                Main.instance.MouseTextNoOverride(Language.GetTextValue((Player.Settings.StackToChestsPreferredMode == Player.Settings.StackToNearbyChestsMode.QuickStackToNearbyChests) ? "GameUI.QuickStackToNearby" : "GameUI.SmartStackToNearby"), 0, 0);
            }
        },
        DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
        {
            Texture2D texture = TextureAssets.ChestStack[(hovered ? 1 : 0) + (int)Player.Settings.StackToChestsPreferredMode * 2].Value;
            spriteBatch.Draw(texture, new Vector2(area.X, area.Y), Color.White);
            UILinkPointNavigator.SetPosition(301, new Vector2((float)area.X + (float)area.Width * 0.75f, (float)area.Y + (float)area.Height * 0.75f));
        }
    });

    public static readonly UIIconButtonScreen SortInventory = new UIIconButtonScreen(new UIIconButton
    {
        Area = () => new Rectangle(534, 244, 30, 30),
        IncludeFarEdges = true,
        CanHover = () => !Main.LocalPlayerHasPendingInventoryActions(),
        OnPress = delegate
        {
            ItemSorting.SortInventory();
            SoundEngine.PlaySound(12);
        },
        Tooltip = () => Language.GetTextValue("GameUI.SortInventory"),
        DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
        {
            spriteBatch.Draw(TextureAssets.InventorySort[hovered ? 1 : 0].Value, new Vector2(area.X, area.Y), Color.White);
            UILinkPointNavigator.SetPosition(302, new Vector2((float)area.X + (float)area.Width * 0.75f, (float)area.Y + (float)area.Height * 0.75f));
        }
    });

    private static int _hotbarLockY;

    public static readonly UIIconButtonScreen HotbarLock = new UIIconButtonScreen(new UIIconButton
    {
        Area = delegate
        {
            Rectangle frame = TextureAssets.HbLock[0].Value.Frame(2);
            return new Rectangle(0, _hotbarLockY, (int)((float)frame.Width * 0.9f), (int)((float)frame.Height * 0.9f));
        },
        // Hovered strictly inside, and the click isn't used up (the legacy code doesn't).
        ExcludeNearEdges = true,
        AutomationCaption = () => Lang.inter[Main.LocalPlayer.hbLocked ? 6 : 5].Value,
        ConsumesClick = false,
        WhileHovered = delegate
        {
            Main.instance.MouseText(Lang.inter[Main.LocalPlayer.hbLocked ? 6 : 5].Value, 0, 0);
            Main.mouseText = true;
        },
        OnPress = delegate
        {
            SoundEngine.PlaySound(22);
            Main.LocalPlayer.hbLocked = !Main.LocalPlayer.hbLocked;
        },
        DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
        {
            Texture2D texture = TextureAssets.HbLock[(!Main.LocalPlayer.hbLocked) ? 1 : 0].Value;
            spriteBatch.Draw(texture, new Vector2(area.X, area.Y), texture.Frame(2), Color.White, 0f, default(Vector2), 0.9f, SpriteEffects.None, 0f);
            if (hovered)
            {
                spriteBatch.Draw(texture, new Vector2(area.X, area.Y), texture.Frame(2, 1, 1), Main.OurFavoriteColor, 0f, default(Vector2), 0.9f, SpriteEffects.None, 0f);
            }
        }
    });

    /// <summary>Called by Main.DrawHotbarLockIcon in place of its body.</summary>
    public static bool DrawHotbarLock(int pivotTopLeftY, bool pushSideToolsUp)
    {
        _hotbarLockY = pushSideToolsUp ? pivotTopLeftY : (21 + pivotTopLeftY);
        return HotbarLock.Draw();
    }

    // The bestiary and emote buttons: a 30×30 button beside the trash slot, lower while a chest or
    // shop is open (and lower still while renaming a chest); opening them ends any NPC chat.
    private static UIIconButtonScreen SideButton(int x, int linkPoint, ReLogic.Content.Asset<Texture2D> texture, string tooltip, System.Action open)
    {
        UIIconButtonScreen screen = new UIIconButtonScreen(new UIIconButton
        {
            Area = delegate
            {
                int y = 278 + (Main.ChestOrShopUIVisible ? 168 : 0) + (Main.editChest ? 24 : 0);
                return new Rectangle(x + (Main.ChestOrShopUIVisible ? 5 : 0), y, 30, 30);
            },
            CanHover = () => !Main.LocalPlayerHasPendingInventoryActions(),
            OnPress = delegate
            {
                Main.LocalPlayer.SetTalkNPC(-1);
                Main.npcChatCornerItem = 0;
                Main.npcChatText = "";
                SoundEngine.PlaySound(12);
                open();
            },
            Tooltip = () => Language.GetTextValue(tooltip),
            DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
            {
                Rectangle frame = texture.Value.Frame(2, 1, hovered ? 1 : 0);
                frame.Width -= 2;
                frame.Height -= 2;
                Vector2 center = area.Center.ToVector2();
                spriteBatch.Draw(texture.Value, center, frame, Color.White, 0f, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
                UILinkPointNavigator.SetPosition(linkPoint, center);
            }
        });
        // The legacy code leaves Main.inventoryScale at the trash slot's scale.
        screen.BeforeUpdate = delegate
        {
            Main.inventoryScale = Main.ChestOrShopUIVisible ? 0.755f : 0.85f;
        };
        return screen;
    }
}
