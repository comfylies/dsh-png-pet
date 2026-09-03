using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PhysicsFlightStatsTests
{
    [Fact]
    public void Add_accumulates_minimum_average_and_maximum_intervals()
    {
        var stats = new PhysicsFlightStats();
        stats.Add(intervalMs: 8d, windowMoveMs: 0.5d);
        stats.Add(intervalMs: 24d, windowMoveMs: 1d);
        stats.Add(intervalMs: 12d, windowMoveMs: 3d);

        var snapshot = stats.Snapshot();

        Assert.Equal(3, snapshot.TickCount);
        Assert.Equal(8d, snapshot.IntervalMinMs);
        Assert.Equal(44d / 3d, snapshot.IntervalAvgMs, 3);
        Assert.Equal(24d, snapshot.IntervalMaxMs);
        Assert.Equal(1, snapshot.SlowTickCount);
    }

    [Fact]
    public void Add_counts_only_intervals_strictly_slower_than_the_frame_budget()
    {
        var stats = new PhysicsFlightStats();

        stats.Add(intervalMs: PhysicsFlightStats.SlowFrameThresholdMs, windowMoveMs: 0d);
        stats.Add(intervalMs: PhysicsFlightStats.SlowFrameThresholdMs + 0.001d, windowMoveMs: 0d);

        Assert.Equal(1, stats.Snapshot().SlowTickCount);
    }

    [Fact]
    public void Add_tracks_average_and_maximum_window_move_duration()
    {
        var stats = new PhysicsFlightStats();
        stats.Add(intervalMs: 10d, windowMoveMs: 2d);
        stats.Add(intervalMs: 10d, windowMoveMs: 5d);

        var snapshot = stats.Snapshot();

        Assert.Equal(3.5d, snapshot.MoveAvgMs, 3);
        Assert.Equal(5d, snapshot.MoveMaxMs);
    }

    [Fact]
    public void Reset_clears_every_accumulated_value()
    {
        var stats = new PhysicsFlightStats();
        stats.Add(intervalMs: 40d, windowMoveMs: 9d);
        Assert.False(stats.IsEmpty);

        stats.Reset();

        Assert.True(stats.IsEmpty);
        var snapshot = stats.Snapshot();
        Assert.Equal(0, snapshot.TickCount);
        Assert.Equal(0d, snapshot.IntervalMinMs);
        Assert.Equal(0d, snapshot.IntervalAvgMs);
        Assert.Equal(0d, snapshot.IntervalMaxMs);
        Assert.Equal(0, snapshot.SlowTickCount);
        Assert.Equal(0d, snapshot.MoveAvgMs);
        Assert.Equal(0d, snapshot.MoveMaxMs);
    }
}
