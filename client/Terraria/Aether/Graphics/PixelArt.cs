using Microsoft.Xna.Framework.Graphics;

namespace Aether.Graphics;

/// <summary>
/// The sampler of every sprite batch that draws the UI (Terraria's UI is pixel art it draws scaled,
/// with linear filtering): the batch's own, as in vanilla. Mods redirect it, for example to sample
/// the nearest texel (fonts keep linear filtering: <c>Texture.SamplerEXT</c>).
/// </summary>
internal static class PixelArt
{
    /// <summary>The sampler a UI sprite batch uses in place of <paramref name="requested" /> (null: the default, linear).</summary>
    internal static SamplerState Sampler(SamplerState requested)
    {
        return requested;
    }
}
