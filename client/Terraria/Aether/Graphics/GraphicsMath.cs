using System;
namespace Aether.Graphics;

internal static class GraphicsMath
{
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static float Clamp(float value, float minimum, float maximum) => Math.Min(Math.Max(value, minimum), maximum);
    public static int Clamp(int value, int minimum, int maximum) => Math.Min(Math.Max(value, minimum), maximum);
}
