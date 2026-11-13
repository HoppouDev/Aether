using Terraria;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria.GameInput;

namespace Aether.Graphics;

public static class CursorHooks
{
    private static Vector2 mousePosition;

    public static void SampleMouse()
    {
        // Sample once per rendered frame so the cursor and outline stay aligned.
        MouseState mouse = Mouse.GetState();
        mousePosition = new Vector2(mouse.X, mouse.Y) * PlayerInput.RawMouseScale;
    }

    public static Vector2 GetDrawPosition()
    {
        // Controller cursors can be positioned by smart selection or housing tools.
        if (PlayerInput.SettingsForUI.ShowGamepadCursor || PlayerInput.ControllerHousingCursorActive)
            return new Vector2(Main.mouseX, Main.mouseY);

        // Cursor sprite batches use UIScaleMatrix; keep gameplay input untouched.
        return mousePosition / Main.UIScale;
    }
}
