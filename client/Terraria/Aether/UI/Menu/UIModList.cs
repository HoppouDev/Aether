using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Aether.Modding;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Menu;

internal sealed class UIModList : UIState, IHaveBackButtonCommand
{
    private static readonly Color PanelColor = new Color(33, 43, 79) * 0.85f;
    private static readonly Color ButtonColor = new Color(63, 82, 151) * 0.8f;
    private static readonly Color HoverColor = new Color(73, 94, 171);
    private static readonly Color MutedText = new Color(188, 204, 240);
    private readonly List<ModRow> _rows = new();
    private readonly List<Texture2D> _icons = new();
    private UIList _list;
    private UIList _details;
    private UIScrollbar _listScroll;
    private UIScrollbar _detailScroll;
    private UIPanel _detailPanel;
    private UIWrappedSearchBar _search;
    private UIText _count;
    private UIElement _back;
    private UIElement _folder;
    private UIElement _homepage;
    private UIElement _source;
    private ModRow _selected;
    private bool _virtualKeyboard;
    private static bool Disabled => Program.LaunchParameters.ContainsKey("-nomods");

    internal static void Open()
    {
        SoundEngine.PlaySound(10);
        Main.menuMode = 888;
        Main.MenuUI.SetState(new UIModList());
    }

    public override void OnInitialize()
    {
        var body = new UIElement
        {
            Width = StyleDimension.FromPercent(0.88f),
            MaxWidth = StyleDimension.FromPixels(960f),
            Top = StyleDimension.FromPixels(218f),
            Height = StyleDimension.FromPixelsAndPercent(-218f, 1f),
            HAlign = 0.5f
        };
        Append(body);

        var title = new UITextPanel<LocalizedText>(Language.GetText("AetherMods.Title"), 0.8f, large: true)
        {
            HAlign = 0.5f,
            Top = StyleDimension.FromPixels(-39f),
            BackgroundColor = HoverColor,
            IgnoresMouseInteraction = true
        };
        title.SetPadding(12f);

        var browser = new UIPanel
        {
            Width = StyleDimension.FromPixelsAndPercent(-5f, 0.4f),
            Height = StyleDimension.FromPixelsAndPercent(-100f, 1f),
            BackgroundColor = PanelColor
        };
        body.Append(browser);
        _detailPanel = new UIPanel
        {
            Width = StyleDimension.FromPixelsAndPercent(-5f, 0.6f),
            Height = StyleDimension.FromPixelsAndPercent(-100f, 1f),
            HAlign = 1f,
            BackgroundColor = PanelColor
        };
        body.Append(_detailPanel);
        body.Append(title);

        _search = new UIWrappedSearchBar(ReturnFromKeyboard, Language.GetText("AetherMods.Search"));
        _search.MaxInputLength = 64;
        _search.CustomOpenVirtualKeyboard = keyboard =>
        {
            _virtualKeyboard = true;
            Main.MenuUI.SetState(keyboard);
        };
        _search.SetSearchSnapPoint("Search", 0);
        _search.SetSearchCancelSnapPoint("Clear", 0);
        browser.Append(_search);
        _count = new UIText("", 0.85f)
        {
            Width = StyleDimension.Fill,
            VAlign = 1f,
            TextOriginX = 0f,
            IgnoresMouseInteraction = true
        };
        browser.Append(_count);
        (_list, _listScroll) = AddList(browser, 34f, 58f);
        (_details, _detailScroll) = AddList(_detailPanel, 0f, 0f);
        _details.ManualSortMethod = _ => { };

        _back = AddButton(body, "UI.Back", HandleBackButtonUsage, 0f, footer: true);
        _folder = AddButton(body, "AetherMods.OpenFolder", OpenModsFolder, 1f, footer: true);
        _search.OnSearchContentsChanged += Refresh;
    }

    public override void OnActivate()
    {
        if (_virtualKeyboard)
            return;

        var fallback = Main.Assets.Request<Texture2D>("Images/UI/DefaultResourcePackIcon").Value;
        foreach (var mod in ModCatalog.LoadedMods.OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(mod => mod.Id, StringComparer.Ordinal))
        {
            var row = new ModRow(mod, LoadIcon(mod) ?? fallback, _rows.Count);
            row.OnLeftClick += (_, _) =>
            {
                SoundEngine.PlaySound(12);
                Select(row);
            };
            _rows.Add(row);
        }
        Refresh(null);
    }

    public override void OnDeactivate()
    {
        if (_virtualKeyboard)
            return;
        if (_search.IsWritingText)
            _search.ToggleTakingText();
        foreach (var icon in _icons)
            icon.Dispose();
        _icons.Clear();
        _list.Clear();
        _rows.Clear();
        Select(null);
    }

    private void ReturnFromKeyboard()
    {
        Main.MenuUI.SetState(this);
        _virtualKeyboard = false;
    }

    public void HandleBackButtonUsage()
    {
        SoundEngine.PlaySound(11);
        Main.menuMode = 0;
    }

    public override void Update(GameTime gameTime)
    {
        if (!_search.IsWritingText && !PlayerInput.UsingGamepad && Main.keyState.IsKeyDown(Keys.Escape)
            && Main.oldKeyState.IsKeyUp(Keys.Escape))
            HandleBackButtonUsage();
        base.Update(gameTime);
    }

    private static (UIList, UIScrollbar) AddList(UIElement parent, float top, float reservedHeight)
    {
        var list = new UIList
        {
            Top = StyleDimension.FromPixels(top),
            Width = StyleDimension.FromPixelsAndPercent(-22f, 1f),
            Height = StyleDimension.FromPixelsAndPercent(-reservedHeight, 1f)
        };
        var scrollbar = new UIScrollbar
        {
            Top = StyleDimension.FromPixels(top + 5f),
            Height = StyleDimension.FromPixelsAndPercent(-reservedHeight - 10f, 1f),
            HAlign = 1f,
            AutoHide = true
        };
        parent.Append(list);
        parent.Append(scrollbar);
        list.SetScrollbar(scrollbar);
        return (list, scrollbar);
    }

    private static UITextPanel<LocalizedText> AddButton(UIElement parent, string key, Action action, float align, bool footer = false)
    {
        var button = new UITextPanel<LocalizedText>(Language.GetText(key), footer ? 0.62f : 0.9f, large: footer)
        {
            Width = StyleDimension.FromPixelsAndPercent(-5f, 0.5f),
            Height = StyleDimension.FromPixels(footer ? 42f : 30f),
            Top = StyleDimension.FromPixels(footer ? -50f : 0f),
            HAlign = align,
            VAlign = 1f,
            BackgroundColor = ButtonColor
        };
        button.SetPadding(3f);
        button.OnMouseOver += (_, _) =>
        {
            SoundEngine.PlaySound(12);
            button.BackgroundColor = HoverColor;
            button.BorderColor = Colors.FancyUIFatButtonMouseOver;
        };
        button.OnMouseOut += (_, _) =>
        {
            button.BackgroundColor = ButtonColor;
            button.BorderColor = Color.Black;
        };
        button.OnLeftClick += (_, _) => action();
        button.SetSnapPoint(key, 0);
        parent.Append(button);
        return button;
    }

    private void Refresh(string query)
    {
        query = query?.Trim() ?? "";
        _list.Clear();
        _listScroll.ViewPosition = 0f;
        ModRow first = null;
        bool hasSelection = false;
        foreach (var row in _rows)
        {
            if (!row.Matches(query))
                continue;
            _list.Add(row);
            first ??= row;
            hasSelection |= row == _selected;
        }
        int count = _list.Count;
        if (count == 0)
            _list.Add(new ModText(Language.GetTextValue(Disabled ? "AetherMods.Disabled" :
                _rows.Count == 0 ? "AetherMods.Empty" : "AetherMods.NoMatches"), 0.9f, MutedText, wrap: true));
        _list.Recalculate();
        _count.SetText(query.Length == 0 ? Language.GetTextValue("AetherMods.Loaded", _rows.Count) :
            Language.GetTextValue("AetherMods.Filtered", count, _rows.Count));
        Select(hasSelection ? _selected : first);
    }

    private void Select(ModRow row)
    {
        if (_selected != null)
            _selected.SetSelected(false);
        _selected = row;
        _details.Clear();
        _detailScroll.ViewPosition = 0f;
        _homepage?.Remove();
        _source?.Remove();
        _homepage = _source = null;

        if (row != null)
        {
            row.SetSelected(true);
            ModInfo mod = row.Mod;
            _details.Add(new ModText(mod.Name, 1.25f, Color.White, wrap: true));
            _details.Add(new ModText(Language.GetTextValue("AetherMods.Identity", mod.Id, mod.Version), 0.85f, MutedText, wrap: true));
            AddMetadata("AetherMods.Authors", mod.Authors);
            if (!string.IsNullOrWhiteSpace(mod.Description))
                _details.Add(new ModText(mod.Description, 1f, Color.White, wrap: true));
            AddMetadata("AetherMods.Contributors", mod.Contributors);
            AddMetadata("AetherMods.License", mod.Licenses);
            _homepage = AddLink(mod.Homepage, "AetherMods.Homepage", 0f);
            _source = AddLink(mod.Source, "AetherMods.Source", 1f);
        }
        else if (_rows.Count == 0)
        {
            string help = Disabled ? "AetherMods.RestartWithMods" : "AetherMods.AddMods";
            _details.Add(new ModText(Language.GetTextValue(help), 1f, MutedText, wrap: true));
        }
        float buttons = _homepage != null || _source != null ? 38f : 0f;
        _details.Height.Set(-buttons, 1f);
        _detailScroll.Height.Set(-buttons - 10f, 1f);
        _detailPanel.Recalculate();
    }

    private void AddMetadata(string key, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
            _details.Add(new ModText(Language.GetTextValue(key, string.Join(", ", values)), 0.9f, MutedText, wrap: true));
    }

    private UIElement AddLink(string value, string key, float align)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || uri.Host.Length == 0)
            return null;
        return AddButton(_detailPanel, key, () => OpenTarget(uri.AbsoluteUri), align);
    }

    private static void OpenModsFolder()
    {
        string folder = Path.GetFullPath(Path.Combine(Program.SavePath, "Mods"));
        if (Utils.TryCreatingDirectory(folder))
            OpenTarget(folder);
    }

    private static void OpenTarget(string target)
    {
        SoundEngine.PlaySound(12);
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not open '{target}': {exception.Message}");
        }
    }

    private Texture2D LoadIcon(ModInfo mod)
    {
        if (mod.IconPath == null)
            return null;
        try
        {
            using Stream stream = ModResources.OpenIcon(mod.Id);
            if (stream.Length is < 24 or > 4 * 1024 * 1024)
                return null;
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt64BigEndian(header) != 0x89504E470D0A1A0AUL ||
                !header.Slice(12, 4).SequenceEqual("IHDR"u8) ||
                BinaryPrimitives.ReadUInt32BigEndian(header[16..]) is 0 or > 1024 ||
                BinaryPrimitives.ReadUInt32BigEndian(header[20..]) is 0 or > 1024)
                return null;
            stream.Position = 0;
            var icon = Texture2D.FromStream(Main.instance.GraphicsDevice, stream, 64, 64, zoom: false);
            _icons.Add(icon);
            return icon;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not read the icon for '{mod.Id}': {exception.Message}");
            return null;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
        base.Draw(spriteBatch);
        if (!PlayerInput.UsingGamepadUI)
            return;

        var helper = new UIGamepadHelper();
        int next = 3000;
        UILinkPoint Link(SnapPoint snap) => helper.MakeLinkPointFromSnapPoint(next++, snap);
        UILinkPoint ButtonLink(UIElement element) => element.GetSnapPoint(out SnapPoint snap) ? Link(snap) : null;
        var back = ButtonLink(_back);
        var folder = ButtonLink(_folder);
        var search = Link(_search.GetSnapPoints().First(snap => snap.Name == "Search"));
        var clear = Link(_search.GetSnapPoints().First(snap => snap.Name == "Clear"));
        helper.PairLeftRight(search, clear);
        helper.PairLeftRight(back, folder);
        var visible = _list.GetSnapPoints();
        helper.CullPointsOutOfElementArea(spriteBatch, visible, _list);
        var rows = helper.GetVerticalStripFromCategoryName(ref next, visible, "Mod");
        if (rows != null)
        {
            helper.PairUpDown(search, rows[0]);
            helper.PairUpDown(rows[^1], back);
        }
        else
        {
            helper.PairUpDown(search, back);
        }
        clear.Down = search.Down;
        var homepage = _homepage != null ? ButtonLink(_homepage) : null;
        var source = _source != null ? ButtonLink(_source) : null;
        if (homepage != null && source != null)
            helper.PairLeftRight(homepage, source);
        var firstLink = homepage ?? source;
        if (firstLink != null)
        {
            helper.PairUpDown(firstLink, folder);
            if (source != null)
                source.Down = folder.ID;
            helper.LinkVerticalStripRightSideToSingle(rows, firstLink);
            clear.Right = firstLink.ID;
            if (rows == null)
                firstLink.Left = clear.ID;
        }
        else
        {
            folder.Up = back.Up;
        }
        helper.MoveToVisuallyClosestPoint(3000, next);
    }

    private sealed class ModRow : UIPanel
    {
        internal readonly ModInfo Mod;
        private readonly int _order;
        private bool _selected;
        internal override string AutomationLabel => Mod.Name;

        internal ModRow(ModInfo mod, Texture2D icon, int order)
        {
            Mod = mod;
            _order = order;
            Width = StyleDimension.Fill;
            Height = StyleDimension.FromPixels(58f);
            SetPadding(6f);
            SetSelected(false);
            var image = new UIImage(icon)
            {
                Width = StyleDimension.FromPixels(40f),
                Height = StyleDimension.FromPixels(40f),
                VAlign = 0.5f,
                ScaleToFit = true,
                IgnoresMouseInteraction = true
            };
            Append(image);
            AddText(mod.Name, 1f, Color.White, 4f);
            AddText(Language.GetTextValue("AetherMods.Identity", mod.Id, mod.Version), 0.8f, MutedText, 25f);
            SetSnapPoint("Mod", order);
            OnMouseOver += (_, _) => { SoundEngine.PlaySound(12); UpdateColors(); };
            OnMouseOut += (_, _) => UpdateColors();
        }

        private void AddText(string text, float scale, Color color, float top)
        {
            var label = new ModText(text, scale, color)
            {
                Left = StyleDimension.FromPixels(50f),
                Top = StyleDimension.FromPixels(top),
                Width = StyleDimension.FromPixelsAndPercent(-50f, 1f)
            };
            Append(label);
        }

        internal bool Matches(string query) => query.Length == 0 ||
            Mod.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Mod.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Mod.Authors.Any(author => author.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        internal void SetSelected(bool selected)
        {
            _selected = selected;
            UpdateColors();
        }

        private void UpdateColors()
        {
            BackgroundColor = _selected || IsMouseHovering ? HoverColor : ButtonColor;
            BorderColor = _selected || IsMouseHovering ? Colors.FancyUIFatButtonMouseOver : Color.Black;
        }

        public override int CompareTo(object obj) => obj is ModRow other ? _order.CompareTo(other._order) : base.CompareTo(obj);
    }

    // Manifest text is literal. Terraria's UIText interprets chat tags.
    private sealed class ModText : UIElement
    {
        private readonly string _text;
        private readonly float _scale;
        private readonly Color _color;
        private readonly bool _wrap;
        private string _display = "";
        private float _layoutWidth = -1f;
        private float _drawScale;
        internal override string AutomationLabel => _text;

        internal ModText(string text, float scale, Color color, bool wrap = false)
        {
            _text = Utils.TrimUserString(wrap ? text.Replace("\r", "") : text.Replace('\r', ' ').Replace('\n', ' '),
                wrap ? 8192 : 200);
            _scale = scale;
            _color = color;
            _wrap = wrap;
            Width = StyleDimension.Fill;
            Height = StyleDimension.FromPixels(20f);
            IgnoresMouseInteraction = true;
        }

        public override void Recalculate()
        {
            base.Recalculate();
            float width = GetInnerDimensions().Width;
            if (width <= 0 || width == _layoutWidth)
                return;
            _layoutWidth = width;
            var font = FontAssets.MouseText.Value;
            _display = _wrap ? font.CreateWrappedText(_text, _scale, width, 0f, Language.ActiveCulture.CultureInfo) : _text;
            Vector2 size = font.MeasureString(_display);
            _drawScale = _wrap || size.X == 0 ? _scale : Math.Min(_scale, width / size.X);
            if (_wrap)
            {
                Height.Set(size.Y * _drawScale + 3f, 0f);
                base.Recalculate();
            }
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Vector2 position = GetInnerDimensions().Position();
            var font = FontAssets.MouseText.Value;
            spriteBatch.DrawString(font, _display, position + Vector2.One, Color.Black, 0f, Vector2.Zero, _drawScale, SpriteEffects.None, 0f);
            spriteBatch.DrawString(font, _display, position, _color, 0f, Vector2.Zero, _drawScale, SpriteEffects.None, 0f);
        }
    }
}
