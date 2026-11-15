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
    byte JobType,
    bool IsCrafterGatherer,
    string BlacklistKey);
