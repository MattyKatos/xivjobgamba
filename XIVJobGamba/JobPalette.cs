using System.Numerics;

namespace XIVJobGamba;

/// <summary>
/// Slice colors from the ClassJob sheet's JobType, shifted a little per job
/// so two tanks are not the same blue.
/// </summary>
internal static class JobPalette
{
    public static Vector4 For(uint classJobId, byte jobType)
    {
        var baseColor = jobType switch
        {
            1 => new Vector4(0.27f, 0.46f, 0.82f, 1f),
            2 => new Vector4(0.28f, 0.70f, 0.42f, 1f),
            3 => new Vector4(0.78f, 0.30f, 0.28f, 1f),
            4 => new Vector4(0.84f, 0.62f, 0.22f, 1f),
            5 => new Vector4(0.58f, 0.36f, 0.80f, 1f),
            6 => new Vector4(0.22f, 0.64f, 0.66f, 1f),
            _ => new Vector4(0.72f, 0.54f, 0.30f, 1f),
        };

        var shift = (((classJobId * 37) % 7) - 3) * 0.04f;
        return new Vector4(
            Math.Clamp(baseColor.X + shift, 0.16f, 0.90f),
            Math.Clamp(baseColor.Y + (shift * 0.35f), 0.14f, 0.86f),
            Math.Clamp(baseColor.Z - shift, 0.14f, 0.90f),
            1f);
    }

    public static Vector4 Muted(Vector4 color)
    {
        const float grey = 0.20f;
        return new Vector4(
            (color.X * 0.22f) + grey,
            (color.Y * 0.22f) + grey,
            (color.Z * 0.22f) + grey,
            0.95f);
    }
}
