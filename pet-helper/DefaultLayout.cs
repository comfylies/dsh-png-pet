using System.Windows;

namespace PetHelper;

/// <summary>
/// Shared, display-independent interpretation of the defaults saved by Harness. Positions are
/// semantic anchors rather than pixels so a setting survives monitor changes and DPI scaling.
/// </summary>
public static class DefaultLayout
{
    public const string Center = "center";
    public const string TopLeft = "top-left";
    public const string TopCenter = "top-center";
    public const string TopRight = "top-right";
    public const string MiddleLeft = "middle-left";
    public const string MiddleRight = "middle-right";
    public const string BottomLeft = "bottom-left";
    public const string BottomCenter = "bottom-center";
    public const string BottomRight = "bottom-right";
    public const string NearPet = "near-pet";

    public static bool IsPetPlacement(string value) => value is
        Center or
        TopLeft or TopCenter or TopRight or
        MiddleLeft or MiddleRight or
        BottomLeft or BottomCenter or BottomRight;

    public static bool IsDialoguePlacement(string value) => value == NearPet || IsPetPlacement(value);

    /// <summary>Places a requested rectangle within a work area, retaining the standard safe margin.</summary>
    public static Rect Place(string placement, Rect workArea, Size requestedSize)
    {
        var fitted = PlacementPlanner.FitIntoWorkArea(
            new Rect(workArea.X, workArea.Y, requestedSize.Width, requestedSize.Height),
            workArea,
            new Size(0d, 0d));
        var x = placement switch
        {
            TopLeft or MiddleLeft or BottomLeft => workArea.Left + PlacementPlanner.ScreenMargin,
            TopRight or MiddleRight or BottomRight => workArea.Right - PlacementPlanner.ScreenMargin - fitted.Width,
            _ => workArea.Left + (workArea.Width - fitted.Width) / 2d,
        };
        var y = placement switch
        {
            TopLeft or TopCenter or TopRight => workArea.Top + PlacementPlanner.ScreenMargin,
            BottomLeft or BottomCenter or BottomRight => workArea.Bottom - PlacementPlanner.ScreenMargin - fitted.Height,
            _ => workArea.Top + (workArea.Height - fitted.Height) / 2d,
        };
        return PlacementPlanner.ClampIntoWorkArea(new Rect(x, y, fitted.Width, fitted.Height), workArea);
    }
}
