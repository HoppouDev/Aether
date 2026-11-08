using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Menu;

/// <summary>
/// The legacy HSL(+alpha) colour bars for <c>Main.selColor</c>. Like DrawMenu it keeps the colour in
/// <c>Main.hBar</c>/<c>sBar</c>/<c>lBar</c>/<c>aBar</c>, which the gamepad also adjusts
/// (UILinksInitializer.HandleOptionsSpecials, features 5 to 8).
/// </summary>
public class UIMenuColorBars : UIElement
{
    private const int Blips = 167;

    private const float RowSpacing = 26f;

    private readonly bool _withAlpha;

    private readonly float _minLuminosity;

    // Which bar is being dragged (1-based like DrawMenu's grabColorSlider), and whether the mouse has
    // been released since the screen opened (so the click that opened it doesn't grab a bar).
    private int _grabbed;

    private bool _armed;

    private int _focused;

    private int Rows => _withAlpha ? 4 : 3;

    /// <summary>In game: the first of the bars' gamepad link points (the title menu links them itself).</summary>
    public int LinkPointBase = -1;

    public UIMenuColorBars(bool withAlpha, float minLuminosity)
    {
        _withAlpha = withAlpha;
        _minLuminosity = minLuminosity;
        Width.Set(TextureAssets.Hue.Width(), 0f);
        Height.Set(RowSpacing * (Rows - 1) + TextureAssets.Hue.Height(), 0f);
        HAlign = 0.5f;
    }

    /// <summary>Starts editing <c>Main.selColor</c>.</summary>
    public void Load()
    {
        Vector3 hsl = Main.rgbToHsl(Main.selColor);
        Main.hBar = hsl.X;
        Main.sBar = hsl.Y;
        Main.lBar = hsl.Z;
        Main.aBar = Main.selColor.A / 255f;
        _grabbed = 0;
        _armed = false;
    }

    private bool IsOverRow(int row)
    {
        CalculatedStyle dimensions = GetDimensions();
        float y = dimensions.Y + RowSpacing * row;
        Vector2 mouse = Main.MouseScreen;
        return mouse.X > dimensions.X - 4f && mouse.X < dimensions.X + dimensions.Width + 4f && mouse.Y > y - 4f && mouse.Y < y + TextureAssets.Hue.Height() + 4f;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (!Main.mouseLeft)
        {
            _grabbed = 0;
            _armed = true;
        }
        int focused = 0;
        for (int row = 0; row < Rows; row++)
        {
            if ((_grabbed == 0 && IsOverRow(row)) || _grabbed == row + 1)
            {
                focused = row + 1;
                break;
            }
        }
        if (focused != _focused)
        {
            SoundEngine.PlaySound(12);
            _focused = focused;
        }
        if (focused != 0)
        {
            UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE = 4 + focused;
            if (Main.mouseLeft && !PlayerInput.UsingGamepad && _armed)
            {
                _grabbed = focused;
                CalculatedStyle dimensions = GetDimensions();
                float value = MathHelper.Clamp((Main.MouseScreen.X - dimensions.X) / dimensions.Width, 0f, 1f);
                switch (focused)
                {
                case 1:
                    Main.hBar = value;
                    break;
                case 2:
                    Main.sBar = value;
                    break;
                case 3:
                    Main.lBar = value * (1f - _minLuminosity) + _minLuminosity;
                    break;
                default:
                    Main.aBar = value;
                    break;
                }
            }
        }
        Main.selColor = Main.hslToRgb(Main.hBar, Main.sBar, Main.lBar);
        if (_withAlpha)
        {
            Main.selColor.A = (byte)(Main.aBar * 255f);
        }
    }

    /// <summary>Where a bar's slider sits, 0 to 1.</summary>
    private float BarValue(int row)
    {
        return row switch
        {
            0 => Main.hBar,
            1 => Main.sBar,
            2 => (Main.lBar - _minLuminosity) / (1f - _minLuminosity),
            _ => Main.aBar
        };
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        CalculatedStyle dimensions = GetDimensions();
        Color color = Main.hslToRgb(Main.hBar, Main.sBar, Main.lBar);
        for (int row = 0; row < Rows; row++)
        {
            Vector2 position = new Vector2(dimensions.X, dimensions.Y + RowSpacing * row);
            if (row == 0)
            {
                spriteBatch.Draw(TextureAssets.Hue.Value, position, Color.White);
            }
            else
            {
                spriteBatch.Draw(TextureAssets.ColorBar.Value, position, Color.White);
                for (int i = 0; i <= Blips; i++)
                {
                    float t = (float)i / (float)Blips;
                    Color blip = row switch
                    {
                        1 => Main.hslToRgb(Main.hBar, t, Main.lBar),
                        2 => Main.hslToRgb(Main.hBar, Main.sBar, t),
                        _ => color * t
                    };
                    spriteBatch.Draw(TextureAssets.ColorBlip.Value, position + new Vector2(i + 5, 4f), blip);
                }
            }
            if (_focused == row + 1)
            {
                spriteBatch.Draw(TextureAssets.ColorHighlight.Value, position, Main.OurFavoriteColor);
            }
            spriteBatch.Draw(TextureAssets.ColorSlider.Value, new Vector2(position.X + (TextureAssets.Hue.Width() - 2) * BarValue(row) - TextureAssets.ColorSlider.Width() / 2, position.Y - TextureAssets.ColorSlider.Height() / 2 + TextureAssets.Hue.Height() / 2), Color.White);
            if (LinkPointBase >= 0)
            {
                UILinkPointNavigator.SetPosition(LinkPointBase + row, position + TextureAssets.ColorBar.Value.Size() / 2f);
            }
            else
            {
                MenuScreens.AddGamepadPoint(position + TextureAssets.ColorBar.Value.Size() / 2f);
            }
        }
    }
}
