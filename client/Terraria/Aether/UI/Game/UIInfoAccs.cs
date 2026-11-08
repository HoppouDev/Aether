using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Game;

/// <summary>
/// The info accessory readouts (Main.DrawInfoAccs): one row per equipped info accessory (watch, weather
/// radio, depth meter, ...), each an icon and its text; with the inventory open the icons line up and a
/// click hides or shows that row. Main.DrawInfoAccs still works out each row's text and hands the rows
/// over (<see cref="Collect" />); this draws them and handles the mouse.
/// </summary>
public class UIInfoAccs : UIGameScreen
{
    public static readonly UIInfoAccs Instance = new UIInfoAccs();

    private readonly List<UIInfoAccRow> _rows = new List<UIInfoAccRow>();

    private int _count;

    private int _startX;

    private float _maxWidth;

    /// <summary>Called by Main.DrawInfoAccs before its rows: starts a new set (false: legacy drawing).</summary>
    public static bool Begin()
    {
        InGamePreview.Update();
        Instance._count = 0;
        return UIInventorySlots.Enabled;
    }

    /// <summary>Called by Main.DrawInfoAccs for each row that has text, in place of drawing it.</summary>
    public static bool Collect(int line, int icon, string text, string name, Color textColor, Color shadowColor)
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        if (Instance._count >= Instance._rows.Count)
        {
            UIInfoAccRow row = new UIInfoAccRow();
            Instance._rows.Add(row);
            Instance.Append(row);
        }
        Instance._rows[Instance._count++].Set(line, icon, text, name, textColor, shadowColor);
        return true;
    }

    /// <summary>Called by Main.DrawInfoAccs after its rows, in place of its mouse text and hover sound.</summary>
    public static bool Draw(int startX, float maxWidth)
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        Instance._startX = startX;
        Instance._maxWidth = maxWidth;
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    protected override void DrawChildren(SpriteBatch spriteBatch)
    {
        int drawn = 0;
        int hoveredLine = -10;
        int state = -1;
        string hoverText = "";
        for (int i = 0; i < _count; i++)
        {
            _rows[i].DrawRow(spriteBatch, _startX, _maxWidth, ref drawn, ref hoveredLine, ref state, ref hoverText);
        }
        if (!string.IsNullOrEmpty(hoverText))
        {
            if (Main.playerInventory)
            {
                Main.LocalPlayer.mouseInterface = true;
            }
            Main.instance.MouseTextNoOverride(hoverText, 0, 0);
        }
        Main.DoStatefulTickSound(ref Main.infoAccMouseOver, state);
    }
}

/// <summary>One info accessory row: its icon (clickable in the inventory) and, outside it, its text.</summary>
public class UIInfoAccRow : UIElement
{
    private int _line;

    private int _icon;

    private string _text;

    private string _name;

    private Color _textColor;

    private Color _shadowColor;

    public UIInfoAccRow()
    {
        IgnoresMouseInteraction = true;
    }

    public void Set(int line, int icon, string text, string name, Color textColor, Color shadowColor)
    {
        _line = line;
        _icon = icon;
        _text = text;
        _name = name;
        _textColor = textColor;
        _shadowColor = shadowColor;
    }

    public void DrawRow(SpriteBatch spriteBatch, int startX, float maxWidth, ref int drawn, ref int hoveredLine, ref int state, ref string hoverText)
    {
        Player player = Main.LocalPlayer;
        Main.GetInfoAccIconPosition(drawn, startX, out var x, out var y);
        int rowStep = (Main.screenHeight < 650) ? 20 : 22;
        if (_icon >= 0)
        {
            drawn++;
            Texture2D icon = TextureAssets.InfoIcon[_icon].Value;
            Vector2 position = Main.playerInventory ? new Vector2(x, y) : new Vector2(x, y + 74 + rowStep * _line + 52);
            int hideIndex = (_icon == 8) ? 7 : _icon;
            bool inside = (float)Main.mouseX >= position.X && (float)Main.mouseY >= position.Y && (float)Main.mouseX <= position.X + (float)icon.Width && (float)Main.mouseY <= position.Y + (float)icon.Height;
            Color color = Color.White;
            bool lit = false;
            if (Main.playerInventory)
            {
                if (inside)
                {
                    state = drawn;
                    if (!PlayerInput.IgnoreMouseInterface)
                    {
                        lit = true;
                        player.mouseInterface = true;
                        if (Main.mouseLeft && Main.mouseLeftRelease)
                        {
                            SoundEngine.PlaySound(12);
                            Main.mouseLeftRelease = false;
                            player.hideInfo[hideIndex] = !player.hideInfo[hideIndex];
                        }
                        if (!Main.mouseText)
                        {
                            hoverText = _name;
                            Main.mouseText = true;
                        }
                    }
                }
                if (player.hideInfo[hideIndex])
                {
                    color = new Color(80, 80, 80, 70);
                }
            }
            else if (inside && !Main.mouseText)
            {
                hoveredLine = _line;
                hoverText = _name;
                Main.mouseText = true;
            }
            UILinkPointNavigator.SetPosition(1558 + drawn - 1, position + icon.Size() * 0.75f);
            spriteBatch.Draw(icon, position, color);
            if (lit)
            {
                spriteBatch.Draw(TextureAssets.InfoIcon[13].Value, position - Vector2.One * 2f, Main.OurFavoriteColor);
            }
            x += 20;
        }
        UILinkPointNavigator.Shortcuts.INFOACCCOUNT = drawn;
        if (Main.playerInventory)
        {
            return;
        }
        // Long texts are squeezed to fit (and made a little taller to stay readable).
        DynamicSpriteFont font = FontAssets.MouseText.Value;
        Vector2 scale = Vector2.One;
        Vector2 size = font.MeasureString(_text);
        if (size.X > maxWidth)
        {
            scale.X = maxWidth / size.X;
        }
        if (scale.X < 0.58f)
        {
            scale.Y = 1f - scale.X / 3f;
        }
        for (int i = 0; i < 5; i++)
        {
            Vector2 offset = i switch
            {
                0 => new Vector2(-2f, 0f),
                1 => new Vector2(2f, 0f),
                2 => new Vector2(0f, -2f),
                3 => new Vector2(0f, 2f),
                _ => Vector2.Zero
            };
            Color color = (i == 4) ? _textColor : _shadowColor;
            // The line under a hovered icon is dimmed, so the mouse text over it stays readable.
            if (_line > hoveredLine && _line < hoveredLine + 2)
            {
                color = new Color(color.R / 3, color.G / 3, color.B / 3, color.A / 3);
            }
            spriteBatch.DrawString(font, _text, new Vector2(x, y + 74 + rowStep * _line + 48) + offset, color, 0f, default(Vector2), scale, SpriteEffects.None, 0f);
        }
    }
}
