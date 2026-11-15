using System.Numerics;
using Xunit;

namespace XIVJobGamba.Tests;

public class RoleColorTests
{
    [Fact]
    public void JobType_picks_the_promoted_combat_role()
    {
        Assert.Equal(WheelRole.Tank, Classify(jobType: 1));
        Assert.Equal(WheelRole.Healer, Classify(jobType: 2));
        Assert.Equal(WheelRole.Healer, Classify(jobType: 6));
        Assert.Equal(WheelRole.Melee, Classify(jobType: 3));
        Assert.Equal(WheelRole.PhysicalRanged, Classify(jobType: 4));
        Assert.Equal(WheelRole.MagicalRanged, Classify(jobType: 5));
    }

    [Fact]
    public void Base_classes_use_role_and_discipline_when_job_type_is_unset()
    {
        Assert.Equal(WheelRole.Tank, Classify(role: 1, war: true));
        Assert.Equal(WheelRole.Melee, Classify(role: 2, war: true));
        Assert.Equal(WheelRole.Healer, Classify(role: 4, magic: true));
        Assert.Equal(WheelRole.PhysicalRanged, Classify(role: 3, war: true));
        Assert.Equal(WheelRole.MagicalRanged, Classify(role: 3, magic: true));
    }

    [Fact]
    public void Ranged_role_falls_back_to_primary_stat_when_the_discipline_is_mixed()
    {
        Assert.Equal(
            WheelRole.MagicalRanged,
            Classify(role: 3, war: true, magic: true, primary: JobRoleClassifier.PrimaryIntelligence));
        Assert.Equal(
            WheelRole.PhysicalRanged,
            Classify(role: 3, war: true, magic: true, primary: JobRoleClassifier.PrimaryDexterity));
    }

    [Fact]
    public void Hand_and_land_categories_beat_a_shared_doh_dol_index()
    {
        Assert.Equal(WheelRole.Crafter, Classify(hand: true, doh: 0, ui: 101));
        Assert.Equal(WheelRole.Gatherer, Classify(land: true, doh: 0, ui: 201));
    }

    [Fact]
    public void Ui_priority_splits_hand_and_land_when_the_category_row_is_missing()
    {
        Assert.Equal(WheelRole.Crafter, Classify(doh: 0, ui: 101));
        Assert.Equal(WheelRole.Gatherer, Classify(doh: 0, ui: 201));
        Assert.Equal(WheelRole.Unknown, Classify(doh: -1, ui: 0));
    }

    [Fact]
    public void Adventurer_stays_neutral_even_on_the_war_category()
    {
        Assert.Equal(WheelRole.Unknown, Classify(war: true));
    }

    [Fact]
    public void Role_colors_match_the_requested_hues()
    {
        AssertChannel(WheelRole.Tank, strongest: Channel.Blue);
        AssertChannel(WheelRole.Healer, strongest: Channel.Green);
        AssertChannel(WheelRole.Melee, strongest: Channel.Red);
        AssertYellow(JobPalette.Base(WheelRole.PhysicalRanged));
        AssertPurple(JobPalette.Base(WheelRole.MagicalRanged));

        var crafter = JobPalette.Base(WheelRole.Crafter);
        var gatherer = JobPalette.Base(WheelRole.Gatherer);
        AssertGrey(crafter);
        AssertGrey(gatherer);
        Assert.True(JobPalette.RelativeLuminance(crafter) > JobPalette.RelativeLuminance(gatherer) + 0.12f);
    }

    [Fact]
    public void Adjacent_slices_of_the_same_role_use_different_shades()
    {
        var roles = new[]
        {
            WheelRole.Tank,
            WheelRole.Tank,
            WheelRole.Melee,
            WheelRole.Melee,
            WheelRole.Healer,
        };
        var shades = JobPalette.AlternatingShades(roles);

        Assert.NotEqual(shades[0], shades[1]);
        Assert.NotEqual(shades[2], shades[3]);
        Assert.Equal(0, shades[2]);
        Assert.True(Distance(Color(roles[0], shades[0]), Color(roles[1], shades[1])) > 0.12f);
    }

    [Fact]
    public void An_odd_ring_of_one_role_still_separates_the_wrap()
    {
        var roles = new[] { WheelRole.Melee, WheelRole.Melee, WheelRole.Melee };
        var shades = JobPalette.AlternatingShades(roles);

        Assert.NotEqual(shades[0], shades[1]);
        Assert.NotEqual(shades[1], shades[2]);
        Assert.NotEqual(shades[2], shades[0]);
    }

    [Fact]
    public void Slice_text_stays_readable_including_muted_shades()
    {
        foreach (var role in Enum.GetValues<WheelRole>())
        {
            foreach (var shade in new[] { 0, 1, 2 })
            {
                foreach (var muted in new[] { false, true })
                {
                    var background = JobPalette.SliceColor(role, shade, muted);
                    var text = JobPalette.TextColor(background);
                    Assert.True(
                        JobPalette.Contrast(text, background) >= 4.5f,
                        $"{role} shade {shade} muted {muted} contrast {JobPalette.Contrast(text, background):0.00}");
                }
            }
        }
    }

    private static WheelRole Classify(
        byte role = 0,
        byte jobType = 0,
        bool hand = false,
        bool land = false,
        bool war = false,
        bool magic = false,
        byte primary = 0,
        byte ui = 0,
        sbyte doh = -1)
        => JobRoleClassifier.Classify(new JobRoleSignals(role, jobType, doh, primary, ui, hand, land, war, magic));

    private static Vector4 Color(WheelRole role, int shade)
        => JobPalette.SliceColor(role, shade, muted: false);

    private static float Distance(Vector4 a, Vector4 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static void AssertChannel(WheelRole role, Channel strongest)
    {
        var color = JobPalette.Base(role);
        var value = strongest switch
        {
            Channel.Red => color.X,
            Channel.Green => color.Y,
            _ => color.Z,
        };
        Assert.True(value > color.X - 0.001f || strongest == Channel.Red);
        Assert.True(value > color.Y - 0.001f || strongest == Channel.Green);
        Assert.True(value > color.Z - 0.001f || strongest == Channel.Blue);
        Assert.True(value > 0.55f);
    }

    private static void AssertYellow(Vector4 color)
    {
        Assert.True(color.X > 0.75f && color.Y > 0.6f && color.Z < 0.35f);
    }

    private static void AssertPurple(Vector4 color)
    {
        Assert.True(color.X > 0.4f && color.Z > 0.65f && color.Y < color.X && color.Y < color.Z);
    }

    private static void AssertGrey(Vector4 color)
    {
        Assert.True(MathF.Abs(color.X - color.Y) < 0.04f);
        Assert.True(MathF.Abs(color.Y - color.Z) < 0.04f);
    }

    private enum Channel
    {
        Red,
        Green,
        Blue,
    }
}
