namespace XIVJobGamba;

/// <summary>
/// Points and the spin that is waiting on the next duty. Stored with
/// BinaryWriter in the plugin config directory, separate from the JSON config.
/// Layout: magic "GMB1", int32 version 1, int32 points, int32 spins,
/// bool hasLandedJob, uint32 landedJobId, uint32 currentDutyContentId.
/// </summary>
internal sealed class DutyScoreStore
{
    private const int Version = 1;
    private readonly object gate = new();
    private readonly string path;

    private DutyScoreStore(string path)
    {
        this.path = path;
    }

    public int Points { get; private set; }

    public int SpinsSinceDuty { get; private set; }

    public bool HasLandedJob { get; private set; }

    public uint LandedJobId { get; private set; }

    public uint CurrentDutyContentId { get; private set; }

    public bool ResetBecauseUnreadable { get; private set; }

    public static DutyScoreStore Load(string path)
    {
        var store = new DutyScoreStore(path);
        store.ReadOrReset();
        return store;
    }

    public DutyScoreSnapshot Snapshot()
    {
        lock (gate)
        {
            return new DutyScoreSnapshot(Points, SpinsSinceDuty, HasLandedJob, LandedJobId);
        }
    }

    public void RecordSpin(uint classJobId)
    {
        lock (gate)
        {
            if (SpinsSinceDuty < int.MaxValue)
                SpinsSinceDuty++;

            HasLandedJob = true;
            LandedJobId = classJobId;
            Write();
        }
    }

    /// <summary>
    /// Scores the duty entry, then clears the spin counter. The same content id
    /// is remembered so a later territory change inside that duty is not a new entry.
    /// </summary>
    public DutyPointDecision EnterDuty(uint? currentJobId, int pointsPerDuty, uint contentId)
    {
        lock (gate)
        {
            var landed = HasLandedJob ? LandedJobId : (uint?)null;
            var decision = DutyPoints.Evaluate(SpinsSinceDuty, landed, currentJobId, pointsPerDuty);
            if (decision.PointsAwarded > 0)
            {
                var sum = (long)Points + decision.PointsAwarded;
                Points = (int)Math.Min(int.MaxValue, sum);
            }

            SpinsSinceDuty = 0;
            HasLandedJob = false;
            LandedJobId = 0;
            CurrentDutyContentId = contentId;
            Write();
            return decision;
        }
    }

    public void LeaveDuty()
    {
        lock (gate)
        {
            if (CurrentDutyContentId == 0)
                return;

            CurrentDutyContentId = 0;
            Write();
        }
    }

    private void ReadOrReset()
    {
        if (!File.Exists(path))
            return;

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            var magic = reader.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != (byte)'G' || magic[1] != (byte)'M' || magic[2] != (byte)'B' || magic[3] != (byte)'1')
                throw new InvalidDataException("Duty score file has the wrong header.");

            var version = reader.ReadInt32();
            if (version != Version)
                throw new InvalidDataException($"Duty score file version {version} is not supported.");

            var points = reader.ReadInt32();
            var spins = reader.ReadInt32();
            var hasLanded = reader.ReadBoolean();
            var landedJob = reader.ReadUInt32();
            var dutyId = reader.ReadUInt32();

            if (points < 0 || spins < 0)
                throw new InvalidDataException("Duty score file has a negative count.");

            Points = points;
            SpinsSinceDuty = spins;
            HasLandedJob = hasLanded;
            LandedJobId = hasLanded ? landedJob : 0;
            CurrentDutyContentId = dutyId;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Points = 0;
            SpinsSinceDuty = 0;
            HasLandedJob = false;
            LandedJobId = 0;
            CurrentDutyContentId = 0;
            ResetBecauseUnreadable = true;
        }
    }

    private void Write()
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { (byte)'G', (byte)'M', (byte)'B', (byte)'1' });
            writer.Write(Version);
            writer.Write(Points);
            writer.Write(SpinsSinceDuty);
            writer.Write(HasLandedJob);
            writer.Write(LandedJobId);
            writer.Write(CurrentDutyContentId);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

internal readonly record struct DutyScoreSnapshot(int Points, int SpinsSinceDuty, bool HasLandedJob, uint LandedJobId);
