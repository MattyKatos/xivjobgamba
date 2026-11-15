namespace XIVJobGamba;

/// <summary>
/// Pure rules for which gearsets appear on the wheel.
/// Each saved gearset is its own entry unless one slice per job is enabled,
/// in which case the highest item level set for that job wins (lowest slot id on a tie).
/// Cooldown is per job, not per gearset: every set for a job spun within the last
/// X picks stays off the wheel. Zero disables it.
/// A spin that the game refuses to equip does not count as a pick.
/// </summary>
internal static class SpinRules
{
    public const int MaxCooldown = 40;
    public const int MaxHistory = 100;

    public static List<GearsetSnapshot> SelectEntries(
        IReadOnlyList<GearsetSnapshot> gearsets,
        bool oneSlicePerJob,
        bool includeCraftersAndGatherers,
        IEnumerable<string> blacklistKeys)
    {
        var blacklist = blacklistKeys as IReadOnlySet<string> ?? new HashSet<string>(blacklistKeys);
        var selected = new List<GearsetSnapshot>(gearsets.Count);

        foreach (var gearset in gearsets)
        {
            if (blacklist.Contains(gearset.BlacklistKey))
                continue;

            if (!includeCraftersAndGatherers && gearset.IsCrafterGatherer)
                continue;

            selected.Add(gearset);
        }

        if (!oneSlicePerJob)
        {
            selected.Sort(static (a, b) => a.GearsetId.CompareTo(b.GearsetId));
            return selected;
        }

        var bestByJob = new Dictionary<uint, GearsetSnapshot>();
        foreach (var gearset in selected)
        {
            if (!bestByJob.TryGetValue(gearset.ClassJobId, out var current)
                || gearset.ItemLevel > current.ItemLevel
                || (gearset.ItemLevel == current.ItemLevel && gearset.GearsetId < current.GearsetId))
            {
                bestByJob[gearset.ClassJobId] = gearset;
            }
        }

        var collapsed = bestByJob.Values.ToList();
        collapsed.Sort(static (a, b) => a.GearsetId.CompareTo(b.GearsetId));
        return collapsed;
    }

    public static HashSet<uint> JobsOnCooldown(IReadOnlyList<uint> recentJobIds, int cooldownSpins)
    {
        var blocked = new HashSet<uint>();
        if (cooldownSpins <= 0 || recentJobIds.Count == 0)
            return blocked;

        var start = Math.Max(0, recentJobIds.Count - cooldownSpins);
        for (var i = start; i < recentJobIds.Count; i++)
            blocked.Add(recentJobIds[i]);

        return blocked;
    }

    /// <summary>
    /// How many further spins must happen before this job can be rolled again.
    /// Zero means it is eligible.
    /// </summary>
    public static int SpinsRemaining(uint jobId, IReadOnlyList<uint> recentJobIds, int cooldownSpins)
    {
        if (cooldownSpins <= 0 || recentJobIds.Count == 0)
            return 0;

        var start = Math.Max(0, recentJobIds.Count - cooldownSpins);
        var last = -1;
        for (var i = start; i < recentJobIds.Count; i++)
        {
            if (recentJobIds[i] == jobId)
                last = i;
        }

        if (last < 0)
            return 0;

        var spinsSince = recentJobIds.Count - 1 - last;
        return cooldownSpins - spinsSince;
    }

    public static List<GearsetSnapshot> Eligible(
        IReadOnlyList<GearsetSnapshot> selected,
        IReadOnlyList<uint> recentJobIds,
        int cooldownSpins)
    {
        if (cooldownSpins <= 0)
            return selected.ToList();

        var blocked = JobsOnCooldown(recentJobIds, cooldownSpins);
        var eligible = new List<GearsetSnapshot>(selected.Count);
        foreach (var entry in selected)
        {
            if (!blocked.Contains(entry.ClassJobId))
                eligible.Add(entry);
        }

        return eligible;
    }

    public static bool AllOnCooldown(
        IReadOnlyList<GearsetSnapshot> selected,
        IReadOnlyList<uint> recentJobIds,
        int cooldownSpins)
    {
        return cooldownSpins > 0
            && selected.Count > 0
            && Eligible(selected, recentJobIds, cooldownSpins).Count == 0;
    }

    public static string? WhySpinIsBlocked(
        bool loggedIn,
        bool readFailed,
        int savedCount,
        int selectedCount,
        int eligibleCount,
        int cooldownSpins)
    {
        if (!loggedIn)
            return "Log in to a character to load gearsets.";

        if (readFailed)
            return "Gearsets couldn't be read. Check /xllog for the error.";

        if (savedCount == 0)
            return "No saved gearsets yet. Create one from the Character gearset list.";

        if (selectedCount == 0)
            return "Every saved gearset is excluded by the blacklist or the crafter filter. Change that in settings.";

        if (eligibleCount == 0 && cooldownSpins > 0)
            return "Every remaining job was spun within the last " + cooldownSpins + " picks, so the wheel is empty. Clear history, or lower the cooldown, to spin again.";

        return null;
    }

    public static void Remember(List<uint> recentJobIds, uint jobId)
    {
        recentJobIds.Add(jobId);
        if (recentJobIds.Count > MaxHistory)
            recentJobIds.RemoveRange(0, recentJobIds.Count - MaxHistory);
    }
}
