using Xunit;

namespace XIVJobGamba.Tests;

public class SpinRulesTests
{
    [Fact]
    public void Each_gearset_is_its_own_slice_in_slot_order()
    {
        var gearsets = new[]
        {
            Set(5, 19, 730, "A"),
            Set(1, 21, 740, "B"),
            Set(3, 19, 745, "C"),
        };

        var selected = SpinRules.SelectEntries(gearsets, oneSlicePerJob: false, includeCraftersAndGatherers: true, []);

        Assert.Equal([1, 3, 5], selected.Select(s => s.GearsetId).ToArray());
    }

    [Fact]
    public void One_slice_per_job_keeps_highest_item_level_and_lowest_slot_on_a_tie()
    {
        var gearsets = new[]
        {
            Set(4, 19, 745, "Raid"),
            Set(1, 19, 745, "Older"),
            Set(2, 21, 740, "War"),
            Set(8, 19, 730, "Dungeoning"),
        };

        var selected = SpinRules.SelectEntries(gearsets, oneSlicePerJob: true, includeCraftersAndGatherers: true, []);

        Assert.Equal([1, 2], selected.Select(s => s.GearsetId).ToArray());
        Assert.Equal("Older", selected[0].GearsetName);
    }

    [Fact]
    public void Blacklist_and_crafter_filter_remove_entries()
    {
        var gearsets = new[]
        {
            Set(1, 19, 740, "Pld"),
            Set(2, 8, 100, "Carp", crafter: true),
            Set(3, 21, 740, "War"),
        };

        var blacklist = new[] { "3:War" };
        var selected = SpinRules.SelectEntries(gearsets, false, includeCraftersAndGatherers: false, blacklist);

        Assert.Equal([1], selected.Select(s => s.GearsetId).ToArray());
    }

    [Fact]
    public void Cooldown_blocks_jobs_picked_inside_the_window_and_releases_them_after()
    {
        var selected = new[]
        {
            Set(1, 19, 740, "Pld"),
            Set(2, 21, 740, "War"),
            Set(3, 24, 740, "Whm"),
        };

        var history = new List<uint> { 19, 21, 24 };

        Assert.Empty(SpinRules.Eligible(selected, history, cooldownSpins: 3));
        Assert.True(SpinRules.AllOnCooldown(selected, history, 3));
        Assert.Equal(1, SpinRules.SpinsRemaining(19, history, 3));
        Assert.Equal(2, SpinRules.SpinsRemaining(21, history, 3));
        Assert.Equal(3, SpinRules.SpinsRemaining(24, history, 3));

        // Window slides to [21, 24, 19]. Warrior is the oldest and has one spin left.
        history.Add(19);
        Assert.Empty(SpinRules.Eligible(selected, history, 3));
        Assert.Equal(1, SpinRules.SpinsRemaining(21, history, 3));

        // Window slides to [24, 19, 24]. Warrior has aged out.
        history.Add(24);
        var eligible = SpinRules.Eligible(selected, history, 3);
        Assert.Equal([21], eligible.Select(entry => entry.ClassJobId).ToArray());
        Assert.Equal(0, SpinRules.SpinsRemaining(21, history, 3));
        Assert.Equal(2, SpinRules.SpinsRemaining(19, history, 3));
        Assert.Equal(3, SpinRules.SpinsRemaining(24, history, 3));
    }

    [Fact]
    public void Zero_cooldown_disables_the_window()
    {
        var selected = new[] { Set(1, 19, 740, "Pld") };
        var history = new List<uint> { 19, 19, 19 };

        Assert.Single(SpinRules.Eligible(selected, history, 0));
        Assert.False(SpinRules.AllOnCooldown(selected, history, 0));
        Assert.Equal(0, SpinRules.SpinsRemaining(19, history, 0));
    }

    [Fact]
    public void Duplicate_job_sets_share_one_cooldown()
    {
        var selected = new[]
        {
            Set(1, 19, 745, "Raid"),
            Set(2, 19, 730, "Dungeon"),
            Set(3, 21, 740, "War"),
        };

        var eligible = SpinRules.Eligible(selected, [19], cooldownSpins: 2);

        Assert.Equal([3], eligible.Select(s => s.GearsetId).ToArray());
    }

    [Fact]
    public void Clearing_history_makes_every_selected_job_eligible_again()
    {
        var selected = new[]
        {
            Set(1, 19, 740, "Pld"),
            Set(2, 21, 740, "War"),
        };
        var history = new List<uint> { 19, 21 };
        Assert.True(SpinRules.AllOnCooldown(selected, history, 5));

        history.Clear();

        Assert.Equal(2, SpinRules.Eligible(selected, history, 5).Count);
        Assert.False(SpinRules.AllOnCooldown(selected, history, 5));
    }

    [Fact]
    public void Remember_trims_to_the_history_cap_without_dropping_the_cooldown_window()
    {
        var history = new List<uint>();
        for (var i = 0; i < SpinRules.MaxHistory + 10; i++)
            SpinRules.Remember(history, (uint)(i + 1));

        Assert.Equal(SpinRules.MaxHistory, history.Count);
        Assert.Equal((uint)(SpinRules.MaxHistory + 10), history[^1]);
        Assert.Contains((uint)(SpinRules.MaxHistory + 10 - 3), SpinRules.JobsOnCooldown(history, SpinRules.MaxCooldown));
    }

    [Fact]
    public void Block_reason_distinguishes_login_filters_and_cooldown()
    {
        Assert.Contains("Log in", SpinRules.WhySpinIsBlocked(false, false, 0, 0, 0, 3));
        Assert.Contains("No saved", SpinRules.WhySpinIsBlocked(true, false, 0, 0, 0, 3));
        Assert.Contains("blacklist", SpinRules.WhySpinIsBlocked(true, false, 4, 0, 0, 3));
        Assert.Contains("cooldown", SpinRules.WhySpinIsBlocked(true, false, 4, 2, 0, 3));
        Assert.Null(SpinRules.WhySpinIsBlocked(true, false, 4, 2, 1, 3));
    }

    private static GearsetSnapshot Set(int id, uint job, short itemLevel, string name, bool crafter = false)
        => new(id, job, "J" + job, "Job " + job, name, itemLevel, 62100 + job, WheelRole.Unknown, crafter, id + ":" + name);
}
