using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PeakValleyScheduleTests
{
    [Theory]
    [InlineData(2026, 8, 24, 8, 59, PeakValleyPeriod.Valley)]
    [InlineData(2026, 8, 24, 9, 0, PeakValleyPeriod.Peak)]
    [InlineData(2026, 8, 24, 11, 59, PeakValleyPeriod.Peak)]
    [InlineData(2026, 8, 24, 12, 0, PeakValleyPeriod.Valley)]
    [InlineData(2026, 8, 24, 14, 0, PeakValleyPeriod.Peak)]
    [InlineData(2026, 8, 24, 18, 0, PeakValleyPeriod.Valley)]
    public void Weekday_periods_follow_the_announced_peak_schedule(
        int year, int month, int day, int hour, int minute, PeakValleyPeriod expected)
    {
        Assert.Equal(expected, PeakValleySchedule.AtBeijing(new DateTime(year, month, day, hour, minute, 0)));
    }

    [Theory]
    [InlineData(2026, 8, 23, 10, 0)] // Sunday: rule effective date from the announcement.
    [InlineData(2026, 8, 29, 15, 0)] // Saturday during what would otherwise be a peak interval.
    [InlineData(2026, 8, 30, 3, 0)]
    public void Weekends_are_valley_all_day(int year, int month, int day, int hour, int minute)
    {
        Assert.Equal(PeakValleyPeriod.Valley, PeakValleySchedule.AtBeijing(new DateTime(year, month, day, hour, minute, 0)));
    }

    [Fact]
    public void Card_is_twice_as_wide_as_its_head_height_and_prefers_the_right()
    {
        var pet = new Rect(300, 200, 220, 260);
        var workArea = new Rect(0, 0, 1920, 1040);

        var card = PeakValleyCardPlacement.Place(pet, workArea, headHeight: 96);

        Assert.Equal(192, card.Width);
        Assert.Equal(96, card.Height);
        Assert.Equal(pet.Right + PlacementPlanner.PetGap, card.Left);
    }

    [Fact]
    public void Card_never_shrinks_below_the_readable_minimum()
    {
        var pet = new Rect(300, 200, 220, 260);
        var workArea = new Rect(0, 0, 1920, 1040);

        // The 75% pet scale makes the head ~69px tall; the card must not follow it down,
        // or the period text gets clipped.
        var card = PeakValleyCardPlacement.Place(pet, workArea, headHeight: 69);

        Assert.Equal(PeakValleyCardPlacement.MinCardHeight, card.Height);
        Assert.Equal(PeakValleyCardPlacement.MinCardHeight * PeakValleyCardPlacement.WidthPerHeight, card.Width);
        Assert.Equal(pet.Right + PlacementPlanner.PetGap, card.Left);
    }

    [Fact]
    public void Card_flips_to_the_left_when_the_right_side_has_no_room()
    {
        var pet = new Rect(1740, 200, 220, 260);
        var workArea = new Rect(0, 0, 1920, 1040);

        var card = PeakValleyCardPlacement.Place(pet, workArea, headHeight: 72);

        Assert.True(card.Right <= pet.Left - PlacementPlanner.PetGap);
        Assert.True(card.Left >= workArea.Left + PlacementPlanner.ScreenMargin);
    }
}
