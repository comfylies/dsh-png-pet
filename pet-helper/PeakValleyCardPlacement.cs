using System.Windows;

namespace PetHelper;

/// <summary>Pure geometry for the transient peak/valley card beside the pet's head.</summary>
public static class PeakValleyCardPlacement
{
    public const double WidthPerHeight = 2d;

    /// <summary>
    /// Smallest card that still fits the fixed "现在是：" row, the period label and the
    /// close button. Without a floor the card shrinks with the pet's scale and clips the
    /// period text (e.g. the 75% pet scale makes the head ~69px tall, which is too short).
    /// </summary>
    public const double MinCardHeight = 84d;

    public static Rect Place(Rect headAnchor, Rect workArea, double headHeight)
    {
        var height = Math.Max(MinCardHeight, headHeight);
        var size = new Size(height * WidthPerHeight, height);
        return PlacementPlanner.PlaceBeside(
            headAnchor,
            workArea,
            size,
            PlacementPlanner.PetGap,
            [PlaceSide.Right, PlaceSide.Left]);
    }
}
