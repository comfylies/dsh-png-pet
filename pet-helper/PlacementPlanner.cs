using System.Windows;

namespace PetHelper;

public enum PlaceSide
{
    Right,
    Left,
    Above,
    Below,
}

/// <summary>
/// Pure window-placement geometry: clamping into a work area, smart "beside the pet"
/// placement with edge/corner awareness, and restoration correction across monitors.
/// No UI or Win32 dependencies so every branch is unit-testable.
/// </summary>
public static class PlacementPlanner
{
    /// <summary>Minimum distance any window edge keeps from the work-area border.</summary>
    public const double ScreenMargin = 4d;

    /// <summary>Gap between the pet window and a beside-placed dialogue window.</summary>
    public const double PetGap = 8d;

    /// <summary>Side preference used when the caller does not supply one.</summary>
    public static readonly IReadOnlyList<PlaceSide> DefaultSideOrder =
        [PlaceSide.Right, PlaceSide.Left, PlaceSide.Above, PlaceSide.Below];

    /// <summary>
    /// Position-only clamp: keeps the rect's size and shifts it so it lies fully inside
    /// the work area with <paramref name="margin"/> on every side. When the rect is wider
    /// or taller than the available space the position is pinned to the top-left margin.
    /// </summary>
    public static Rect ClampIntoWorkArea(Rect rect, Rect workArea, double margin = ScreenMargin)
    {
        var availableWidth = workArea.Width - 2 * margin;
        var availableHeight = workArea.Height - 2 * margin;
        var x = rect.Width > availableWidth
            ? workArea.X + margin
            : Math.Clamp(rect.X, workArea.X + margin, workArea.Right - margin - rect.Width);
        var y = rect.Height > availableHeight
            ? workArea.Y + margin
            : Math.Clamp(rect.Y, workArea.Y + margin, workArea.Bottom - margin - rect.Height);
        return new Rect(x, y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Size-aware fit: shrinks the rect when it exceeds the work area (never below
    /// <paramref name="minSize"/> unless the work area itself is smaller), then clamps
    /// the position into the work area with <paramref name="margin"/> on every side.
    /// </summary>
    public static Rect FitIntoWorkArea(Rect rect, Rect workArea, Size minSize, double margin = ScreenMargin)
    {
        var availableWidth = Math.Max(0d, workArea.Width - 2 * margin);
        var availableHeight = Math.Max(0d, workArea.Height - 2 * margin);
        var width = Math.Min(rect.Width, availableWidth);
        var height = Math.Min(rect.Height, availableHeight);
        if (availableWidth >= minSize.Width) width = Math.Max(width, minSize.Width);
        if (availableHeight >= minSize.Height) height = Math.Max(height, minSize.Height);
        var x = Math.Clamp(rect.X, workArea.X + margin, workArea.Right - margin - width);
        var y = Math.Clamp(rect.Y, workArea.Y + margin, workArea.Bottom - margin - height);
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Places a window of <paramref name="size"/> beside <paramref name="anchor"/> with a
    /// <paramref name="gap"/>, trying <paramref name="preferences"/> in order. A side wins as
    /// soon as the window size fits the work area and the anchor-side axis has room; the
    /// perpendicular axis is then clamped into the work area (so a pet at the very top or
    /// bottom edge still gets its preferred side). Falls back to fitting the preferred-side
    /// candidate into the work area when no side has room at all.
    /// </summary>
    public static Rect PlaceBeside(
        Rect anchor,
        Rect workArea,
        Size size,
        double gap,
        IReadOnlyList<PlaceSide>? preferences = null)
    {
        var order = preferences is { Count: > 0 } ? preferences : DefaultSideOrder;
        Rect? fallback = null;
        foreach (var side in order)
        {
            var candidate = SideRect(anchor, size, gap, side);
            fallback ??= candidate;
            var fitted = FitOnSide(candidate, workArea, side, ScreenMargin);
            if (fitted is { } rect) return rect;
        }
        return FitIntoWorkArea(fallback ?? new Rect(anchor.X, anchor.Y, size.Width, size.Height), workArea, size);
    }

    /// <summary>
    /// Position-only clamp with edge protrusion: the window may sit partially outside the
    /// work area by up to <see cref="EdgeProtrusion"/> on each side (classic "dock at the
    /// screen edge" feel). Dragging beyond the protrusion limit snaps the window back to it.
    /// When the work area is too small even with both protrusions the window is centered.
    /// </summary>
    public static Rect ClampIntoWorkAreaWithProtrusion(Rect rect, Rect workArea, EdgeProtrusion protrusion)
    {
        var minX = workArea.Left - protrusion.Left;
        var maxX = workArea.Right - rect.Width + protrusion.Right;
        var minY = workArea.Top - protrusion.Top;
        var maxY = workArea.Bottom - rect.Height + protrusion.Bottom;

        var x = minX <= maxX
            ? Math.Clamp(rect.X, minX, maxX)
            : workArea.Left + (workArea.Width - rect.Width) / 2;
        var y = minY <= maxY
            ? Math.Clamp(rect.Y, minY, maxY)
            : workArea.Top + (workArea.Height - rect.Height) / 2;
        return new Rect(x, y, rect.Width, rect.Height);
    }

    /// <summary>Per-side protrusion in DIPs: how far a window may extend past a work-area edge.</summary>
    public readonly record struct EdgeProtrusion(double Left, double Top, double Right, double Bottom);

    /// <summary>
    /// The work area whose center is closest to <paramref name="rect"/>'s center. Used when
    /// restoring a saved position: the window lands on (or is pulled into) the monitor it
    /// was last on, even after monitors are added, removed, or re-arranged.
    /// </summary>
    public static Rect NearestWorkArea(Rect rect, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0) return rect;
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var nearest = workAreas[0];
        var bestSquared = double.PositiveInfinity;
        foreach (var area in workAreas)
        {
            var areaCenter = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            var dx = center.X - areaCenter.X;
            var dy = center.Y - areaCenter.Y;
            var squared = dx * dx + dy * dy;
            if (squared < bestSquared)
            {
                bestSquared = squared;
                nearest = area;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Restores a previously saved window rect: when it lies on any known work area it is
    /// fitted into that area; when the monitor was unplugged or the resolution changed it is
    /// pulled into the nearest work area by center distance.
    /// </summary>
    public static Rect CorrectRestoredPosition(
        Rect saved,
        IReadOnlyList<Rect> workAreas,
        Size minSize,
        double margin = ScreenMargin)
    {
        if (workAreas.Count == 0) return saved;
        return FitIntoWorkArea(saved, NearestWorkArea(saved, workAreas), minSize, margin);
    }

    private static Rect SideRect(Rect anchor, Size size, double gap, PlaceSide side)
    {
        return side switch
        {
            PlaceSide.Right => new Rect(anchor.Right + gap, VerticalCenter(anchor, size.Height), size.Width, size.Height),
            PlaceSide.Left => new Rect(anchor.Left - gap - size.Width, VerticalCenter(anchor, size.Height), size.Width, size.Height),
            PlaceSide.Above => new Rect(HorizontalCenter(anchor, size.Width), anchor.Top - gap - size.Height, size.Width, size.Height),
            PlaceSide.Below => new Rect(HorizontalCenter(anchor, size.Width), anchor.Bottom + gap, size.Width, size.Height),
            _ => new Rect(anchor.Right + gap, VerticalCenter(anchor, size.Height), size.Width, size.Height),
        };
    }

    /// <summary>
    /// Accepts a side when the window size fits the work area and the anchor-side axis has
    /// room; clamps the perpendicular axis into the work area and returns the result, or
    /// <c>null</c> when the side itself has no room.
    /// </summary>
    private static Rect? FitOnSide(Rect candidate, Rect workArea, PlaceSide side, double margin)
    {
        if (candidate.Width > workArea.Width - 2 * margin || candidate.Height > workArea.Height - 2 * margin)
        {
            return null;
        }

        var minY = workArea.Y + margin;
        var maxY = workArea.Bottom - margin - candidate.Height;
        var minX = workArea.X + margin;
        var maxX = workArea.Right - margin - candidate.Width;

        switch (side)
        {
            case PlaceSide.Right:
                if (candidate.Right > workArea.Right - margin || candidate.X < workArea.X + margin) return null;
                return new Rect(candidate.X, Math.Clamp(candidate.Y, minY, maxY), candidate.Width, candidate.Height);
            case PlaceSide.Left:
                if (candidate.X < workArea.X + margin || candidate.Right > workArea.Right - margin) return null;
                return new Rect(candidate.X, Math.Clamp(candidate.Y, minY, maxY), candidate.Width, candidate.Height);
            case PlaceSide.Above:
                if (candidate.Y < workArea.Y + margin || candidate.Bottom > workArea.Bottom - margin) return null;
                return new Rect(Math.Clamp(candidate.X, minX, maxX), candidate.Y, candidate.Width, candidate.Height);
            case PlaceSide.Below:
                if (candidate.Bottom > workArea.Bottom - margin || candidate.Y < workArea.Y + margin) return null;
                return new Rect(Math.Clamp(candidate.X, minX, maxX), candidate.Y, candidate.Width, candidate.Height);
            default:
                return null;
        }
    }

    private static double VerticalCenter(Rect anchor, double height) =>
        anchor.Top + (anchor.Height - height) / 2;

    private static double HorizontalCenter(Rect anchor, double width) =>
        anchor.Left + (anchor.Width - width) / 2;
}
