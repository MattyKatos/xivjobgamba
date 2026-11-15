namespace XIVJobGamba;

/// <summary>
/// Combat and disciple role used to color a wheel slice.
/// </summary>
internal enum WheelRole
{
    Unknown = 0,
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    MagicalRanged,
    Crafter,
    Gatherer,
}

/// <summary>
/// Fields copied off a ClassJob row and its ClassJobCategory row.
/// Role is 1 tank, 2 melee, 3 ranged, 4 healer. JobType is the sheet's
/// finer split: 1 tank, 2 pure healer, 3 melee, 4 physical ranged,
/// 5 magical ranged, 6 barrier healer. Base classes often leave JobType at 0
/// and only set Role. PrimaryStat is 1 strength, 2 dexterity, 4 intelligence,
/// 5 mind. UIPriority is only a fallback for disciples of the hand (100s)
/// and land (200s) when the category row is missing.
/// </summary>
internal readonly record struct JobRoleSignals(
    byte Role,
    byte JobType,
    sbyte DohDolJobIndex,
    byte PrimaryStat,
    byte UiPriority,
    bool CategoryHasHand,
    bool CategoryHasLand,
    bool CategoryHasWar,
    bool CategoryHasMagic);

/// <summary>
/// Maps ClassJob sheet fields to a wheel role. No job row ids.
/// </summary>
internal static class JobRoleClassifier
{
    public const byte PrimaryDexterity = 2;
    public const byte PrimaryIntelligence = 4;

    public static WheelRole Classify(JobRoleSignals signals)
    {
        if (Exclusive(signals.CategoryHasHand, signals.CategoryHasLand, signals.CategoryHasWar, signals.CategoryHasMagic))
            return WheelRole.Crafter;

        if (Exclusive(signals.CategoryHasLand, signals.CategoryHasHand, signals.CategoryHasWar, signals.CategoryHasMagic))
            return WheelRole.Gatherer;

        var fromType = FromJobType(signals.JobType);
        if (fromType != WheelRole.Unknown)
            return fromType;

        var fromRole = FromRole(signals);
        if (fromRole != WheelRole.Unknown)
            return fromRole;

        // Category row was missing. Crafters sit in UI priority 101+, gatherers in 201+.
        if (signals.DohDolJobIndex >= 0)
        {
            if (signals.UiPriority >= 200)
                return WheelRole.Gatherer;

            if (signals.UiPriority >= 100)
                return WheelRole.Crafter;
        }

        return WheelRole.Unknown;
    }

    private static bool Exclusive(bool chosen, bool otherA, bool otherB, bool otherC)
        => chosen && !otherA && !otherB && !otherC;

    private static WheelRole FromJobType(byte jobType)
        => jobType switch
        {
            1 => WheelRole.Tank,
            2 or 6 => WheelRole.Healer,
            3 => WheelRole.Melee,
            4 => WheelRole.PhysicalRanged,
            5 => WheelRole.MagicalRanged,
            _ => WheelRole.Unknown,
        };

    private static WheelRole FromRole(JobRoleSignals signals)
        => signals.Role switch
        {
            1 => WheelRole.Tank,
            2 => WheelRole.Melee,
            4 => WheelRole.Healer,
            3 => SplitRanged(signals),
            _ => WheelRole.Unknown,
        };

    private static WheelRole SplitRanged(JobRoleSignals signals)
    {
        var warOnly = signals.CategoryHasWar && !signals.CategoryHasMagic;
        var magicOnly = signals.CategoryHasMagic && !signals.CategoryHasWar;
        if (magicOnly)
            return WheelRole.MagicalRanged;

        if (warOnly)
            return WheelRole.PhysicalRanged;

        if (signals.PrimaryStat == PrimaryIntelligence)
            return WheelRole.MagicalRanged;

        if (signals.PrimaryStat == PrimaryDexterity)
            return WheelRole.PhysicalRanged;

        return WheelRole.Unknown;
    }
}
