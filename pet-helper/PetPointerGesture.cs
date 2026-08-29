using System.Windows;

namespace PetHelper;

public enum PetPointerAction
{
    None,
    StartDrag,
    ShowPeakValleyCard,
}

/// <summary>
/// Separates a short click from a drag without depending on WPF input events. The caller owns
/// mouse capture and supplies the system drag thresholds.
/// </summary>
public sealed class PetPointerGesture
{
    private Point pressPoint;
    private bool pending;
    private bool dragging;

    public bool CombinedDrag { get; private set; }

    public void Begin(Point point, bool combinedDrag)
    {
        pressPoint = point;
        CombinedDrag = combinedDrag;
        pending = true;
        dragging = false;
    }

    public PetPointerAction Move(Point point, double horizontalThreshold, double verticalThreshold)
    {
        if (!pending || dragging) return PetPointerAction.None;
        if (Math.Abs(point.X - pressPoint.X) < horizontalThreshold
            && Math.Abs(point.Y - pressPoint.Y) < verticalThreshold)
        {
            return PetPointerAction.None;
        }

        dragging = true;
        pending = false;
        return PetPointerAction.StartDrag;
    }

    public PetPointerAction Release()
    {
        var action = pending && !dragging ? PetPointerAction.ShowPeakValleyCard : PetPointerAction.None;
        Cancel();
        return action;
    }

    public void Cancel()
    {
        pending = false;
        dragging = false;
        CombinedDrag = false;
    }
}
