using Microsoft.Xna.Framework.Graphics;

namespace Aether.Graphics;

/// <summary>
/// Terraria's UI is pixel art that it draws scaled (the UI scale, slot and icon scales) with
/// linear filtering, which blurs it. With <see cref="GraphicsSettings.SharpUI" /> on, the sprite
/// batches that draw the UI sample the nearest texel instead.
/// </summary>
internal static class PixelArt
{
    /// <summary>The sampler a UI sprite batch uses in place of <paramref name="requested" /> (null: the default, linear).</summary>
    internal static SamplerState Sampler(SamplerState requested)
    {
        return GraphicsSettings.SharpUI ? SamplerState.PointClamp : requested;
    }
}
