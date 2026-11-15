namespace XIVJobGamba;

/// <summary>
/// Wheel angles in screen space: 0 is east, positive angles run clockwise
/// because ImGui's Y axis points down. The pointer sits at the top.
/// </summary>
internal static class WheelMath
{
    public const float PointerAngle = -MathF.PI / 2f;

    public static float Wrap(float angle)
    {
        var wrapped = angle % MathF.Tau;
        if (wrapped < 0f)
            wrapped += MathF.Tau;

        return wrapped;
    }

    /// <summary>
    /// Rotation that puts the center of <paramref name="index"/> under the pointer.
    /// <paramref name="jitter"/> shifts within the slice and must stay well inside it.
    /// </summary>
    public static float CenterRotation(int index, int count, float jitter)
    {
        var slice = MathF.Tau / count;
        return PointerAngle - ((index + 0.5f) * slice) - jitter;
    }

    public static int SliceIndexAtAngle(float angle, float rotation, int count)
    {
        if (count <= 1)
            return 0;

        var local = Wrap(angle - rotation);
        var index = (int)MathF.Floor(local / (MathF.Tau / count));
        if ((uint)index >= (uint)count)
            return 0;

        return index;
    }

    public static int SliceIndexAtPointer(float rotation, int count)
        => SliceIndexAtAngle(PointerAngle, rotation, count);
}
