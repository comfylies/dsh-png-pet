using System.Windows;

namespace PetHelper;

/// <summary>Pure geometry for the transient peak/valley card beside the pet's head.</summary>
public static class PeakValleyCardPlacement
{
    public const double WidthPerHeight = 2d;

    public static Rect Place(Rect headAnchor, Rect workArea, double headHeight)
    {
        var height = Math.Max(1d, headHeight);
        var size = new Size(height * WidthPerHeight, height);
        return PlacementPlanner.PlaceBeside(
            headAnchor,
            workArea,
            size,
            PlacementPlanner.PetGap,
            [PlaceSide.Right, PlaceSide.Left]);
    }
}
