using Xunit;

namespace XIVJobGamba.Tests;

public class WheelMathTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(40)]
    public void Center_rotation_puts_the_pointer_in_that_slice(int count)
    {
        var slice = MathF.Tau / count;
        for (var index = 0; index < count; index++)
        {
            foreach (var jitter in new[] { 0f, slice * 0.22f, -slice * 0.22f })
            {
                var rotation = WheelMath.CenterRotation(index, count, jitter);
                Assert.Equal(index, WheelMath.SliceIndexAtPointer(rotation, count));

                var centerAngle = rotation + ((index + 0.5f) * slice) + jitter;
                Assert.Equal(index, WheelMath.SliceIndexAtAngle(centerAngle, rotation, count));
            }
        }
    }

    [Fact]
    public void Spin_decelerates_and_lands_in_the_chosen_slice()
    {
        foreach (var count in new[] { 1, 3, 8, 21 })
        {
            for (var winner = 0; winner < count; winner++)
            {
                var animation = new SpinAnimation();
                animation.SetRotation(0.4f);
                var slice = MathF.Tau / count;
                animation.Start(count, winner, now: 0f, durationSeconds: 2f, extraTurns: 5.5f, jitter: slice * 0.15f);

                Assert.True(animation.IsSpinning);
                Assert.False(animation.Advance(0.4f));
                Assert.True(animation.Rotation > 0.4f);
                Assert.False(animation.Advance(1.2f));
                Assert.True(animation.Advance(2f));
                Assert.False(animation.IsSpinning);
                Assert.Equal(winner, WheelMath.SliceIndexAtPointer(animation.Rotation, count));
                Assert.False(animation.Advance(3f));
            }
        }
    }

    [Fact]
    public void Ease_out_covers_more_distance_in_the_first_half_than_the_second()
    {
        var animation = new SpinAnimation();
        animation.Start(4, 2, now: 0f, durationSeconds: 2f, extraTurns: 4f, jitter: 0f);
        var start = animation.Rotation;
        animation.Advance(1f);
        var mid = animation.Rotation;
        animation.Advance(2f);
        var end = animation.Rotation;

        var firstHalf = mid - start;
        var secondHalf = end - mid;
        Assert.True(firstHalf > secondHalf);
    }
}
