namespace XIVJobGamba;

/// <summary>
/// A decelerating spin that finishes with the pointer inside a chosen slice.
/// Time is supplied by the caller so the motion can be tested without ImGui.
/// </summary>
internal sealed class SpinAnimation
{
    public bool IsSpinning { get; private set; }

    public float Rotation { get; private set; }

    public int WinnerIndex { get; private set; }

    public void SetRotation(float rotation) => Rotation = rotation;

    public void Start(int count, int winnerIndex, float now, float durationSeconds, float extraTurns, float jitter)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if ((uint)winnerIndex >= (uint)count)
            throw new ArgumentOutOfRangeException(nameof(winnerIndex));

        var slice = MathF.Tau / count;
        var maxJitter = slice * 0.30f;
        jitter = Math.Clamp(jitter, -maxJitter, maxJitter);

        var start = WheelMath.Wrap(Rotation);
        extraTurns = MathF.Max(1f, extraTurns);
        var minimum = start + (extraTurns * MathF.Tau);
        var target = WheelMath.CenterRotation(winnerIndex, count, jitter);
        var delta = minimum - target;
        if (delta > 0f)
            target += MathF.Ceiling(delta / MathF.Tau) * MathF.Tau;

        if (target <= start)
            target += MathF.Tau;

        startRotation = start;
        endRotation = target;
        startTime = now;
        duration = MathF.Max(0.05f, durationSeconds);
        WinnerIndex = winnerIndex;
        Rotation = start;
        IsSpinning = true;
    }

    /// <summary>
    /// Advances the spin. Returns true on the single call that reaches the end.
    /// </summary>
    public bool Advance(float now)
    {
        if (!IsSpinning)
            return false;

        var elapsed = now - startTime;
        if (elapsed < 0f)
            elapsed = 0f;

        var t = elapsed / duration;
        if (t >= 1f)
        {
            Rotation = endRotation;
            IsSpinning = false;
            return true;
        }

        Rotation = startRotation + ((endRotation - startRotation) * EaseOutCubic(t));
        return false;
    }

    public void Cancel() => IsSpinning = false;

    private static float EaseOutCubic(float t)
    {
        var inverse = 1f - t;
        return 1f - (inverse * inverse * inverse);
    }

    private float startRotation;
    private float endRotation;
    private float startTime;
    private float duration = 1f;
}
