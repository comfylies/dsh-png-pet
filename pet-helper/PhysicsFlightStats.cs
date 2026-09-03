namespace PetHelper;

/// <summary>
/// Bounded numeric statistics for one physics flight (the toss/bounce phase after a drag
/// release). Diagnostics only: it counts frame intervals and window-move durations and never
/// carries conversation content, paths, or credentials.
/// </summary>
public sealed class PhysicsFlightStats
{
    /// <summary>Rough 60 fps frame budget; ticks slower than this are counted as slow frames.</summary>
    public const double SlowFrameThresholdMs = 17d;

    private long tickCount;
    private double intervalSumMs;
    private double intervalMinMs = double.PositiveInfinity;
    private double intervalMaxMs;
    private int slowTickCount;
    private double moveSumMs;
    private double moveMaxMs;

    public bool IsEmpty => tickCount == 0;

    public void Reset()
    {
        tickCount = 0;
        intervalSumMs = 0d;
        intervalMinMs = double.PositiveInfinity;
        intervalMaxMs = 0d;
        slowTickCount = 0;
        moveSumMs = 0d;
        moveMaxMs = 0d;
    }

    /// <summary>Records one simulation tick: the real interval since the previous tick and the
    /// time the single window reposition took inside that tick's budget.</summary>
    public void Add(double intervalMs, double windowMoveMs)
    {
        tickCount++;
        intervalSumMs += intervalMs;
        intervalMinMs = Math.Min(intervalMinMs, intervalMs);
        intervalMaxMs = Math.Max(intervalMaxMs, intervalMs);
        if (intervalMs > SlowFrameThresholdMs)
        {
            slowTickCount++;
        }
        moveSumMs += windowMoveMs;
        moveMaxMs = Math.Max(moveMaxMs, windowMoveMs);
    }

    public PhysicsFlightSnapshot Snapshot() => new(
        TickCount: tickCount,
        IntervalMinMs: intervalMinMs == double.PositiveInfinity ? 0d : intervalMinMs,
        IntervalAvgMs: tickCount == 0 ? 0d : intervalSumMs / tickCount,
        IntervalMaxMs: intervalMaxMs,
        SlowTickCount: slowTickCount,
        MoveAvgMs: tickCount == 0 ? 0d : moveSumMs / tickCount,
        MoveMaxMs: moveMaxMs);
}

public readonly record struct PhysicsFlightSnapshot(
    long TickCount,
    double IntervalMinMs,
    double IntervalAvgMs,
    double IntervalMaxMs,
    int SlowTickCount,
    double MoveAvgMs,
    double MoveMaxMs);
