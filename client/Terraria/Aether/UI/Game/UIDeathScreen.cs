using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Chat;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Chat;

namespace Aether.UI.Game;

/// <summary>A line of text drawn with SpriteBatch.DrawString at a position, scale and colour set by its screen.</summary>
public class UIPlacedText : UIElement
{
    public DynamicSpriteFont Font;

    public string Text;

    public Vector2 Position;

    public Color Color;

    public float Scale = 1f;

    public UIPlacedText()
    {
        IgnoresMouseInteraction = true;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        if (Text != null)
        {
            spriteBatch.DrawString(Font, Text, Position, Color, 0f, default(Vector2), Scale, SpriteEffects.None, 0f);
        }
    }
}

/// <summary>
/// The respawn skip prompt ("Press [jump] to respawn", with the button's glyph), which fades in and
/// shrinks into place once the timer can be skipped.
/// </summary>
public class UIRespawnSkipPrompt : UIElement
{
    public bool Shown;

    public float TextTop;

    public float Fade;

    public float Grow;

    public UIRespawnSkipPrompt()
    {
        IgnoresMouseInteraction = true;
    }

    // The prompt parsed and laid out once: plain parts get a sentinel colour at parse time, and are
    // given the fading colour every frame (the same as parsing with that colour, without the parse).
    private static readonly Color Sentinel = new Color(1, 2, 3, 4);

    private string _text;

    private TextSnippet[] _snippets;

    private Vector2 _size;

    private List<PositionedSnippet> _layout;

    private bool[] _layoutInherits;

    private Vector2 _layoutScale;

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        if (!Shown)
        {
            return;
        }
        const float scaleBase = 0.7f;
        DynamicSpriteFont font = FontAssets.DeathText.Value;
        Color color = Main.LocalPlayer.GetDeathAlpha(Color.Transparent) * Fade;
        string text = Language.GetTextValue("Game.RespawnSkip");
        float glyphScale = 1.75f;
        Utils.Swap(ref GlyphTagHandler.GlyphsScale, ref glyphScale);
        if (text != _text)
        {
            _text = text;
            _snippets = ChatManager.ParseMessage(text, Sentinel).ToArray();
            foreach (TextSnippet snippet in _snippets)
            {
                snippet.UseRawColor = true;
            }
            _size = ChatManager.GetStringSize(font, _snippets, Vector2.One);
            _layout = null;
        }
        float scale = (1f + Grow) * scaleBase;
        Vector2 scaleVector = new Vector2(scale, scale);
        if (_layout == null || scaleVector != _layoutScale)
        {
            Relayout(font, scaleVector);
        }
        for (int i = 0; i < _layout.Count; i++)
        {
            if (_layoutInherits[i])
            {
                _layout[i].Snippet.Color = color;
            }
        }
        GlyphTagHandler.GlyphsOffset = new Vector2(0f, 6f * scale);
        Vector2 position = new Vector2((float)(Main.screenWidth / 2) - _size.X * 0.5f * scale, (float)(Main.screenHeight / 2) + TextTop + _size.Y * 0.5f * scaleBase);
        // ChatManager.DrawColorCodedString(spriteBatch, font, snippets, position, 0f, Vector2.Zero, scale, out _).
        float uniqueScale = Math.Min(scaleVector.X, scaleVector.Y);
        for (int i = 0; i < _layout.Count; i++)
        {
            PositionedSnippet positioned = _layout[i];
            Vector2 snippetPosition = position + positioned.Position;
            TextSnippet snippet = positioned.Snippet;
            Color snippetColor = snippet.GetVisibleColor();
            if (!snippet.UniqueDraw(justCheckingSize: false, out var _, spriteBatch, snippetPosition, snippetColor, uniqueScale))
            {
                spriteBatch.DrawString(font, snippet.Text, snippetPosition, snippetColor, 0f, Vector2.Zero, scaleVector, SpriteEffects.None, 0f);
            }
        }
        Utils.Swap(ref GlyphTagHandler.GlyphsScale, ref glyphScale);
        GlyphTagHandler.GlyphsOffset = Vector2.Zero;
    }

    /// <summary>Lays the snippets out at scale (it shrinks into place once, then stays).</summary>
    private void Relayout(DynamicSpriteFont font, Vector2 scale)
    {
        // Snippets drawn uniquely (glyphs) are laid out as themselves: put back the sentinel the
        // last frame's colour replaced, so it's copied into the new layout's text parts.
        if (_layout != null)
        {
            for (int i = 0; i < _layout.Count; i++)
            {
                if (_layoutInherits[i])
                {
                    _layout[i].Snippet.Color = Sentinel;
                }
            }
        }
        _layoutScale = scale;
        _layout = ChatManager.LayoutSnippets(font, _snippets, scale).ToList();
        _layoutInherits = new bool[_layout.Count];
        for (int i = 0; i < _layout.Count; i++)
        {
            _layoutInherits[i] = _layout[i].Snippet.Color == Sentinel;
        }
    }
}

/// <summary>
/// What's shown while the player is dead (Main.DrawInterface_35_YouDied): "You were slain", the coins
/// dropped, the respawn skip prompt (jump skips the timer when allowed) and the respawn countdown, all
/// centred and fading with the death alpha; spectating shows only the prompt and countdown.
/// </summary>
public class UIDeathScreen : UIGameScreen
{
    public static readonly UIDeathScreen Instance = new UIDeathScreen();

    private UIPlacedText _title;

    private UIPlacedText _coins;

    private UIRespawnSkipPrompt _skip;

    private UIPlacedText _countdown;

    // The coins and countdown lines, made again only when they change.
    private string _coinsFor;

    private GameCulture _coinsIn;

    private string _coinsText;

    private int _countdownSeconds = -1;

    private GameCulture _countdownIn;

    private string _countdownText;

    public override void OnInitialize()
    {
        _title = new UIPlacedText();
        _coins = new UIPlacedText();
        _skip = new UIRespawnSkipPrompt();
        _countdown = new UIPlacedText();
        Append(_title);
        Append(_coins);
        Append(_skip);
        Append(_countdown);
    }

    /// <summary>Called by Main.DrawInterface_35_YouDied in place of its body; false: run that.</summary>
    public static bool Draw()
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        if (Main.LocalPlayer.dead)
        {
            GameScreens.Draw(Instance, Main.spriteBatch);
            if (PlayerInput.SettingsForUI.ShowGamepadHints)
            {
                Main.DrawGamepadInstructions();
            }
        }
        return true;
    }

    public override void UpdateState()
    {
        Player player = Main.LocalPlayer;
        Color color = player.GetDeathAlpha(Color.Transparent);
        float y = -60f;
        _title.Text = null;
        _coins.Text = null;
        _countdown.Text = null;
        if (player.spectating < 0)
        {
            Place(_title, FontAssets.DeathText.Value, Lang.inter[38].Value, y, color, 1f);
            if (player.lostCoins > 0)
            {
                y += 50f;
                if ((object)player.lostCoinString != _coinsFor || Language.ActiveCulture != _coinsIn)
                {
                    _coinsFor = player.lostCoinString;
                    _coinsIn = Language.ActiveCulture;
                    _coinsText = Language.GetTextValue("Game.DroppedCoins", player.lostCoinString);
                }
                Place(_coins, FontAssets.MouseText.Value, _coinsText, y, color, 1f);
                y -= 26f;
            }
        }
        else
        {
            y += 24f;
        }
        bool skipPressed = Utils.JustBecameTrue(PlayerInput.Triggers.Current.Jump, ref Main._respawnSkipButtonClickable);
        _skip.Shown = false;
        if (player.AllowsRespawnTimerSkip())
        {
            Main._framesWithRespawnSkipAvailable++;
            float fade = Utils.Remap(Main._framesWithRespawnSkipAvailable, 0f, 60f, 0f, 1f);
            float grow = Utils.Remap(Main._framesWithRespawnSkipAvailable, 0f, 30f, 0.5f, 0f);
            grow *= grow;
            y += Utils.Remap(fade, 0f, 1f, 70f, 20f) * fade;
            _skip.Shown = true;
            _skip.TextTop = y;
            _skip.Fade = fade;
            _skip.Grow = grow;
            if (skipPressed)
            {
                player.releaseJump = false;
                player.SkipRespawnTime();
            }
        }
        else
        {
            Main._framesWithRespawnSkipAvailable = 0;
        }
        if (player.respawnTimer > 1)
        {
            y += 70f;
            int seconds = (int)(1f + (float)player.respawnTimer / 60f);
            if (seconds != _countdownSeconds || Language.ActiveCulture != _countdownIn)
            {
                _countdownSeconds = seconds;
                _countdownIn = Language.ActiveCulture;
                _countdownText = Language.GetTextValue("Game.RespawnInSuffix", ((float)seconds).ToString());
            }
            Place(_countdown, FontAssets.DeathText.Value, _countdownText, y, color, 0.7f);
        }
    }

    /// <summary>Centres a line horizontally, y below the middle of the screen.</summary>
    private static void Place(UIPlacedText line, DynamicSpriteFont font, string text, float y, Color color, float scale)
    {
        line.Font = font;
        line.Text = text;
        line.Color = color;
        line.Scale = scale;
        float width = font.MeasureString(text).X;
        line.Position = new Vector2((float)(Main.screenWidth / 2) - width * scale / 2f, (float)(Main.screenHeight / 2) + y);
    }
}
