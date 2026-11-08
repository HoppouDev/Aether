using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Game;

/// <summary>
/// The banner claiming list (BannerClaimingUI.DrawBannersList): banners waiting to be claimed, in a
/// column that scrolls to the focused one like the crafting list; clicking the focused one claims it
/// (held: more of them). Also its toggle beside the crafting toggle, which opens the list or grid.
/// </summary>
public class UIBannerList : UICraftingScreen
{
    public static readonly UIBannerList Instance = new UIBannerList();

    private BannerClaimingUI _banners;

    private int _adjY;

    private int _middleY;

    private Color _tipColor;

    private static Point _toggle;

    public static bool Draw(BannerClaimingUI banners, int adjY, int middleY, Color craftingTipColor)
    {
        Instance._banners = banners;
        Instance._adjY = adjY;
        Instance._middleY = middleY;
        Instance._tipColor = craftingTipColor;
        return Instance.DrawScreen();
    }

    protected override void DrawIcons(SpriteBatch spriteBatch)
    {
        BannerClaimingUI ui = _banners;
        UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeBig = -1;
        UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeSmall = -1;
        int count = ui.UpdateAndGetClaimableItemsCount();
        BannerSystem.AnyNewClaimableBanners = false;
        if (count <= 0)
        {
            Main.TryChangePipsPage(Main.PipPage.Recipes);
            return;
        }
        spriteBatch.DrawString(FontAssets.MouseText.Value, Language.GetTextValue("GameUI.BannersTitle"), new Vector2(76f, 414 + _adjY), _tipColor, 0f, default(Vector2), 1f, SpriteEffects.None, 0f);
        ui.DrawBannersList_Move(count);
        if (Main.PipsFastScroll)
        {
            ui._currentElementOffset = ui._focusedElementIndex;
            Main.PipsFastScroll = false;
        }
        bool pressing = Main.mouseLeft || Main.mouseRight;
        if (!pressing)
        {
            ui._bannerStackRequestLimiter = 0;
        }
        for (int i = 0; i < count; i++)
        {
            float offset = ((float)i - ui._currentElementOffset) * 65f;
            if (Math.Abs(offset) > (float)_middleY)
            {
                continue;
            }
            BannerClaimingUI.Entry entry = ui._squashedItemTypesToShow[i];
            if (!ContentSamples.ItemsByType.TryGetValue(entry.Type, out var item))
            {
                continue;
            }
            float scale = Math.Max(0.75f, 100f / (Math.Abs(offset) + 100f));
            Main.inventoryScale = scale;
            ui.GetItemSlotColors(offset, _middleY, 100f, i, out var backAlpha, out var lightColor);
            Vector2 position = new Vector2((int)(46f - 26f * scale), (int)(410f + offset * scale - 30f * scale + (float)_adjY));
            UICraftingIcon icon = NextIcon();
            if (!Main.LocalPlayer.creativeInterface && UICraftingIcon.IsHovered(position, scale))
            {
                Main.craftingHide = true;
                Main.LocalPlayer.mouseInterface = true;
                BannerSystem.ClearNewBanner(entry.Index);
                if (pressing)
                {
                    // A click on another banner focuses it; holding on the focused one claims it.
                    if (ui._focusedElementIndex != i)
                    {
                        if ((Main.mouseLeft && Main.mouseLeftRelease) || (Main.mouseRight && Main.mouseRightRelease))
                        {
                            ui._focusedElementIndex = i;
                            ui._bannerStackRequestLimiter = -1;
                        }
                    }
                    else if (offset == 0f)
                    {
                        ui.TryClaimingBanner(entry);
                    }
                }
                ItemSlot.MouseHover(item, 35);
            }
            if (i == ui._focusedElementIndex)
            {
                UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeSmall = 0;
                if (PlayerInput.SettingsForUI.HighlightThingsForMouse)
                {
                    ItemSlot.DrawGoldBGForCraftingMaterial = true;
                }
            }
            else
            {
                UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeSmall = -1;
            }
            // Shown with the claimable count as its stack.
            int stack = item.stack;
            item.stack = entry.Stack;
            ItemSlot.DrawNewAndShinyHighlightOverride = !ItemSlot.DrawGoldBGForCraftingMaterial && BannerSystem.IsNewBanner(entry.Index);
            icon.DrawItem(spriteBatch, ref item, position, backAlpha - 50.0, lightColor, 35);
            item.stack = stack;
            ItemSlot.DrawNewAndShinyHighlightOverride = false;
        }
    }

    private static readonly UIIconButtonScreen Toggle = new UIIconButtonScreen(new UIIconButton
    {
        Area = () => new Rectangle(_toggle.X - 14, _toggle.Y - 14, 29, 29),
        ConsumesClick = false,
        WhileHovered = delegate
        {
            Main.instance.MouseTextNoOverride(Language.GetTextValue("GameUI.BannersWindow"), 0, 0);
        },
        OnPress = delegate
        {
            if (!Main.TryChangePipsPage(Main.PipPage.Banners))
            {
                Main.PipsUseGrid = !Main.PipsUseGrid;
            }
            SoundEngine.PlaySound(12);
        },
        DrawIcon = delegate(SpriteBatch spriteBatch, Rectangle area, bool hovered)
        {
            Vector2 center = _toggle.ToVector2();
            UILinkPointNavigator.SetPosition(11002, center);
            if (Main.PipsCurrentPage == Main.PipPage.Banners)
            {
                Utils.DrawSelectedCraftingBarIndicator(spriteBatch, _toggle.X, _toggle.Y);
            }
            Asset<Texture2D> texture = TextureAssets.BannerToggle[2 - (Main.PipsCurrentPage == Main.PipPage.Banners && Main.PipsUseGrid).ToInt() * 2 + hovered.ToInt()];
            spriteBatch.Draw(texture.Value, center, null, Color.White, 0f, texture.Size() / 2f, 1f, SpriteEffects.None, 0f);
            if (BannerSystem.AnyNewClaimableBanners)
            {
                Utils.DrawNotificationIcon(spriteBatch, center + new Vector2(0f, -7f));
            }
        }
    });

    /// <summary>Called by BannerClaimingUI.DrawGridToggle once it's known there are banners to claim.</summary>
    public static bool DrawToggle(int x, int y)
    {
        _toggle = new Point(x, y);
        return Toggle.Draw();
    }
}

/// <summary>The banner claiming grid (BannerClaimingUI.DrawBannersGrid): clicking one focuses it in the list.</summary>
public class UIBannerGrid : UICraftingScreen
{
    public static readonly UIBannerGrid Instance = new UIBannerGrid();

    private const int Step = 42;

    private const int GridLeft = 310;

    private const int GridTop = 340;

    private BannerClaimingUI _banners;

    public static bool Draw(BannerClaimingUI banners)
    {
        Instance._banners = banners;
        return Instance.DrawScreen();
    }

    private static bool ArrowHovered(int x, int y)
    {
        if (Main.mouseX >= x && Main.mouseX <= x + TextureAssets.CraftUpButton.Width() && Main.mouseY >= y && Main.mouseY <= y + TextureAssets.CraftUpButton.Height())
        {
            return !PlayerInput.IgnoreMouseInterface;
        }
        return false;
    }

    protected override void DrawIcons(SpriteBatch spriteBatch)
    {
        BannerClaimingUI ui = _banners;
        int count = ui.UpdateAndGetClaimableItemsCount();
        BannerSystem.AnyNewClaimableBanners = false;
        UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeBig = -1;
        UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeSmall = -1;
        Main.inventoryScale = 0.75f;
        int perRow = (Main.screenWidth - GridLeft - 280) / Step;
        int rows = (Main.screenHeight - GridTop - 20) / Step;
        UILinkPointNavigator.Shortcuts.CRAFT_IconsPerRow = perRow;
        UILinkPointNavigator.Shortcuts.CRAFT_IconsPerColumn = rows;
        if (ui._gridStartingElementIndex > count - perRow * rows)
        {
            ui._gridStartingElementIndex = Math.Max(0, count - perRow * rows);
        }
        int arrowX = GridLeft - 20;
        int arrowY = GridTop + 2;
        if (ui._gridStartingElementIndex > 0)
        {
            if (ArrowHovered(arrowX, arrowY))
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeftRelease && Main.mouseLeft)
                {
                    ui._gridStartingElementIndex = Math.Max(0, ui._gridStartingElementIndex - perRow);
                    SoundEngine.PlaySound(12);
                    Main.mouseLeftRelease = false;
                }
            }
            spriteBatch.Draw(TextureAssets.CraftUpButton.Value, new Vector2(arrowX, arrowY), new Color(200, 200, 200, 200));
        }
        if (ui._gridStartingElementIndex < count - perRow * rows)
        {
            arrowY += 20;
            if (ArrowHovered(arrowX, arrowY))
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeftRelease && Main.mouseLeft)
                {
                    ui._gridStartingElementIndex += perRow;
                    SoundEngine.PlaySound(12);
                    if (ui._gridStartingElementIndex > count - perRow)
                    {
                        ui._gridStartingElementIndex = count - perRow;
                    }
                    Main.mouseLeftRelease = false;
                }
            }
            spriteBatch.Draw(TextureAssets.CraftDownButton.Value, new Vector2(arrowX, arrowY), new Color(200, 200, 200, 200));
        }
        ui._gridStartingElementIndex = Utils.Clamp(ui._gridStartingElementIndex, 0, count - 1);
        ui._gridSelectionCandidateIndex = Utils.Clamp(ui._gridSelectionCandidateIndex, 0, count - 1);
        int column = 0;
        int row = 0;
        for (int i = ui._gridStartingElementIndex; i < count; i++)
        {
            BannerClaimingUI.Entry entry = ui._squashedItemTypesToShow[i];
            if (!ContentSamples.ItemsByType.TryGetValue(entry.Type, out var item))
            {
                continue;
            }
            Vector2 position = new Vector2(GridLeft + column * Step, GridTop + row * Step);
            if (UICraftingIcon.IsHovered(position, Main.inventoryScale))
            {
                Main.LocalPlayer.mouseInterface = true;
                if (Main.mouseLeftRelease && Main.mouseLeft)
                {
                    ui._focusedElementIndex = i;
                    Main.PipsFastScroll = true;
                    Main.PipsUseGrid = false;
                    SoundEngine.PlaySound(12);
                    Main.mouseLeftRelease = false;
                    if (PlayerInput.UsingGamepadUI)
                    {
                        UILinkPointNavigator.ChangePage(22);
                        Main.LockCraftingForThisCraftClickDuration();
                    }
                }
                Main.craftingHide = true;
                Main.HoverItem = item.Clone();
                Main.HoverItem.stack = entry.Stack;
                ItemSlot.MouseHover(35);
                BannerSystem.ClearNewBanner(entry.Index);
                Main.hoverItemName = item.Name + ((item.stack > 1) ? (" (" + item.stack + ")") : "");
            }
            UILinkPointNavigator.Shortcuts.CRAFT_CurrentRecipeBig = (ui._gridSelectionCandidateIndex != i) ? (-1) : 0;
            int stack = item.stack;
            item.stack = entry.Stack;
            ItemSlot.DrawNewAndShinyHighlightOverride = BannerSystem.IsNewBanner(entry.Index);
            NextIcon().DrawItem(spriteBatch, ref item, position, Main.inventoryBack.A, default(Color), 35);
            ItemSlot.DrawNewAndShinyHighlightOverride = false;
            item.stack = stack;
            if (++column >= perRow)
            {
                column = 0;
                if (++row >= rows)
                {
                    break;
                }
            }
        }
    }
}
