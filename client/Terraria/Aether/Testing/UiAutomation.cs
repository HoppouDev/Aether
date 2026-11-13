using System;
using System.Collections.Generic;
using System.Linq;
using Aether.UI.Game;
using Aether.UI.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.IO;
using Terraria.UI;

namespace Aether.Testing;

/// <summary>Captures retained UI while it draws and dispatches actions to the captured controls.</summary>
internal static class UiAutomation
{
    [Flags]
    internal enum Actions
    {
        None = 0,
        Activate = 1,
        Secondary = 2,
        SetValue = 4,
        SetText = 8
    }

    internal sealed record Entry(UserInterface Owner, UIState Root, int Revision, UIElement Element, UIElement Parent,
        Rectangle Bounds, Vector2 Point, string Role, string Label, string Context, object Value, int? Slot, Actions Actions);

    internal sealed class Frame(UIElement target)
    {
        internal readonly Dictionary<UIElement, Entry> Entries = new Dictionary<UIElement, Entry>();
        internal readonly UIElement Target = target;
        internal bool Finished;
        internal Exception Error;
        internal bool GameMenu;
        internal int MenuMode;
        internal WorldFileData World;
        internal PlayerFileData Player;
        internal int Width;
        internal int Height;
    }

    internal readonly struct DrawScope : IDisposable
    {
        private readonly UserInterface _previous;
        private readonly UIState _previousRoot;

        internal DrawScope(UserInterface owner)
        {
            _previous = _owner;
            _previousRoot = _root;
            _owner = owner;
            _root = owner.CurrentState;
        }

        public void Dispose()
        {
            _owner = _previous;
            _root = _previousRoot;
        }
    }

    private static Frame _capture;
    private static UserInterface _owner;
    private static UIState _root;
    private static bool _publishedGameMenu;
    private static int _publishedMenuMode;
    private static WorldFileData _publishedWorld;
    private static PlayerFileData _publishedPlayer;
    private static Dictionary<string, Entry> _published = new Dictionary<string, Entry>(StringComparer.Ordinal);

    internal static Frame Capture(string componentId)
    {
        if (componentId == null)
        {
            return _capture = new Frame(null);
        }
        return _published.TryGetValue(componentId, out Entry entry) ? _capture = new Frame(entry.Element) : null;
    }

    internal static void FinishFrame(GraphicsDevice device)
    {
        if (_capture == null)
        {
            return;
        }
        _capture.GameMenu = Main.gameMenu;
        _capture.MenuMode = Main.menuMode;
        _capture.World = Main.gameMenu ? null : Main.ActiveWorldFileData;
        _capture.Player = Main.gameMenu ? null : Main.ActivePlayerFileData;
        _capture.Width = device.PresentationParameters.BackBufferWidth;
        _capture.Height = device.PresentationParameters.BackBufferHeight;
        _capture.Finished = true;
        _capture = null;
    }

    internal static void Cancel(Frame frame)
    {
        if (_capture == frame)
        {
            _capture = null;
        }
    }

    internal static DrawScope Observe(UserInterface owner) => _capture == null ? default : new DrawScope(owner);

    internal static void Record(UIElement element, GraphicsDevice device)
    {
        if (_capture == null || (_capture.Target != null && _capture.Target != element) ||
            _root == null || _owner.CurrentState != _root || _capture.Error != null)
        {
            return;
        }
        bool capturesMouse = Main.RetainedUiCapturesMouse;
        Main.RetainedUiCapturesMouse = false;
        try
        {
            RecordElement(element, device);
        }
        catch (Exception exception)
        {
            _capture.Error = exception;
        }
        finally
        {
            Main.RetainedUiCapturesMouse = capturesMouse;
        }
    }

    private static void RecordElement(UIElement element, GraphicsDevice device)
    {
        Rectangle area = element switch
        {
            UIIconButton icon => icon.Area(),
            UISettingsButton settings => settings.AutomationBounds,
            _ => element.GetDimensions().ToRectangle()
        };
        if (area.Width <= 0 || area.Height <= 0 ||
            (element is UIScrollbar scrollbar && scrollbar.AutoHide && !scrollbar.CanScroll) ||
            (element is UIGameItemSlot slot && slot.IsShown?.Invoke() == false) ||
            element is UITextBox { HideSelf: true })
        {
            return;
        }

        Matrix scale = Main.UIScaleMatrix;
        Vector2 topLeft = Vector2.Transform(new Vector2(area.Left, area.Top), scale);
        Vector2 bottomRight = Vector2.Transform(new Vector2(area.Right, area.Bottom), scale);
        Rectangle bounds = new Rectangle((int)topLeft.X, (int)topLeft.Y,
            (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
        bounds = Rectangle.Intersect(bounds, device.Viewport.Bounds);
        if (device.RasterizerState.ScissorTestEnable)
        {
            bounds = Rectangle.Intersect(bounds, device.ScissorRectangle);
        }
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        UIElement parent = element.Parent;
        while (parent != null && !_capture.Entries.ContainsKey(parent))
        {
            parent = parent.Parent;
        }
        string context = _owner.AutomationContext;
        for (UIElement ancestor = element.Parent; context == null && ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is UICharacterListItem or UIWorldListItem)
            {
                context = ancestor.AutomationLabel;
                break;
            }
        }
        string label = element.AutomationLabel;
        (string role, object value, Actions actions) = Describe(element);
        (Item Item, int? Index) slotData = element switch
        {
            UIGameItemSlot itemSlot => (itemSlot.AutomationItem, itemSlot.Slot),
            UIHotbarSlot hotbar => (hotbar.AutomationItem, hotbar.Slot),
            _ => (null, null)
        };
        if (slotData.Item is Item item)
        {
            label = item.IsAir ? null : item.HoverName;
            value = item.IsAir ? 0 : item.stack;
        }
        Vector2 point = Vector2.Transform(bounds.Center.ToVector2(), Matrix.Invert(scale));
        if (!AllowsInput(_owner) || (element.IgnoresMouseInteraction && element is not UISearchBar))
        {
            actions = Actions.None;
        }
        else if (actions != Actions.None && element is not (UISettingsButton or UISearchBar))
        {
            UIElement hit = _root.GetElementAt(point);
            while (hit != null && hit != element)
            {
                hit = hit.Parent;
            }
            if (hit == null)
            {
                actions = Actions.None;
            }
        }

        _capture.Entries[element] = new Entry(_owner, _root, _owner.AutomationRevision, element, parent,
            bounds, point, role, label, context, value, slotData.Index, actions);
    }

    private static bool AllowsInput(UserInterface owner)
    {
        return Main.gameMenu || (!Main.mapFullscreen && !PlayerInput.IgnoreMouseInterfaceWithoutModUi &&
            (!Main.ingameOptionsWindow || owner.CurrentState is UIIngameOptions) &&
            (!Main.InGameUI.IsVisible || owner == Main.InGameUI));
    }

    private static (string Role, object Value, Actions Actions) Describe(UIElement element)
    {
        return element switch
        {
            UIMenuItem item => (item.IsLabel ? "label" : "button", null,
                item.IsInteractive ? Actions.Activate : Actions.None),
            UIMenuSlider slider => ("slider", slider.AutomationValue, Actions.SetValue),
            UIScrollbar bar => ("scrollbar", bar.AutomationPosition, bar.CanScroll ? Actions.SetValue : Actions.None),
            UISearchBar search => ("text_field", search.AutomationText, Actions.SetText),
            UIOptionsCategory or UISettingsButton => ("button", null, Actions.Activate),
            UIToggleImage toggle => ("button", toggle.IsOn, Actions.Activate),
            UIOptionsText text => (text.HasAction ? "button" : "label", null, text.HasAction ? Actions.Activate : Actions.None),
            UIIconButton icon => ("button", null,
                (icon.OnPress == null ? Actions.None : Actions.Activate) |
                (icon.OnRightPress == null ? Actions.None : Actions.Secondary)),
            UIGameItemSlot slot => ("item_slot", null, slot.SupportsAutomationActivation ? Actions.Activate : Actions.None),
            UIHotbarSlot => ("hotbar_slot", null, Main.blockMouse || Main.LocalPlayer.dead ? Actions.None : Actions.Activate),
            UIState => ("screen", null, Actions.None),
            _ => DescribeEvents(element)
        };
    }

    private static (string Role, object Value, Actions Actions) DescribeEvents(UIElement element)
    {
        Actions actions = (element.HasAutomationClick ? Actions.Activate : Actions.None) |
            (element.HasAutomationRightClick ? Actions.Secondary : Actions.None);
        string role = actions != Actions.None ? "button" : element is UIText ? "label" : "group";
        return (role, null, actions);
    }

    internal static string Publish(Frame frame, bool interactiveOnly)
    {
        string token = Guid.NewGuid().ToString("N")[..8];
        var ids = new Dictionary<UIElement, string>();
        var published = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (Entry entry in frame.Entries.Values)
        {
            if ((!interactiveOnly || entry.Actions != Actions.None) && entry.Owner.CurrentState == entry.Root)
            {
                string id = $"{token}-{ids.Count + 1}";
                ids.Add(entry.Element, id);
                published.Add(id, entry);
            }
        }
        _published = published;
        _publishedGameMenu = frame.GameMenu;
        _publishedMenuMode = frame.MenuMode;
        _publishedWorld = frame.World;
        _publishedPlayer = frame.Player;
        return JsonConvert.SerializeObject(new
        {
            screen = new[] { frame.Width, frame.Height },
            gameMenu = frame.GameMenu,
            menuMode = frame.GameMenu ? (int?)frame.MenuMode : null,
            components = published.Select(pair =>
            {
                Entry entry = pair.Value;
                return new
                {
                    id = pair.Key,
                    parent = entry.Parent != null ? ids.GetValueOrDefault(entry.Parent) : null,
                    screen = entry.Root.GetType().Name,
                    type = entry.Element.GetType().Name,
                    role = entry.Role,
                    label = entry.Label,
                    context = entry.Context,
                    bounds = new[] { entry.Bounds.X, entry.Bounds.Y, entry.Bounds.Width, entry.Bounds.Height },
                    value = entry.Value,
                    slot = entry.Slot,
                    actions = ActionNames(entry.Actions)
                };
            })
        });
    }

    /// <summary>A component's bounds as the latest layout published them, in back-buffer pixels.</summary>
    internal static bool TryGetPublishedBounds(string id, out Rectangle bounds)
    {
        bool found = _published.TryGetValue(id, out Entry entry);
        bounds = found ? entry.Bounds : Rectangle.Empty;
        return found;
    }

    private static IEnumerable<string> ActionNames(Actions actions)
    {
        if (actions.HasFlag(Actions.Activate)) yield return "activate";
        if (actions.HasFlag(Actions.Secondary)) yield return "secondary";
        if (actions.HasFlag(Actions.SetValue)) yield return "set_value";
        if (actions.HasFlag(Actions.SetText)) yield return "set_text";
    }

    internal static bool TryAct(Frame frame, string id, Actions action, JToken value, out string error)
    {
        if (frame.GameMenu != _publishedGameMenu || (frame.GameMenu && frame.MenuMode != _publishedMenuMode) ||
            frame.World != _publishedWorld || frame.Player != _publishedPlayer ||
            !_published.TryGetValue(id, out Entry previous) ||
            !frame.Entries.TryGetValue(previous.Element, out Entry target) ||
            target.Owner != previous.Owner || target.Owner.CurrentState != previous.Root ||
            target.Root != previous.Root || target.Revision != previous.Revision ||
            target.Revision != target.Owner.AutomationRevision || target.Context != previous.Context || target.Slot != previous.Slot ||
            (action is Actions.Activate or Actions.Secondary && target.Label != previous.Label))
        {
            error = "stale UI component; request the current layout";
            return false;
        }
        if (!previous.Actions.HasFlag(action) || !target.Actions.HasFlag(action) || !AllowsInput(target.Owner))
        {
            error = "the component does not currently support that action";
            return false;
        }
        UserInterface previousOwner = UserInterface.ActiveInstance;
        bool capturesMouse = Main.RetainedUiCapturesMouse;
        Main.RetainedUiCapturesMouse = false;
        UserInterface.ActiveInstance = target.Owner;
        try
        {
            UIElement element = target.Element;
            switch (action)
            {
            case Actions.Activate:
                if (element is UIIconButton button)
                    button.OnPress();
                else if (element is UISettingsButton settings)
                    settings.Activate();
                else if (element is UIHotbarSlot hotbar)
                    hotbar.Select();
                else if (element is UIGameItemSlot slot)
                {
                    if (!slot.Activate())
                    {
                        error = "the inventory slot is busy";
                        return false;
                    }
                }
                else
                    element.LeftClick(new UIMouseEvent(element, target.Point));
                break;
            case Actions.Secondary:
                if (element is UIIconButton icon)
                    icon.OnRightPress();
                else
                    element.RightClick(new UIMouseEvent(element, target.Point));
                break;
            case Actions.SetValue:
                float number = value.Value<float>();
                if (element is UIMenuSlider slider)
                    slider.AutomationValue = number;
                else
                    ((UIScrollbar)element).AutomationPosition = number;
                break;
            case Actions.SetText:
                ((UISearchBar)element).SetContents(value.Value<string>());
                break;
            }
        }
        finally
        {
            UserInterface.ActiveInstance = previousOwner;
            Main.RetainedUiCapturesMouse = capturesMouse;
        }
        error = null;
        return true;
    }
}
