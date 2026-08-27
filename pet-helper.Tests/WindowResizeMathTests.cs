using System.Windows;
using System.Windows.Input;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class WindowResizeMathTests
{
    private const double Margin = PlacementPlanner.ScreenMargin;
    private static readonly Rect WorkArea = new(0, 0, 1920, 1040);
    private static readonly Size Min = new(220, 240);
    private static readonly Size Max = new(800, 900);

    private static Rect Resize(Rect start, double dx, double dy, ResizeEdge edge) =>
        WindowResizeMath.ResizeFrom(start, new Vector(dx, dy), edge, Min, Max, WorkArea, Margin);

    // ---------- HitTest: edges and corners ----------

    [Fact]
    public void HitTest_returns_none_at_center()
    {
        Assert.Equal(ResizeEdge.None, WindowResizeMath.HitTest(new Point(140, 190), new Rect(0, 0, 280, 380)));
    }

    [Theory]
    [InlineData(0, 190, ResizeEdge.West)]
    [InlineData(280, 190, ResizeEdge.East)]
    [InlineData(140, 0, ResizeEdge.North)]
    [InlineData(140, 380, ResizeEdge.South)]
    public void HitTest_detects_each_edge(double x, double y, ResizeEdge expected)
    {
        Assert.Equal(expected, WindowResizeMath.HitTest(new Point(x, y), new Rect(0, 0, 280, 380)));
    }

    [Theory]
    [InlineData(0, 0, ResizeEdge.NorthWest)]
    [InlineData(280, 0, ResizeEdge.NorthEast)]
    [InlineData(0, 380, ResizeEdge.SouthWest)]
    [InlineData(280, 380, ResizeEdge.SouthEast)]
    public void HitTest_detects_each_corner(double x, double y, ResizeEdge expected)
    {
        Assert.Equal(expected, WindowResizeMath.HitTest(new Point(x, y), new Rect(0, 0, 280, 380)));
    }

    [Theory]
    [InlineData(8, 190, ResizeEdge.West)]   // exactly on the sensitivity boundary
    [InlineData(-8, 190, ResizeEdge.West)]  // just outside the window, still grabbable
    [InlineData(9, 190, ResizeEdge.None)]   // just past the boundary
    [InlineData(-9, 190, ResizeEdge.None)]
    public void HitTest_respects_sensitivity_band(double x, double y, ResizeEdge expected)
    {
        Assert.Equal(expected, WindowResizeMath.HitTest(new Point(x, y), new Rect(0, 0, 280, 380)));
    }

    [Fact]
    public void HitTest_corner_takes_priority_over_plain_edge()
    {
        // Within 8px of both the west and north edges: a corner, not a plain edge.
        Assert.Equal(ResizeEdge.NorthWest, WindowResizeMath.HitTest(new Point(5, 5), new Rect(0, 0, 280, 380)));
    }

    // ---------- ResizeFrom: sizing ----------

    [Fact]
    public void Resize_east_grows_width_and_keeps_left_anchored()
    {
        var result = Resize(new Rect(500, 300, 280, 380), 100, 0, ResizeEdge.East);
        Assert.Equal(new Rect(500, 300, 380, 380), result);
    }

    [Fact]
    public void Resize_west_grows_width_and_keeps_right_anchored()
    {
        var result = Resize(new Rect(500, 300, 280, 380), -100, 0, ResizeEdge.West);
        Assert.Equal(new Rect(400, 300, 380, 380), result);
    }

    [Fact]
    public void Resize_south_grows_height_and_keeps_top_anchored()
    {
        var result = Resize(new Rect(500, 300, 280, 380), 0, 100, ResizeEdge.South);
        Assert.Equal(new Rect(500, 300, 280, 480), result);
    }

    [Fact]
    public void Resize_north_grows_height_and_keeps_bottom_anchored()
    {
        var result = Resize(new Rect(500, 300, 280, 380), 0, -100, ResizeEdge.North);
        Assert.Equal(new Rect(500, 200, 280, 480), result);
    }

    [Fact]
    public void Resize_southeast_combines_both_axes()
    {
        var result = Resize(new Rect(500, 300, 280, 380), 100, 100, ResizeEdge.SouthEast);
        Assert.Equal(new Rect(500, 300, 380, 480), result);
    }

    [Fact]
    public void Resize_northwest_combines_both_axes()
    {
        var result = Resize(new Rect(500, 300, 280, 380), -100, -100, ResizeEdge.NorthWest);
        Assert.Equal(new Rect(400, 200, 380, 480), result);
    }

    // ---------- ResizeFrom: bounds ----------

    [Fact]
    public void Resize_clamps_to_max_size()
    {
        var result = Resize(new Rect(500, 300, 280, 380), 600, 0, ResizeEdge.East);
        Assert.Equal(Max.Width, result.Width);
        Assert.Equal(500, result.Left);
    }

    [Fact]
    public void Resize_clamps_to_min_size_when_dragging_edges_inward()
    {
        var result = Resize(new Rect(500, 300, 280, 380), 100, 0, ResizeEdge.West);
        Assert.Equal(Min.Width, result.Width);
        Assert.Equal(780 - Min.Width, result.Left); // right edge stays anchored at 780
    }

    [Fact]
    public void Resize_keeps_window_inside_work_area_at_left_edge()
    {
        var result = Resize(new Rect(100, 300, 280, 380), -500, 0, ResizeEdge.West);
        Assert.Equal(WorkArea.Left + Margin, result.Left);
        Assert.True(result.Right <= WorkArea.Right - Margin);
    }

    [Fact]
    public void Resize_keeps_window_inside_work_area_at_bottom_right_corner()
    {
        var result = Resize(new Rect(1500, 800, 280, 380), 700, 700, ResizeEdge.SouthEast);

        Assert.Equal(Max.Width, result.Width);
        Assert.Equal(Max.Height, result.Height);
        Assert.True(result.Right <= WorkArea.Right - Margin);
        Assert.True(result.Bottom <= WorkArea.Bottom - Margin);
    }

    [Fact]
    public void Resize_keeps_window_inside_small_work_area()
    {
        var small = new Rect(0, 0, 600, 500);
        var result = WindowResizeMath.ResizeFrom(
            new Rect(100, 100, 280, 380), new Vector(500, 0), ResizeEdge.East, Min, Max, small, Margin);

        Assert.True(result.Right <= small.Right - Margin);
        Assert.True(result.Width <= small.Width - 2 * Margin);
    }

    // ---------- Cursor mapping ----------

    [Theory]
    [InlineData(ResizeEdge.North, nameof(Cursors.SizeNS))]
    [InlineData(ResizeEdge.South, nameof(Cursors.SizeNS))]
    [InlineData(ResizeEdge.West, nameof(Cursors.SizeWE))]
    [InlineData(ResizeEdge.East, nameof(Cursors.SizeWE))]
    [InlineData(ResizeEdge.NorthWest, nameof(Cursors.SizeNWSE))]
    [InlineData(ResizeEdge.SouthEast, nameof(Cursors.SizeNWSE))]
    [InlineData(ResizeEdge.NorthEast, nameof(Cursors.SizeNESW))]
    [InlineData(ResizeEdge.SouthWest, nameof(Cursors.SizeNESW))]
    [InlineData(ResizeEdge.None, nameof(Cursors.Arrow))]
    public void ResizeCursor_maps_every_edge(ResizeEdge edge, string expectedCursor)
    {
        Assert.Same((Cursor)typeof(Cursors).GetProperty(expectedCursor)!.GetValue(null)!, WindowResizeMath.ResizeCursor(edge));
    }
}
