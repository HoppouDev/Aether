using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace Aether.UI.Game;

/// <summary>A popup in the stack above the hotbar; the screen places it at its bottom anchor each frame.</summary>
public abstract class UINotificationPopup : UIElement
{
    public IInGameNotification Notification;

    /// <summary>Lays the popup out with its bottom centre at anchor; false: it's invisible this frame.</summary>
    public abstract bool Place(Vector2 anchor);

    public override bool ContainsPoint(Vector2 point)
    {
        return !PlayerInput.IgnoreMouseInterface && base.ContainsPoint(point);
    }

    /// <summary>The panel (brighter while hovered), as the legacy popups draw it.</summary>
    protected void DrawPanel(SpriteBatch spriteBatch, bool hovered)
    {
        Utils.DrawInvBG(spriteBatch, GetDimensions().ToRectangle(), hovered ? (new Color(64, 109, 164) * 0.75f) : (new Color(64, 109, 164) * 0.5f));
    }

    /// <summary>Whether the panel or a button in it is hovered and pressed now (taking the click, as the legacy code does).</summary>
    protected static bool Pressed(UIElement element)
    {
        if (!element.IsMouseHovering)
        {
            return false;
        }
        Main.LocalPlayer.mouseInterface = true;
        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            Main.mouseLeftRelease = false;
            return true;
        }
        return false;
    }

    protected static void SetArea(UIElement element, Rectangle area)
    {
        element.Left.Set(area.X, 0f);
        element.Top.Set(area.Y, 0f);
        element.Width.Set(area.Width, 0f);
        element.Height.Set(area.Height, 0f);
    }
}

/// <summary>"Achievement unlocked": its icon and name; clicking opens it in the achievements list.</summary>
public class UIAchievementPopup : UINotificationPopup
{
    private InGamePopups.AchievementUnlockedPopup Popup => (InGamePopups.AchievementUnlockedPopup)Notification;

    public override bool Place(Vector2 anchor)
    {
        InGamePopups.AchievementUnlockedPopup popup = Popup;
        if (popup.Opacity <= 0f)
        {
            return false;
        }
        float scale = popup.Scale * 1.1f;
        Vector2 size = (FontAssets.ItemStack.Value.MeasureString(popup._title) + new Vector2(58f, 10f)) * scale;
        SetArea(this, Utils.CenteredRectangle(anchor + new Vector2(0f, (0f - size.Y) * 0.5f), size));
        return true;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        InGamePopups.AchievementUnlockedPopup popup = Popup;
        float opacity = popup.Opacity;
        float scale = popup.Scale * 1.1f;
        Rectangle area = GetDimensions().ToRectangle();
        DrawPanel(spriteBatch, IsMouseHovering);
        float iconScale = scale * 0.3f;
        Rectangle frame = popup._achievementIconFrame;
        Vector2 icon = area.Right() - Vector2.UnitX * scale * (12f + iconScale * (float)frame.Width);
        Asset<Texture2D> border = popup._achievementBorderTexture;
        spriteBatch.Draw(popup._achievementTexture.Value, icon, frame, Color.White * opacity, 0f, new Vector2(0f, frame.Height / 2), iconScale, SpriteEffects.None, 0f);
        spriteBatch.Draw(border.Value, icon, null, Color.White * opacity, 0f, new Vector2(4f, border.Height() / 2), iconScale, SpriteEffects.None, 0f);
        Utils.DrawBorderString(spriteBatch, popup._title, icon - Vector2.UnitX * 10f, new Color(Main.mouseTextColor, Main.mouseTextColor, Main.mouseTextColor / 5, Main.mouseTextColor) * opacity, scale * 0.9f, 1f, 0.4f);
        if (Pressed(this))
        {
            IngameFancyUI.OpenAchievementsAndGoto(popup._theAchievement);
            popup._ingameDisplayTimeLeft = 0;
            popup.ShouldBeRemoved = true;
        }
    }
}

/// <summary>A button on the join request popup (accept or reject), half transparent until hovered.</summary>
public class UIJoinRequestButton : UIElement
{
    public readonly bool Reject;

    public Vector2 Position;

    public UIJoinRequestButton(bool reject)
    {
        Reject = reject;
    }

    public Texture2D Texture => Main.Assets.Request<Texture2D>(Reject ? "Images/UI/ButtonDelete" : "Images/UI/ButtonPlay").Value;

    public override bool ContainsPoint(Vector2 point)
    {
        return !PlayerInput.IgnoreMouseInterface && base.ContainsPoint(point);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Texture2D texture = Texture;
        spriteBatch.Draw(texture, Position, null, Color.White * (IsMouseHovering ? 1f : 0.5f), 0f, new Vector2(0f, 0.5f) * texture.Size(), 1f, SpriteEffects.None, 0f);
    }
}

/// <summary>A Steam friend asking to join: their name, with accept and reject buttons.</summary>
public class UIJoinRequestPopup : UINotificationPopup
{
    private readonly UIJoinRequestButton _accept = new UIJoinRequestButton(reject: false);

    private readonly UIJoinRequestButton _reject = new UIJoinRequestButton(reject: true);

    private readonly UIJoinRequestButton[] _buttons;

    public UIJoinRequestPopup()
    {
        _buttons = new UIJoinRequestButton[2] { _accept, _reject };
        Append(_accept);
        Append(_reject);
    }

    private InGamePopups.PlayerWantsToJoinGamePopup Popup => (InGamePopups.PlayerWantsToJoinGamePopup)Notification;

    private string Text => Utils.FormatWith(Popup._request.GetUserWrapperText(), new
    {
        DisplayName = Popup._request.UserDisplayName,
        FullId = Popup._request.UserFullIdentifier
    });

    public override bool Place(Vector2 anchor)
    {
        if (Popup.Opacity <= 0f)
        {
            return false;
        }
        float scale = Popup.Scale * 1.1f;
        Vector2 size = (FontAssets.ItemStack.Value.MeasureString(Text) + new Vector2(58f, 10f)) * scale;
        Rectangle area = Utils.CenteredRectangle(anchor + new Vector2(0f, (0f - size.Y) * 0.5f), size);
        SetArea(this, area);
        PlaceButton(_accept, area, new Vector2(area.Left + 7, MathHelper.Lerp(area.Top, area.Bottom, 0.5f) - (float)(_accept.Texture.Height / 2) - 1f));
        PlaceButton(_reject, area, new Vector2(area.Left + 7, MathHelper.Lerp(area.Top, area.Bottom, 0.5f) + (float)(_reject.Texture.Height / 2) + 1f));
        return true;
    }

    private static void PlaceButton(UIJoinRequestButton button, Rectangle panel, Vector2 position)
    {
        Texture2D texture = button.Texture;
        button.Position = position;
        Rectangle area = Utils.CenteredRectangle(position + new Vector2(texture.Width / 2, 0f), texture.Size());
        area.X -= panel.X;
        area.Y -= panel.Y;
        SetArea(button, area);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        DrawPanel(spriteBatch, IsMouseHovering || _accept.IsMouseHovering || _reject.IsMouseHovering);
    }

    protected override void DrawChildren(SpriteBatch spriteBatch)
    {
        base.DrawChildren(spriteBatch);
        InGamePopups.PlayerWantsToJoinGamePopup popup = Popup;
        Rectangle area = GetDimensions().ToRectangle();
        Utils.DrawBorderString(spriteBatch, Text, area.Center.ToVector2() + new Vector2(10f, 0f), new Color(Main.mouseTextColor, Main.mouseTextColor, Main.mouseTextColor / 5, Main.mouseTextColor) * popup.Opacity, popup.Scale * 1.1f * 0.9f, 0.5f, 0.4f);
        foreach (UIJoinRequestButton button in _buttons)
        {
            if (Pressed(button))
            {
                popup._timeLeft = 0;
                if (button.Reject)
                {
                    popup._request.Reject();
                }
                else
                {
                    popup._request.Accept();
                }
            }
        }
    }
}

/// <summary>Any other kind of notification, drawn by its own legacy code.</summary>
public class UILegacyNotification : UINotificationPopup
{
    private Vector2 _anchor;

    public UILegacyNotification()
    {
        IgnoresMouseInteraction = true;
    }

    public override bool Place(Vector2 anchor)
    {
        _anchor = anchor;
        return true;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Notification.DrawInGame(spriteBatch, _anchor);
    }
}

/// <summary>
/// The in-game notifications (InGameNotificationsTracker.DrawInGame): achievement unlocked and join
/// request popups, stacked upwards from above the hotbar, each pushing the next up as it fades in.
/// </summary>
public class UINotifications : UIGameScreen
{
    public static readonly UINotifications Instance = new UINotifications();

    private readonly Dictionary<IInGameNotification, UINotificationPopup> _popups = new Dictionary<IInGameNotification, UINotificationPopup>();

    // Reused every frame, to forget the popups of notifications that are gone.
    private readonly HashSet<IInGameNotification> _current = new HashSet<IInGameNotification>();

    private readonly List<IInGameNotification> _gone = new List<IInGameNotification>();

    /// <summary>Called by Main.DrawInterface_13_AchievementCompletePopups in place of the tracker's drawing; false: draw that.</summary>
    public static bool Draw()
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    public override void UpdateState()
    {
        float bottom = Main.screenHeight - 40;
        if (PlayerInput.UsingGamepad)
        {
            bottom -= 25f;
        }
        Vector2 anchor = new Vector2(Main.screenWidth / 2, bottom);
        RemoveAllChildren();
        _current.Clear();
        foreach (IInGameNotification notification in InGameNotificationsTracker._notifications)
        {
            _current.Add(notification);
            UINotificationPopup popup = PopupFor(notification);
            if (popup.Place(anchor))
            {
                Append(popup);
            }
            notification.PushAnchor(ref anchor);
            if (anchor.Y < -100f)
            {
                break;
            }
        }
        if (_popups.Count > _current.Count)
        {
            _gone.Clear();
            foreach (IInGameNotification notification in _popups.Keys)
            {
                if (!_current.Contains(notification))
                {
                    _gone.Add(notification);
                }
            }
            foreach (IInGameNotification notification in _gone)
            {
                _popups.Remove(notification);
            }
        }
        Recalculate();
    }

    private UINotificationPopup PopupFor(IInGameNotification notification)
    {
        if (!_popups.TryGetValue(notification, out var popup))
        {
            popup = notification switch
            {
                InGamePopups.AchievementUnlockedPopup => new UIAchievementPopup(),
                InGamePopups.PlayerWantsToJoinGamePopup => new UIJoinRequestPopup(),
                _ => new UILegacyNotification()
            };
            popup.Notification = notification;
            _popups[notification] = popup;
        }
        return popup;
    }
}
