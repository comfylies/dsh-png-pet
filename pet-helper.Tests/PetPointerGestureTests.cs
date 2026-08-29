using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetPointerGestureTests
{
    [Fact]
    public void Release_without_crossing_a_drag_threshold_requests_the_card()
    {
        var gesture = new PetPointerGesture();
        gesture.Begin(new Point(100, 100), combinedDrag: false);

        Assert.Equal(PetPointerAction.None, gesture.Move(new Point(102, 103), 4, 4));
        Assert.Equal(PetPointerAction.ShowPeakValleyCard, gesture.Release());
    }

    [Fact]
    public void Crossing_either_drag_threshold_starts_a_drag_and_never_shows_the_card()
    {
        var gesture = new PetPointerGesture();
        gesture.Begin(new Point(100, 100), combinedDrag: true);

        Assert.Equal(PetPointerAction.StartDrag, gesture.Move(new Point(105, 100), 4, 4));
        Assert.True(gesture.CombinedDrag);
        Assert.Equal(PetPointerAction.None, gesture.Release());
    }

    [Fact]
    public void A_cancelled_press_does_not_trigger_a_card()
    {
        var gesture = new PetPointerGesture();
        gesture.Begin(new Point(100, 100), combinedDrag: false);
        gesture.Cancel();

        Assert.Equal(PetPointerAction.None, gesture.Release());
    }
}
