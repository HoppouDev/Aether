using System;
using Aether.UI.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
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
/// The dresser's clothes window (Main.DrawClothesWindow): pick a part (shirt, undershirt, pants, shoes,
/// eyes, skin) and set its colour with the HSL bars; click the preview to change clothing style, the
/// voice button to change voice, and drag the pitch bar; then Change or Cancel.
/// </summary>
public class UIDresserWindow : UIGameScreen
{
    public static readonly UIDresserWindow Instance = new UIDresserWindow();

    private const int PanelWidth = 462;

    private const int PanelHeight = 163;

    private const int PartsWidth = 180;

    private Point _origin;

    private UIMenuColorBars _bars;

    private UIWindowChoice _change;

    private UIWindowChoice _cancel;

    private bool _pitchGrabbed;

    /// <summary>Called by Main.DrawClothesWindow in place of its body (after the window-closing checks).</summary>
    public static bool Draw()
    {
        InGamePreview.Update();
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    public override void OnInitialize()
    {
        _bars = new UIMenuColorBars(withAlpha: false, 0.15f)
        {
            HAlign = 0f,
            LinkPointBase = 2800
        };
        Append(_bars);
        _change = new UIWindowChoice(() => Main.npcChatFocus1, delegate(bool focus)
        {
            Main.npcChatFocus1 = focus;
        }, 2803);
        _cancel = new UIWindowChoice(() => Main.npcChatFocus2, delegate(bool focus)
        {
            Main.npcChatFocus2 = focus;
        }, 2804);
        Append(_change);
        Append(_cancel);
    }

    private static Color PartColor(int part)
    {
        Player player = Main.LocalPlayer;
        return part switch
        {
            0 => player.shirtColor,
            1 => player.underShirtColor,
            2 => player.pantsColor,
            3 => player.shoeColor,
            4 => player.eyeColor,
            _ => player.skinColor
        };
    }

    private static void SetPartColor(int part, Color color)
    {
        Player player = Main.LocalPlayer;
        switch (part)
        {
        case 0:
            player.shirtColor = color;
            break;
        case 1:
            player.underShirtColor = color;
            break;
        case 2:
            player.pantsColor = color;
            break;
        case 3:
            player.shoeColor = color;
            break;
        case 4:
            player.eyeColor = color;
            break;
        default:
            player.skinColor = color;
            break;
        }
    }

    private static bool Changed()
    {
        Player player = Main.LocalPlayer;
        for (int i = 0; i < 6; i++)
        {
            if (Main.oldClothesColor[i] != PartColor(i))
            {
                return true;
            }
        }
        if (Main.oldVoiceStyle == player.voiceVariant && Main.oldClothesStyle == player.skinVariant)
        {
            return Main.oldVoicePitch != player.voicePitchOffset;
        }
        return true;
    }

    public override void UpdateState()
    {
        _origin = new Point(Main.screenWidth / 2 - PanelWidth / 2, Main.screenHeight / 2 + 60);
        Main.selColor = PartColor(Main.selClothes);
        // Picking a part resets the bars to -1; reload them then.
        if (Main.hBar == -1f || Main.sBar == -1f || Main.lBar == -1f)
        {
            _bars.Load();
        }
        _bars.Left.Set(_origin.X + 22, 0f);
        _bars.Top.Set(_origin.Y + 16, 0f);
        _bars.Recalculate();
        if (!PlayerInput.IgnoreMouseInterface && Main.mouseX > _origin.X && Main.mouseX < _origin.X + PanelWidth && Main.mouseY > _origin.Y && Main.mouseY < _origin.Y + PanelHeight)
        {
            Main.LocalPlayer.mouseInterface = true;
        }
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Utils.DrawInvBG(spriteBatch, new Rectangle(_origin.X, _origin.Y, PanelWidth, PanelHeight));
    }

    protected override void DrawChildren(SpriteBatch spriteBatch)
    {
        Player dummy = Main.dresserInterfaceDummy;
        Vector2 feet = new Vector2(_origin.X + PanelWidth / 2 - 44 + 58, _origin.Y + 66 + dummy.height / 2 - 20 + 18);
        Rectangle preview = new Rectangle((int)feet.X - dummy.width / 2 - 15, (int)feet.Y - dummy.height - 33, dummy.width + 30, dummy.height + 66 - 50);
        feet.Y -= 22f;
        bool previewHovered = preview.Contains(Main.MouseScreen.ToPoint());
        int selected = Main.selClothes;
        int grey = (Main.mouseTextColor * 2 + 255) / 3;
        Color color = new Color(grey, (int)((double)grey / 1.1), grey / 2, grey);
        bool changed = Changed();
        Vector2 changeAt = new Vector2(_origin.X + 22, _origin.Y + 124);
        _change.DrawChoice(spriteBatch, Language.GetTextValue("GameUI.Change"), changeAt, changed ? color : new Color(100, 100, 100), changed);
        _cancel.DrawChoice(spriteBatch, Language.GetTextValue("GameUI.Cancel"), changeAt + new Vector2(130f, 0f), color, enabled: true);
        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            if (Main.npcChatFocus1)
            {
                Main.SaveClothesWindow();
                return;
            }
            if (Main.npcChatFocus2)
            {
                Main.CancelClothesWindow();
                return;
            }
        }
        if (!Main.mouseLeft)
        {
            Main.blockMouse = false;
            _pitchGrabbed = false;
        }
        _bars.Draw(spriteBatch);
        SetPartColor(Main.selClothes, Main.selColor);
        DrawParts(spriteBatch, selected);
        DrawPreview(spriteBatch, preview, feet, previewHovered);
        DrawVoice(spriteBatch, preview);
    }

    // The six parts in a 3×2 block: shirt and undershirt, pants and shoes, eyes and skin.
    private void DrawParts(SpriteBatch spriteBatch, int selected)
    {
        Texture2D slot = TextureAssets.InventoryBack.Value;
        Vector2 position = new Vector2(_origin.X + PanelWidth - PartsWidth, _origin.Y + 10);
        for (int part = 0; part < 6; part++)
        {
            spriteBatch.Draw((selected == part) ? TextureAssets.InventoryBack14.Value : TextureAssets.InventoryBack8.Value, position, new Rectangle(0, 0, slot.Width, slot.Height), new Color(200, 200, 200, 200));
            if (Main.mouseX > position.X && Main.mouseX < position.X + slot.Width && Main.mouseY > position.Y && Main.mouseY < position.Y + slot.Height && Main.mouseLeft && Main.mouseLeftRelease)
            {
                Main.mouseLeftRelease = false;
                Main.selClothes = part;
                SoundEngine.PlaySound(12);
                Main.hBar = -1f;
                Main.lBar = -1f;
                Main.sBar = -1f;
            }
            Texture2D icon = TextureAssets.Clothes[part].Value;
            Vector2 iconAt = new Vector2(position.X + slot.Width / 2 - icon.Width / 2, position.Y + slot.Height / 2 - icon.Height / 2);
            spriteBatch.Draw(icon, iconAt, PartColor(part));
            if (part == 4)
            {
                Texture2D eyeBack = Main.Assets.Request<Texture2D>("Images/UI/CharCreation/ColorEyeBack").Value;
                spriteBatch.Draw(eyeBack, iconAt, eyeBack.Frame(), Color.White);
            }
            UILinkPointNavigator.SetPosition(2806 + part, iconAt + icon.Size() * 0.75f);
            position.X += 56f;
            switch (part)
            {
            case 1:
                position += new Vector2(-112f, 56f);
                break;
            case 3:
                position.Y -= 56f;
                break;
            case 4:
                position += new Vector2(-56f, 56f);
                break;
            }
        }
    }

    // The player as they'd look, on a panel that cycles the clothing style when clicked.
    private static void DrawPreview(SpriteBatch spriteBatch, Rectangle panel, Vector2 feet, bool hovered)
    {
        Player player = Main.LocalPlayer;
        Player dummy = Main.dresserInterfaceDummy;
        dummy.skinVariant = player.skinVariant;
        dummy.eyeColor = player.eyeColor;
        dummy.hairColor = player.hairColor;
        dummy.hair = player.hair;
        dummy.skinColor = player.skinColor;
        dummy.shirtColor = player.shirtColor;
        dummy.underShirtColor = player.underShirtColor;
        dummy.shoeColor = player.shoeColor;
        dummy.pantsColor = player.pantsColor;
        dummy.Bottom = Main.screenPosition + feet;
        dummy.direction = -1;
        dummy.gravDir = 1f;
        dummy.PlayerFrame();
        dummy.socialIgnoreLight = true;
        Color back = new Color(46, 106, 98) * 0.6f;
        if (hovered)
        {
            back = new Color(46, 106, 98) * 2f * 0.6f;
            if (Main.mouseLeft && Main.mouseLeftRelease)
            {
                Main.mouseLeftRelease = false;
                SoundEngine.PlaySound(12);
                Main.CycleClothingStyle(player);
            }
        }
        UILinkPointNavigator.SetPosition(2805, panel.Center.ToVector2());
        Utils.DrawInvBG(spriteBatch, panel, back);
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, null, null, Main.UIScaleMatrix);
        Main.PlayerRenderer.DrawPlayer(Main.Camera, dummy, dummy.position, dummy.fullRotation, dummy.fullRotationOrigin);
    }

    // Plays a hurt sound in the player's (new) voice, from the preview dummy.
    private static void SampleVoice()
    {
        Player player = Main.LocalPlayer;
        Player dummy = Main.dresserInterfaceDummy;
        int voice = dummy.voiceVariant;
        Vector2 position = dummy.position;
        dummy.voiceVariant = player.voiceVariant;
        dummy.voicePitchOffset = player.voicePitchOffset;
        dummy.position = new Vector2(-1f, -1f);
        dummy.PlayHurtSound();
        dummy.position = position;
        dummy.voiceVariant = voice;
        // (Restoring the pitch from the variant is what the legacy code does.)
        dummy.voicePitchOffset = voice;
    }

    // Under the preview: a voice button, and beside the colour bars a pitch bar in 21 steps.
    private void DrawVoice(SpriteBatch spriteBatch, Rectangle preview)
    {
        Player player = Main.LocalPlayer;
        Rectangle button = preview;
        button.Y += button.Height + 4;
        button.Height = 46;
        UILinkPointNavigator.SetPosition(2813, button.Center.ToVector2());
        Color back = new Color(46, 106, 98) * 0.6f;
        if (button.Contains(Main.MouseScreen.ToPoint()))
        {
            back = new Color(46, 106, 98) * 2f * 0.6f;
            if (Main.mouseLeft && Main.mouseLeftRelease)
            {
                Main.mouseLeftRelease = false;
                Main.CycleVoiceStyle(player, 1);
                SampleVoice();
            }
        }
        Utils.DrawInvBG(spriteBatch, button, back);
        Asset<Texture2D> voiceIcon = Main.Assets.Request<Texture2D>("Images/UI/CharCreation/Voice");
        spriteBatch.Draw(voiceIcon.Value, button.Center.ToVector2(), null, PlayerVoiceID.Sets.Colors[player.voiceVariant], 0f, voiceIcon.Size() / 2f, 1f, SpriteEffects.None, 0f);
        Vector2 bar = new Vector2(_origin.X + 22, _origin.Y + 16 + 52 + 30);
        Texture2D hue = TextureAssets.Hue.Value;
        spriteBatch.Draw(TextureAssets.ColorBar.Value, bar, Color.White);
        for (int i = 0; i <= 167; i++)
        {
            float t = ((float)i / 167f * 4f + 0.5f) % 1f;
            float peak = Utils.Remap(t, 0f, 0.5f, 0f, 1f) * Utils.Remap(t, 0.5f, 1f, 1f, 0f);
            spriteBatch.Draw(TextureAssets.ColorBlip.Value, bar + new Vector2(i + 5, 4f), Color.Lerp(new Color(90, 90, 120), Color.White, peak * peak * peak * peak * peak));
        }
        bool hovered = (Main.mouseX > bar.X - 4f && Main.mouseX < bar.X + hue.Width + 4f && Main.mouseY > bar.Y - 4f && Main.mouseY < bar.Y + hue.Height + 4f) || _pitchGrabbed;
        if (hovered)
        {
            spriteBatch.Draw(TextureAssets.ColorHighlight.Value, bar, Main.OurFavoriteColor);
        }
        float value = Utils.Remap(player.voicePitchOffset, -1f, 1f, 0f, 1f);
        spriteBatch.Draw(TextureAssets.ColorSlider.Value, new Vector2(bar.X + (float)(hue.Width - 2) * value - (float)(TextureAssets.ColorSlider.Width() / 2), bar.Y - (float)(TextureAssets.ColorSlider.Height() / 2) + (float)(hue.Height / 2)), Color.White);
        if (hovered && Main.mouseLeft && !Main.blockMouse)
        {
            bool grabbed = _pitchGrabbed;
            _pitchGrabbed = true;
            float pitch = MathHelper.Clamp((Main.mouseX - bar.X) / (float)hue.Width, 0f, 1f);
            float old = player.voicePitchOffset;
            player.voicePitchOffset = Utils.Remap((float)Math.Round(pitch * 20f) / 20f, 0f, 1f, -1f, 1f);
            if (player.voicePitchOffset != old || !grabbed)
            {
                SampleVoice();
            }
        }
        UILinkPointNavigator.SetPosition(2812, bar + hue.Size() / 2f);
    }
}
