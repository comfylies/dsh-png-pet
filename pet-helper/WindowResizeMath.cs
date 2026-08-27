using System.Windows;
using System.Windows.Input;

namespace PetHelper;

[Flags]
public enum ResizeEdge
{
    None = 0,
    West = 1 << 0,
    East = 1 << 1,
    North = 1 << 2,
    South = 1 << 3,
    NorthWest = North | West,
    NorthEast = North | East,
    SouthWest = South | West,
    SouthEast = South | East,
}

/// <summary>
/// Pure resize geometry for a transparent borderless window: edge/corner hit testing and
/// the rect math that turns a mouse delta into a resized, clamped window rect.
/// </summary>
public static class WindowResizeMath
{
    /// <summary>Width of the interactive resize zone along each window edge, in DIPs.</summary>
    public const double ResizeSensitivity = 8d;

    /// <summary>
    /// Maps a window-local point to the resize edge or corner it sits on. Corner zones take
    /// priority over plain edges; points outside the window (within the sensitivity band)
    /// still resolve so the grab does not need pixel precision.
    /// </summary>
    public static ResizeEdge HitTest(Point point, Rect windowRect, double sensitivity = ResizeSensitivity)
    {
        var nearWest = point.X >= windowRect.Left - sensitivity && point.X <= windowRect.Left + sensitivity;
        var nearEast = point.X >= windowRect.Right - sensitivity && point.X <= windowRect.Right + sensitivity;
        var nearNorth = point.Y >= windowRect.Top - sensitivity && point.Y <= windowRect.Top + sensitivity;
        var nearSouth = point.Y >= windowRect.Bottom - sensitivity && point.Y <= windowRect.Bottom + sensitivity;

        var horizontal = nearWest ? ResizeEdge.West : nearEast ? ResizeEdge.East : ResizeEdge.None;
        var vertical = nearNorth ? ResizeEdge.North : nearSouth ? ResizeEdge.South : ResizeEdge.None;
        return horizontal | vertical;
    }

    /// <summary>
    /// Computes the resized rect for a drag that started at <paramref name="start"/> and moved
    /// by <paramref name="delta"/> while holding <paramref name="edge"/>. Size is clamped to
    /// [<paramref name="minSize"/>, <paramref name="maxSize"/>] and the final rect is fitted
    /// into <paramref name="workArea"/> with <paramref name="margin"/> on every side.
    /// </summary>
    public static Rect ResizeFrom(
        Rect start,
        Vector delta,
        ResizeEdge edge,
        Size minSize,
        Size maxSize,
        Rect workArea,
        double margin)
    {
        var width = start.Width;
        var height = start.Height;
        var left = start.Left;
        var top = start.Top;

        if ((edge & ResizeEdge.East) != 0) width = start.Width + delta.X;
        if ((edge & ResizeEdge.West) != 0) width = start.Width - delta.X;
        if ((edge & ResizeEdge.South) != 0) height = start.Height + delta.Y;
        if ((edge & ResizeEdge.North) != 0) height = start.Height - delta.Y;

        width = Math.Clamp(width, minSize.Width, maxSize.Width);
        height = Math.Clamp(height, minSize.Height, maxSize.Height);

        if ((edge & ResizeEdge.West) != 0) left = start.Right - width;
        if ((edge & ResizeEdge.North) != 0) top = start.Bottom - height;

        return PlacementPlanner.FitIntoWorkArea(new Rect(left, top, width, height), workArea, minSize, margin);
    }

    /// <summary>Mouse cursor for the given resize edge; <see cref="Cursors.Arrow"/> when not resizing.</summary>
    public static Cursor ResizeCursor(ResizeEdge edge)
    {
        return edge switch
        {
            ResizeEdge.North or ResizeEdge.South => Cursors.SizeNS,
            ResizeEdge.West or ResizeEdge.East => Cursors.SizeWE,
            ResizeEdge.NorthWest or ResizeEdge.SouthEast => Cursors.SizeNWSE,
            ResizeEdge.NorthEast or ResizeEdge.SouthWest => Cursors.SizeNESW,
            _ => Cursors.Arrow,
        };
    }
}
