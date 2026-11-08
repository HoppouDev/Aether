using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.UI.Chat;

namespace Aether.UI;

/// <summary>
/// One chat-formatted string ([c/..:..], [i:..] and other tags), drawn and measured like ChatManager
/// does it but with the parsing and layout kept until the text, font, colour or scale changes, so a
/// screen drawing the same text every frame doesn't allocate. Keep one per piece of text drawn.
/// </summary>
public sealed class ChatText
{
    private string _text;

    private DynamicSpriteFont _font;

    private Color _baseColor;

    private Vector2 _scale;

    private float _maxWidth;

    private bool _plain;

    private List<PositionedSnippet> _snippets;

    private string _measuredText;

    private DynamicSpriteFont _measuredFont;

    private Vector2 _measuredScale;

    private float _measuredMaxWidth;

    private Vector2 _size;

    /// <summary>ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, text, ...), the same draws.</summary>
    public void DrawWithShadow(SpriteBatch spriteBatch, DynamicSpriteFont font, string text, Vector2 position, Color baseColor, float rotation, Vector2 origin, Vector2 scale, float maxWidth = -1f, float spread = 2f)
    {
        Prepare(font, text, baseColor, scale, maxWidth);
        Color shadowColor = baseColor.MultiplyRGBA(Color.Black);
        Vector2[] directions = ChatManager.ShadowDirections;
        if (_plain)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                spriteBatch.DrawString(font, text, position + directions[i] * spread, shadowColor, rotation, origin, scale, SpriteEffects.None, 0f);
            }
            spriteBatch.DrawString(font, text, position, baseColor, rotation, origin, scale, SpriteEffects.None, 0f);
            return;
        }
        for (int i = 0; i < directions.Length; i++)
        {
            DrawSnippets(spriteBatch, font, position + directions[i] * spread, rotation, origin, scale, shadowColor);
        }
        DrawSnippets(spriteBatch, font, position, rotation, origin, scale, null);
    }

    /// <summary>ChatManager.GetStringSize(font, text, scale, maxWidth).</summary>
    public Vector2 Measure(DynamicSpriteFont font, string text, Vector2 scale, float maxWidth = -1f)
    {
        if (font != _measuredFont || scale != _measuredScale || maxWidth != _measuredMaxWidth || text != _measuredText)
        {
            _measuredFont = font;
            _measuredScale = scale;
            _measuredMaxWidth = maxWidth;
            _measuredText = text;
            _size = ChatManager.GetStringSize(font, text, scale, maxWidth);
        }
        return _size;
    }

    private void Prepare(DynamicSpriteFont font, string text, Color baseColor, Vector2 scale, float maxWidth)
    {
        if (font == _font && baseColor == _baseColor && scale == _scale && maxWidth == _maxWidth && text == _text)
        {
            return;
        }
        _font = font;
        _baseColor = baseColor;
        _scale = scale;
        _maxWidth = maxWidth;
        _text = text;
        _plain = maxWidth < 0f && !ChatManager.MayNeedParsing(text);
        if (!_plain)
        {
            List<TextSnippet> snippets = ChatManager.ParseMessage(text, baseColor);
            ChatManager.ConvertNormalSnippets(snippets);
            _snippets = ChatManager.LayoutSnippets(font, snippets, scale, maxWidth).ToList();
        }
    }

    // ChatManager.DrawColorCodedString(spriteBatch, font, positionedSnippets, ...), without its boxed
    // enumerator and its hover test (whose result the shadowed draw ignores).
    private void DrawSnippets(SpriteBatch spriteBatch, DynamicSpriteFont font, Vector2 position, float rotation, Vector2 origin, Vector2 scale, Color? colorOverride)
    {
        float uniqueScale = Math.Min(scale.X, scale.Y);
        for (int i = 0; i < _snippets.Count; i++)
        {
            PositionedSnippet positioned = _snippets[i];
            Vector2 snippetPosition = position + positioned.Position;
            TextSnippet snippet = positioned.Snippet;
            Color color = colorOverride.HasValue ? colorOverride.Value : snippet.GetVisibleColor();
            if (!snippet.UniqueDraw(justCheckingSize: false, out var _, spriteBatch, snippetPosition, color, uniqueScale))
            {
                spriteBatch.DrawString(font, snippet.Text, snippetPosition, color, rotation, origin, scale, SpriteEffects.None, 0f);
            }
        }
    }
}

/// <summary>
/// A string drawn the older way, ChatManager.DrawColorCodedString(spriteBatch, font, string, ...)
/// (split into words at line breaks, ':' and spaces, with "sss1"/"sss2"/"sssr" parts switching to
/// red/blue/white), with the splitting done once per text. Without a maximum width only.
/// </summary>
public sealed class SplitChatText
{
    private enum Kind
    {
        Word,
        Space,
        Red,
        Blue,
        White,
        LineEnd
    }

    private struct Token
    {
        public Kind Kind;

        public string Word;
    }

    private readonly List<Token> _tokens = new List<Token>();

    private string _text;

    /// <summary>ChatManager.DrawColorCodedString(spriteBatch, font, text, ...) with no maximum width.</summary>
    public Vector2 Draw(SpriteBatch spriteBatch, DynamicSpriteFont font, string text, Vector2 position, Color baseColor, float rotation, Vector2 origin, Vector2 baseScale, bool ignoreColors = false)
    {
        Prepare(text);
        Vector2 cursor = position;
        Vector2 result = cursor;
        float space = font.MeasureString(" ").X;
        Color color = baseColor;
        const float lineScale = 1f;
        float lineHeight = 0f;
        for (int i = 0; i < _tokens.Count; i++)
        {
            Token token = _tokens[i];
            switch (token.Kind)
            {
            case Kind.Red:
                if (!ignoreColors)
                {
                    color = Color.Red;
                }
                break;
            case Kind.Blue:
                if (!ignoreColors)
                {
                    color = Color.Blue;
                }
                break;
            case Kind.White:
                if (!ignoreColors)
                {
                    color = Color.White;
                }
                break;
            case Kind.Space:
                cursor.X += space * baseScale.X * lineScale;
                break;
            case Kind.Word:
                if (lineHeight < lineScale)
                {
                    lineHeight = lineScale;
                }
                spriteBatch.DrawString(font, token.Word, cursor, color, rotation, origin, baseScale * lineScale, SpriteEffects.None, 0f);
                cursor.X += font.MeasureString(token.Word).X * baseScale.X * lineScale;
                result.X = Math.Max(result.X, cursor.X);
                break;
            case Kind.LineEnd:
                cursor.X = position.X;
                cursor.Y += (float)font.LineSpacing * lineHeight * baseScale.Y;
                result.Y = Math.Max(result.Y, cursor.Y);
                lineHeight = 0f;
                break;
            }
        }
        return result;
    }

    /// <summary>ChatManager.DrawColorCodedStringShadow(spriteBatch, font, text, ...) with no maximum width.</summary>
    public void DrawShadow(SpriteBatch spriteBatch, DynamicSpriteFont font, string text, Vector2 position, Color shadowColor, float rotation, Vector2 origin, Vector2 baseScale, float spread = 2f)
    {
        Vector2[] directions = ChatManager.ShadowDirections;
        for (int i = 0; i < directions.Length; i++)
        {
            Draw(spriteBatch, font, text, position + directions[i] * spread, shadowColor, rotation, origin, baseScale, ignoreColors: true);
        }
    }

    // The same splits as ChatManager.DrawColorCodedString, as a list of steps.
    private void Prepare(string text)
    {
        if (text == _text)
        {
            return;
        }
        _text = text;
        _tokens.Clear();
        foreach (string line in text.Split('\n'))
        {
            foreach (string part in line.Split(':'))
            {
                if (part.StartsWith("sss"))
                {
                    if (part.StartsWith("sss1"))
                    {
                        _tokens.Add(new Token { Kind = Kind.Red });
                    }
                    else if (part.StartsWith("sss2"))
                    {
                        _tokens.Add(new Token { Kind = Kind.Blue });
                    }
                    else if (part.StartsWith("sssr"))
                    {
                        _tokens.Add(new Token { Kind = Kind.White });
                    }
                    continue;
                }
                string[] words = part.Split(' ');
                for (int k = 0; k < words.Length; k++)
                {
                    if (k != 0)
                    {
                        _tokens.Add(new Token { Kind = Kind.Space });
                    }
                    _tokens.Add(new Token { Kind = Kind.Word, Word = words[k] });
                }
            }
            _tokens.Add(new Token { Kind = Kind.LineEnd });
        }
    }
}
