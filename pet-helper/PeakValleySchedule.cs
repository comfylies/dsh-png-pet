namespace PetHelper;

public enum PeakValleyPeriod
{
    Peak,
    Valley,
}

/// <summary>
/// DeepSeek API peak/valley schedule, evaluated entirely locally in Beijing civil time.
/// Since 2026-08-23, Saturday and Sunday are valley all day; weekday peaks are 09:00–12:00
/// and 14:00–18:00. Keep this table local and explicit so no account, usage, or network data
/// is required to render the pet card.
/// </summary>
public static class PeakValleySchedule
{
    private static readonly TimeZoneInfo BeijingTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    public static PeakValleyPeriod Current() => AtBeijing(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BeijingTimeZone).DateTime);

    public static PeakValleyPeriod AtBeijing(DateTime beijingTime)
    {
        if (beijingTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return PeakValleyPeriod.Valley;
        }

        var time = TimeOnly.FromDateTime(beijingTime);
        return IsWithin(time, new TimeOnly(9, 0), new TimeOnly(12, 0))
            || IsWithin(time, new TimeOnly(14, 0), new TimeOnly(18, 0))
            ? PeakValleyPeriod.Peak
            : PeakValleyPeriod.Valley;
    }

    private static bool IsWithin(TimeOnly value, TimeOnly startInclusive, TimeOnly endExclusive) =>
        value >= startInclusive && value < endExclusive;
}
