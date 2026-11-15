using System.Numerics;

namespace XIVJobGamba;

/// <summary>
/// Role colors for the wheel. Adjacent slices of the same role alternate a
/// lighter shade, and a third darker shade breaks an odd wrap around the circle.
/// </summary>
internal static class JobPalette
{
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    public static readonly Vector4 LightText = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 DarkText = new(0.02f, 0.02f, 0.02f, 1f);

    public static Vector4 Base(WheelRole role)
        => role switch
        {
            WheelRole.Tank => new Vector4(0.23f, 0.45f, 0.86f, 1f),
            WheelRole.Healer => new Vector4(0.20f, 0.62f, 0.34f, 1f),
            WheelRole.Melee => new Vector4(0.78f, 0.20f, 0.20f, 1f),
            WheelRole.PhysicalRanged => new Vector4(0.92f, 0.74f, 0.16f, 1f),
            WheelRole.MagicalRanged => new Vector4(0.56f, 0.30f, 0.82f, 1f),
            WheelRole.Crafter => new Vector4(0.64f, 0.64f, 0.66f, 1f),
            WheelRole.Gatherer => new Vector4(0.30f, 0.30f, 0.32f, 1f),
            _ => new Vector4(0.42f, 0.40f, 0.38f, 1f),
        };

    public static Vector4 ApplyShade(Vector4 color, int shade)
        => shade switch
        {
            1 => Lerp(color, White, 0.34f),
            2 => Lerp(color, Black, 0.30f),
            _ => color,
        };

    public static Vector4 Muted(Vector4 color)
    {
        var lum = Luminance(color);
        var grey = new Vector4(lum, lum, lum, 1f);
        var desaturated = Lerp(color, grey, 0.72f);
        return Lerp(desaturated, new Vector4(0.14f, 0.14f, 0.14f, 1f), 0.28f);
    }

    public static Vector4 SliceColor(WheelRole role, int shade, bool muted)
    {
        var color = ApplyShade(Base(role), shade);
        return muted ? Muted(color) : color;
    }

    public static Vector4 TextColor(Vector4 background)
        => UseDarkText(background) ? DarkText : LightText;

    public static Vector4 TextHalo(Vector4 background)
    {
        var halo = UseDarkText(background) ? White : Black;
        return new Vector4(halo.X, halo.Y, halo.Z, 0.85f);
    }

    public static bool UseDarkText(Vector4 background)
        => Contrast(DarkText, background) >= Contrast(LightText, background);

    /// <summary>
    /// 0 is the base color, 1 is lighter, 2 is darker. Equal neighbors never
    /// share a shade, including the first and last slice of the circle.
    /// </summary>
    public static int[] AlternatingShades(IReadOnlyList<WheelRole> roles)
    {
        var count = roles.Count;
        var shades = new int[count];
        if (count == 0)
            return shades;

        for (var i = 1; i < count; i++)
        {
            if (roles[i] == roles[i - 1])
                shades[i] = shades[i - 1] == 0 ? 1 : 0;
        }

        if (count > 2 && roles[0] == roles[count - 1] && shades[0] == shades[count - 1])
        {
            var previous = shades[count - 2];
            var replacement = previous == 2 ? 0 : 2;
            if (replacement == shades[0])
                replacement = 1;

            shades[count - 1] = replacement;
        }

        return shades;
    }

    public static float RelativeLuminance(Vector4 color)
        => (0.2126f * Linear(color.X)) + (0.7152f * Linear(color.Y)) + (0.0722f * Linear(color.Z));

    public static float Contrast(Vector4 a, Vector4 b)
    {
        var left = RelativeLuminance(a);
        var right = RelativeLuminance(b);
        var lighter = MathF.Max(left, right);
        var darker = MathF.Min(left, right);
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    private static float Luminance(Vector4 color)
        => (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);

    private static float Linear(float channel)
    {
        if (channel <= 0.04045f)
            return channel / 12.92f;

        return MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    private static Vector4 Lerp(Vector4 from, Vector4 to, float t)
        => new(
            from.X + ((to.X - from.X) * t),
            from.Y + ((to.Y - from.Y) * t),
            from.Z + ((to.Z - from.Z) * t),
            1f);
}
