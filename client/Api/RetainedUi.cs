using Aether.Modding;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.Graphics.Capture;
using Terraria.UI;

namespace Aether.Api;

public static class RetainedUi
{
    private static readonly Dictionary<string, RetainedUiLayer> layers = [];

    public static RetainedUiLayer Register(string id, Func<UIState> create, bool initiallyVisible = true)
    {
        ArgumentNullException.ThrowIfNull(create);
        string owner = Registration.Owner();
        var layer = new RetainedUiLayer(id, create, initiallyVisible);
        Registration.Add(layers, id, layer, owner);
        if (layers.Count == 1 && !ServerEvents.IsDedicatedServer)
        {
            ClientStartup.UpdateRetainedUi = time => Update((GameTime)time);
            ClientStartup.DrawRetainedUi = time => Draw((GameTime)time);
        }
        return layer;
    }

    private static bool CanPresent => GameBridge.WorldReady && !Main.hideUI && !Main.mapFullscreen &&
        !Main.ingameOptionsWindow && !Main.inFancyUI && !Main.InGameUI.IsVisible && !CaptureManager.Instance.Active;

    private static bool Update(GameTime time)
    {
        bool present = CanPresent;
        bool acceptMouse = present && FocusHelper.AllowInputProcessing && !Main.blockInput &&
            !PlayerInput.UsingGamepad && !PlayerInput.IgnoreMouseInterface;
        UserInterface previous = UserInterface.ActiveInstance;
        try
        {
            RetainedUiLayer? hovered = null;
            RetainedUiLayer? captured = null;
            foreach (var layer in layers.Values)
            {
                layer.Prepare(present);
                if (!acceptMouse || !layer.WantsMouse(out bool holdsPointer))
                    continue;
                if (holdsPointer)
                    captured = layer;
                else
                    hovered = layer;
            }

            RetainedUiLayer? receiver = captured ?? hovered;
            foreach (var layer in layers.Values)
                layer.Update(time, layer == receiver);
            return receiver != null;
        }
        finally
        {
            UserInterface.ActiveInstance = previous;
        }
    }

    private static void Draw(GameTime time)
    {
        if (!CanPresent)
            return;

        UserInterface previous = UserInterface.ActiveInstance;
        try
        {
            foreach (var layer in layers.Values)
                layer.Draw(time);
        }
        finally
        {
            UserInterface.ActiveInstance = previous;
        }
    }

    internal static void EndWorld()
    {
        UserInterface previous = UserInterface.ActiveInstance;
        try
        {
            foreach (var layer in layers.Values)
                layer.EndWorld();
        }
        finally
        {
            UserInterface.ActiveInstance = previous;
        }
    }
}

public sealed class RetainedUiLayer
{
    private readonly string id;
    private readonly Func<UIState> create;
    private bool visible;
    private bool failed;
    private UserInterface? ui;
    private UIState? state;
    private Vector2 size;

    internal RetainedUiLayer(string id, Func<UIState> create, bool initiallyVisible)
    {
        this.id = id;
        this.create = create;
        visible = initiallyVisible;
    }

    public bool IsVisible
    {
        get => visible && !failed;
        set
        {
            Registration.RequireGameThread();
            visible = value;
        }
    }

    internal void Prepare(bool present)
    {
        if (failed)
            return;
        bool deactivating = false;
        try
        {
            if (present && visible)
            {
                ui ??= new UserInterface { AutomationContext = id };
                UserInterface.ActiveInstance = ui;
                state ??= create() ?? throw new InvalidOperationException("The UI factory returned null.");
                var dimensions = ui.GetDimensions();
                Vector2 newSize = new(dimensions.Width, dimensions.Height);
                if (ui.CurrentState == null)
                    ui.SetState(state);
                else if (size != newSize)
                    ui.Recalculate();
                size = newSize;
            }
            else if (ui?.CurrentState != null)
            {
                UserInterface.ActiveInstance = ui;
                deactivating = true;
                ui.SetState(null);
            }
        }
        catch (Exception exception)
        {
            Disable(exception, deactivate: !deactivating);
        }
    }

    internal bool WantsMouse(out bool holdsPointer)
    {
        holdsPointer = false;
        if (!IsVisible || ui?.CurrentState is not UIState current)
            return false;
        UserInterface.ActiveInstance = ui;
        try
        {
            holdsPointer = ui.MouseCaptured();
            return holdsPointer || current.GetElementAt(Main.MouseScreen) is { } hit && hit != current;
        }
        catch (Exception exception)
        {
            Disable(exception);
            return false;
        }
    }

    internal void Update(GameTime time, bool acceptMouse)
    {
        if (!IsVisible || ui?.CurrentState == null)
            return;
        UserInterface.ActiveInstance = ui;
        try
        {
            ui.Update(time, acceptMouse);
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    internal void Draw(GameTime time)
    {
        if (!IsVisible || ui?.CurrentState == null)
            return;
        UserInterface.ActiveInstance = ui;
        try
        {
            ui.Draw(Main.spriteBatch, time);
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    internal void EndWorld()
    {
        Prepare(false);
        ui = null;
        state = null;
    }

    private void Disable(Exception exception, bool deactivate = true)
    {
        failed = true;
        Console.Error.WriteLine($"Aether API disabled retained UI '{id}': {exception}");
        if (deactivate && ui?.CurrentState != null)
        {
            try { ui.SetState(null); }
            catch (Exception cleanup) { Console.Error.WriteLine($"Aether API could not deactivate retained UI '{id}': {cleanup}"); }
        }
        ui = null;
        state = null;
    }
}
