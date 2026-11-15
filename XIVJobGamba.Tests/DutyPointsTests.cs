using Xunit;

namespace XIVJobGamba.Tests;

public class DutyPointsTests
{
    [Fact]
    public void Zero_spins_awards_nothing()
    {
        var decision = DutyPoints.Evaluate(0, null, 19, DutyPoints.DefaultPointsPerDuty);

        Assert.Equal(DutyPointOutcome.NoSpins, decision.Outcome);
        Assert.Equal(0, decision.PointsAwarded);
        Assert.False(decision.Awarded);
    }

    [Fact]
    public void One_spin_on_the_same_job_awards_the_configured_amount()
    {
        var decision = DutyPoints.Evaluate(1, 19, 19, 4);

        Assert.Equal(DutyPointOutcome.Awarded, decision.Outcome);
        Assert.Equal(4, decision.PointsAwarded);
        Assert.Equal(1, decision.SpinsSeen);
    }

    [Fact]
    public void Default_award_is_one_point()
    {
        var decision = DutyPoints.Evaluate(1, 21, 21, DutyPoints.DefaultPointsPerDuty);

        Assert.Equal(1, decision.PointsAwarded);
    }

    [Fact]
    public void One_spin_on_a_different_job_awards_nothing()
    {
        var decision = DutyPoints.Evaluate(1, 19, 21, 1);

        Assert.Equal(DutyPointOutcome.WrongJob, decision.Outcome);
        Assert.Equal(0, decision.PointsAwarded);
    }

    [Fact]
    public void One_spin_with_no_current_job_is_not_a_match()
    {
        var decision = DutyPoints.Evaluate(1, 19, null, 1);

        Assert.Equal(DutyPointOutcome.WrongJob, decision.Outcome);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void More_than_one_spin_awards_nothing_even_on_the_landed_job(int spins)
    {
        var decision = DutyPoints.Evaluate(spins, 19, 19, 1);

        Assert.Equal(DutyPointOutcome.TooManySpins, decision.Outcome);
        Assert.Equal(spins, decision.SpinsSeen);
        Assert.Equal(0, decision.PointsAwarded);
    }

    [Fact]
    public void A_zero_award_still_counts_as_a_match()
    {
        var decision = DutyPoints.Evaluate(1, 19, 19, 0);

        Assert.Equal(DutyPointOutcome.Awarded, decision.Outcome);
        Assert.Equal(0, decision.PointsAwarded);
    }

    [Fact]
    public void Entering_a_duty_resets_the_counter_after_scoring()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamba-duty-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var store = DutyScoreStore.Load(path);
            store.RecordSpin(19);
            store.RecordSpin(19);

            var missed = store.EnterDuty(19, 1, contentId: 50);

            Assert.Equal(DutyPointOutcome.TooManySpins, missed.Outcome);
            Assert.Equal(0, store.Points);
            Assert.Equal(0, store.SpinsSinceDuty);

            store.RecordSpin(21);
            var awarded = store.EnterDuty(21, 3, contentId: 80);

            Assert.Equal(DutyPointOutcome.Awarded, awarded.Outcome);
            Assert.Equal(3, store.Points);
            Assert.Equal(0, store.SpinsSinceDuty);
            Assert.False(store.Snapshot().HasLandedJob);
            Assert.Equal(80u, store.CurrentDutyContentId);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Points_and_the_pending_spin_survive_a_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamba-duty-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var store = DutyScoreStore.Load(path);
            store.RecordSpin(24);
            store.EnterDuty(24, 2, contentId: 10);
            store.RecordSpin(32);

            var reloaded = DutyScoreStore.Load(path);
            var snapshot = reloaded.Snapshot();

            Assert.Equal(2, snapshot.Points);
            Assert.Equal(1, snapshot.SpinsSinceDuty);
            Assert.True(snapshot.HasLandedJob);
            Assert.Equal(32u, snapshot.LandedJobId);
            Assert.Equal(10u, reloaded.CurrentDutyContentId);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_file_starts_at_zero()
    {
        var path = Path.Combine(Path.GetTempPath(), "gamba-duty-missing-" + Guid.NewGuid().ToString("N") + ".bin");
        var store = DutyScoreStore.Load(path);
        var snapshot = store.Snapshot();

        Assert.Equal(0, snapshot.Points);
        Assert.Equal(0, snapshot.SpinsSinceDuty);
        Assert.False(snapshot.HasLandedJob);
        Assert.False(store.ResetBecauseUnreadable);
    }

    [Fact]
    public void A_short_file_starts_at_zero()
    {
        AssertCorrupt(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void An_unknown_version_starts_at_zero()
    {
        AssertCorrupt(new byte[] { (byte)'G', (byte)'M', (byte)'B', (byte)'1', 2, 0, 0, 0 });
    }

    private static void AssertCorrupt(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "gamba-duty-bad-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, bytes);
            var store = DutyScoreStore.Load(path);

            Assert.True(store.ResetBecauseUnreadable);
            Assert.Equal(0, store.Points);
            Assert.Equal(0, store.SpinsSinceDuty);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
