namespace XIVJobGamba;

/// <summary>
/// A saved gearset copied out of game memory. Safe to keep across frames.
/// </summary>
internal sealed record GearsetSnapshot(
    int GearsetId,
    uint ClassJobId,
    string JobAbbreviation,
    string JobName,
    string GearsetName,
    short ItemLevel,
    uint IconId,
    WheelRole Role,
    bool IsCrafterGatherer,
    string BlacklistKey);
