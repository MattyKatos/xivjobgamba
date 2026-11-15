namespace XIVJobGamba;

internal enum DutyPointOutcome
{
    Awarded,
    NoSpins,
    TooManySpins,
    WrongJob,
}

/// <summary>
/// What happened when a duty entry was scored. <see cref="SpinsSeen"/> is the
/// count before the counter resets.
/// </summary>
internal readonly record struct DutyPointDecision(
    DutyPointOutcome Outcome,
    int PointsAwarded,
    int SpinsSeen,
    uint? LandedJobId,
    uint? CurrentJobId)
{
    public bool Awarded => Outcome == DutyPointOutcome.Awarded;
}

/// <summary>
/// Points are earned by entering a duty after exactly one spin, on that job.
/// </summary>
internal static class DutyPoints
{
    public const int DefaultPointsPerDuty = 1;
    public const int MaxPointsPerDuty = 100;

    public static DutyPointDecision Evaluate(
        int spinsSinceDuty,
        uint? landedJobId,
        uint? currentJobId,
        int pointsPerDuty)
    {
        if (spinsSinceDuty <= 0 || (spinsSinceDuty == 1 && landedJobId is null))
            return new DutyPointDecision(DutyPointOutcome.NoSpins, 0, Math.Max(0, spinsSinceDuty), landedJobId, currentJobId);

        if (spinsSinceDuty != 1)
            return new DutyPointDecision(DutyPointOutcome.TooManySpins, 0, spinsSinceDuty, landedJobId, currentJobId);

        if (currentJobId is null || landedJobId is null || currentJobId.Value != landedJobId.Value)
            return new DutyPointDecision(DutyPointOutcome.WrongJob, 0, spinsSinceDuty, landedJobId, currentJobId);

        var award = Math.Clamp(pointsPerDuty, 0, MaxPointsPerDuty);
        return new DutyPointDecision(DutyPointOutcome.Awarded, award, spinsSinceDuty, landedJobId, currentJobId);
    }

    public static string Describe(DutyPointDecision decision, int totalPoints, string? landedJob, string? currentJob)
    {
        var landed = string.IsNullOrWhiteSpace(landedJob) ? "the spun job" : landedJob;
        var current = string.IsNullOrWhiteSpace(currentJob) ? "an unknown job" : currentJob;

        return decision.Outcome switch
        {
            DutyPointOutcome.Awarded when decision.PointsAwarded == 1 =>
                $"+1 duty point. Total: {totalPoints}. You entered on {landed}.",
            DutyPointOutcome.Awarded when decision.PointsAwarded > 1 =>
                $"+{decision.PointsAwarded} duty points. Total: {totalPoints}. You entered on {landed}.",
            DutyPointOutcome.Awarded =>
                $"Duty spin matched {landed}, but the award is set to 0. Total: {totalPoints}.",
            DutyPointOutcome.NoSpins =>
                "No duty points. Spin exactly once after the last duty, then enter on that job.",
            DutyPointOutcome.TooManySpins =>
                $"No duty points. You spun {decision.SpinsSeen} times since the last duty. It has to be exactly one.",
            _ =>
                $"No duty points. The wheel landed on {landed}, and you entered as {current}.",
        };
    }
}
