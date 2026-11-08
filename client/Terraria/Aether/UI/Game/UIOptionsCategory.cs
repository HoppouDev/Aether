using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI;

namespace Aether.UI.Game;

/// <summary>
/// One entry of the in-game settings' left column, like IngameOptions.DrawLeftSide: big outlined text
/// that grows and brightens while hovered or selected, gold when it's the open category.
/// </summary>
public class UIOptionsCategory : UIElement
{
    private const float Speed = 0.01f;

    private readonly string _text;

    internal override string AutomationLabel => _text;

    private readonly Action _onClick;

    private readonly Func<bool> _isSelected;

    private readonly Vector2 _center;

    private readonly float _minScale;

    private readonly float _maxScale;

    private float _scale;

    public UIOptionsCategory(string text, Action onClick, Func<bool> isSelected, Vector2 center, float minScale, float maxScale)
    {
        _text = text;
        _onClick = onClick;
        _isSelected = isSelected;
        _center = center;
        _minScale = minScale;
        _maxScale = maxScale;
        _scale = minScale;
    }

    private bool IsSelected
    {
        get
        {
            if (_isSelected != null)
            {
                return _isSelected();
            }
            return false;
        }
    }

    private void UpdateSize()
    {
        Vector2 size = FontAssets.DeathText.Value.MeasureString(_text) * _scale;
        Left.Set(_center.X - size.X / 2f - Parent.GetInnerDimensions().X, 0f);
        Top.Set(_center.Y - size.Y / 2f - Parent.GetInnerDimensions().Y, 0f);
        Width.Set(size.X, 0f);
        Height.Set(size.Y, 0f);
        Recalculate();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _scale = MathHelper.Clamp(_scale + ((IsMouseHovering || IsSelected) ? Speed : (0f - Speed)), _minScale, _maxScale);
        UpdateSize();
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        SoundEngine.PlaySound(12);
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        _onClick();
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Color color = IsSelected ? Color.Gold : Color.Lerp(Color.Gray, Color.White, (_scale - _minScale) / (_maxScale - _minScale));
        Utils.DrawBorderStringBig(spriteBatch, _text, _center, color, _scale, 0.5f, 0.5f);
    }
}
