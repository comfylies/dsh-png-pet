using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PlacementPlannerTests
{
    private const double Margin = PlacementPlanner.ScreenMargin;
    private static readonly Size DialogueSize = new(280, 380);

    // A 1920x1040 work area at (0, 0), typical 1080p with taskbar.
    private static readonly Rect WorkArea = new(0, 0, 1920, 1040);

    private static Rect FullyInside(Rect window) =>
        new(100, 100, window.Width, window.Height);

    // ---------- ClampIntoWorkArea: position-only, size preserved ----------

    [Fact]
    public void Clamp_leaves_fully_visible_window_unchanged()
    {
        var window = new Rect(500, 300, 280, 380);
        Assert.Equal(window, PlacementPlanner.ClampIntoWorkArea(window, WorkArea));
    }

    [Theory]
    [InlineData(-50, 300)]  // left overflow
    [InlineData(1700, 300)] // right overflow (1920 - 4 - 280 = 1636 max left)
    [InlineData(300, -50)]  // top overflow
    [InlineData(300, 700)]  // bottom overflow (1040 - 4 - 380 = 656 max top)
    public void Clamp_pulls_window_back_inside_each_edge(double left, double top)
    {
        var window = new Rect(left, top, 280, 380);
        var clamped = PlacementPlanner.ClampIntoWorkArea(window, WorkArea);

        Assert.InRange(clamped.Left, WorkArea.Left + Margin, WorkArea.Right - Margin - clamped.Width);
        Assert.InRange(clamped.Top, WorkArea.Top + Margin, WorkArea.Bottom - Margin - clamped.Height);
        Assert.Equal(window.Width, clamped.Width);
        Assert.Equal(window.Height, clamped.Height);
    }

    [Theory]
    [InlineData(-50, -50)]   // top-left corner
    [InlineData(1700, -50)]  // top-right corner
    [InlineData(-50, 700)]   // bottom-left corner
    [InlineData(1700, 700)]  // bottom-right corner
    public void Clamp_pulls_window_back_from_every_corner(double left, double top)
    {
        var clamped = PlacementPlanner.ClampIntoWorkArea(new Rect(left, top, 280, 380), WorkArea);

        Assert.InRange(clamped.Left, WorkArea.Left + Margin, WorkArea.Right - Margin - clamped.Width);
        Assert.InRange(clamped.Top, WorkArea.Top + Margin, WorkArea.Bottom - Margin - clamped.Height);
    }

    [Fact]
    public void Clamp_pins_oversized_window_to_top_left_margin()
    {
        // Window wider and taller than the work area: position pinning only, size preserved.
        var window = new Rect(2000, 2000, 3000, 3000);
        var clamped = PlacementPlanner.ClampIntoWorkArea(window, WorkArea);

        Assert.Equal(WorkArea.Left + Margin, clamped.X);
        Assert.Equal(WorkArea.Top + Margin, clamped.Y);
        Assert.Equal(3000, clamped.Width);
        Assert.Equal(3000, clamped.Height);
    }

    [Fact]
    public void Clamp_respects_custom_margin()
    {
        var clamped = PlacementPlanner.ClampIntoWorkArea(new Rect(0, 0, 280, 380), WorkArea, margin: 20);
        Assert.Equal(20, clamped.X);
        Assert.Equal(20, clamped.Y);
    }

    // ---------- FitIntoWorkArea: size-aware, shrinks when needed ----------

    [Fact]
    public void Fit_keeps_visible_window_unchanged()
    {
        var window = new Rect(500, 300, 280, 380);
        Assert.Equal(window, PlacementPlanner.FitIntoWorkArea(window, WorkArea, DialogueSize));
    }

    [Fact]
    public void Fit_shrinks_window_larger_than_work_area()
    {
        var window = new Rect(0, 0, 3000, 3000);
        var fitted = PlacementPlanner.FitIntoWorkArea(window, WorkArea, DialogueSize);

        Assert.Equal(WorkArea.Width - 2 * Margin, fitted.Width);
        Assert.Equal(WorkArea.Height - 2 * Margin, fitted.Height);
        Assert.Equal(WorkArea.Left + Margin, fitted.X);
        Assert.Equal(WorkArea.Top + Margin, fitted.Y);
    }

    [Fact]
    public void Fit_floors_small_window_up_to_min_when_work_area_allows()
    {
        // A window smaller than the minimum is grown to the minimum while there is room.
        var fitted = PlacementPlanner.FitIntoWorkArea(new Rect(0, 0, 100, 100), WorkArea, DialogueSize);

        Assert.Equal(DialogueSize.Width, fitted.Width);
        Assert.Equal(DialogueSize.Height, fitted.Height);
        Assert.Equal(WorkArea.Left + Margin, fitted.X);
        Assert.Equal(WorkArea.Top + Margin, fitted.Y);
    }

    [Fact]
    public void Fit_allows_size_below_min_on_tiny_work_area()
    {
        // A 200x200 work area cannot hold even the minimum window: size wins over min.
        var tiny = new Rect(0, 0, 200, 200);
        var fitted = PlacementPlanner.FitIntoWorkArea(new Rect(0, 0, 280, 380), tiny, DialogueSize);

        Assert.True(fitted.Width <= 200 - 2 * Margin);
        Assert.True(fitted.Height <= 200 - 2 * Margin);
    }

    // ---------- PlaceBeside: edge/corner aware first-run placement ----------

    [Fact]
    public void PlaceBeside_prefers_right_when_it_fits()
    {
        var pet = new Rect(800, 500, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(pet, WorkArea, DialogueSize, PlacementPlanner.PetGap);

        Assert.Equal(pet.Right + PlacementPlanner.PetGap, placed.X);
        Assert.Equal(pet.Top + (pet.Height - DialogueSize.Height) / 2, placed.Y);
    }

    [Fact]
    public void PlaceBeside_flips_to_left_when_right_side_has_no_room()
    {
        var pet = new Rect(1700, 500, 220, 220); // near the right edge
        var placed = PlacementPlanner.PlaceBeside(pet, WorkArea, DialogueSize, PlacementPlanner.PetGap);

        Assert.Equal(pet.Left - PlacementPlanner.PetGap - DialogueSize.Width, placed.X);
        Assert.True(placed.Right <= WorkArea.Right - Margin);
    }

    [Fact]
    public void PlaceBeside_flips_to_above_when_neither_side_has_room()
    {
        // Narrow work area: an 800-wide window cannot sit on either side of the pet.
        var narrow = new Rect(0, 0, 1100, 1040);
        var wide = new Size(800, 380);
        var pet = new Rect(400, 600, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(pet, narrow, wide, PlacementPlanner.PetGap);

        Assert.Equal(pet.Left + (pet.Width - wide.Width) / 2, placed.X);
        Assert.Equal(pet.Top - PlacementPlanner.PetGap - wide.Height, placed.Y);
    }

    [Fact]
    public void PlaceBeside_flips_to_below_when_everything_above_is_blocked()
    {
        // Pet pinned to the top edge on a narrow screen: sides and above have no room.
        var narrow = new Rect(0, 0, 600, 1040);
        var tall = new Size(280, 600);
        var pet = new Rect(200, 0, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(pet, narrow, tall, PlacementPlanner.PetGap);

        Assert.Equal(pet.Bottom + PlacementPlanner.PetGap, placed.Y);
        Assert.Equal(pet.Left + (pet.Width - tall.Width) / 2, placed.X);
    }

    [Fact]
    public void PlaceBeside_handles_bottom_right_corner()
    {
        var pet = new Rect(1680, 800, 220, 220); // right edge: 1900
        var placed = PlacementPlanner.PlaceBeside(pet, WorkArea, DialogueSize, PlacementPlanner.PetGap);

        Assert.True(placed.Right <= WorkArea.Right - Margin);
        Assert.True(placed.Bottom <= WorkArea.Bottom - Margin);
        Assert.Equal(pet.Left - PlacementPlanner.PetGap - DialogueSize.Width, placed.X);
        Assert.Equal(WorkArea.Bottom - Margin - DialogueSize.Height, placed.Y); // clamped onto the screen
    }

    [Fact]
    public void PlaceBeside_handles_top_left_corner()
    {
        var pet = new Rect(0, 0, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(pet, WorkArea, DialogueSize, PlacementPlanner.PetGap);

        Assert.Equal(pet.Right + PlacementPlanner.PetGap, placed.X);
        Assert.Equal(WorkArea.Top + Margin, placed.Y); // centered candidate clamped back on screen
        Assert.True(placed.Left >= WorkArea.Left + Margin);
    }

    [Fact]
    public void PlaceBeside_respects_custom_preference_order()
    {
        var pet = new Rect(100, 500, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(
            pet,
            WorkArea,
            DialogueSize,
            PlacementPlanner.PetGap,
            [PlaceSide.Above, PlaceSide.Below, PlaceSide.Right, PlaceSide.Left]);

        Assert.Equal(pet.Top - PlacementPlanner.PetGap - DialogueSize.Height, placed.Y);
        Assert.Equal(pet.Left + (pet.Width - DialogueSize.Width) / 2, placed.X);
    }

    [Fact]
    public void PlaceBeside_falls_back_to_fitting_when_no_side_fits()
    {
        // An enormous window cannot fit beside the pet in any orientation: it must still be
        // returned fully inside the work area.
        var huge = new Size(5000, 5000);
        var pet = new Rect(800, 400, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(pet, WorkArea, huge, PlacementPlanner.PetGap);

        Assert.True(placed.Width <= WorkArea.Width - 2 * Margin);
        Assert.True(placed.Height <= WorkArea.Height - 2 * Margin);
        Assert.True(placed.Left >= WorkArea.Left + Margin);
        Assert.True(placed.Top >= WorkArea.Top + Margin);
    }

    [Fact]
    public void PlaceBeside_uses_default_order_when_preferences_empty()
    {
        var pet = new Rect(800, 500, 220, 220);
        var placed = PlacementPlanner.PlaceBeside(pet, WorkArea, DialogueSize, PlacementPlanner.PetGap, []);

        Assert.Equal(pet.Right + PlacementPlanner.PetGap, placed.X);
    }

    // ---------- CorrectRestoredPosition: monitor changes ----------

    [Fact]
    public void Restore_keeps_position_on_known_monitor()
    {
        var saved = new Rect(500, 300, 280, 380);
        var corrected = PlacementPlanner.CorrectRestoredPosition(saved, [WorkArea], DialogueSize);

        Assert.Equal(saved, corrected);
    }

    [Fact]
    public void Restore_pulls_off_screen_position_into_nearest_work_area()
    {
        // Saved on a monitor that no longer exists (to the right of everything).
        var saved = new Rect(4000, 300, 280, 380);
        var corrected = PlacementPlanner.CorrectRestoredPosition(saved, [WorkArea], DialogueSize);

        Assert.True(corrected.Left >= WorkArea.Left + Margin);
        Assert.True(corrected.Right <= WorkArea.Right - Margin);
        Assert.True(corrected.Top >= WorkArea.Top + Margin);
    }

    [Fact]
    public void Restore_keeps_secondary_monitor_position_on_its_own_work_area()
    {
        var primary = new Rect(0, 0, 1920, 1040);
        var secondary = new Rect(1920, 0, 1920, 980); // different height (its own taskbar)
        var saved = new Rect(2100, 400, 280, 380);    // on the secondary monitor
        var corrected = PlacementPlanner.CorrectRestoredPosition(saved, [primary, secondary], DialogueSize);

        Assert.InRange(corrected.Left, secondary.Left + Margin, secondary.Right - Margin - corrected.Width);
        Assert.InRange(corrected.Top, secondary.Top + Margin, secondary.Bottom - Margin - corrected.Height);
        Assert.True(corrected.Right <= secondary.Right - Margin);
    }

    [Fact]
    public void Restore_pulls_position_above_all_monitors_down_into_nearest()
    {
        var saved = new Rect(800, -5000, 280, 380);
        var corrected = PlacementPlanner.CorrectRestoredPosition(saved, [WorkArea], DialogueSize);

        Assert.True(corrected.Top >= WorkArea.Top + Margin);
    }

    [Fact]
    public void Restore_returns_saved_rect_when_no_work_areas_known()
    {
        var saved = new Rect(100, 100, 280, 380);
        Assert.Equal(saved, PlacementPlanner.CorrectRestoredPosition(saved, [], DialogueSize));
    }

    [Fact]
    public void Restore_shrinks_size_to_fit_smaller_work_area()
    {
        var small = new Rect(0, 0, 800, 600);
        var corrected = PlacementPlanner.CorrectRestoredPosition(new Rect(100, 100, 800, 900), [small], DialogueSize);

        Assert.True(corrected.Width <= small.Width - 2 * Margin);
        Assert.True(corrected.Height <= small.Height - 2 * Margin);
    }

    // ---------- ClampIntoWorkAreaWithProtrusion: edge docking ----------

    private static readonly PlacementPlanner.EdgeProtrusion PetProtrusion = new(54, 21, 29, 5);

    [Fact]
    public void Protrusion_keeps_on_screen_window_unchanged()
    {
        var window = new Rect(500, 300, 220, 220);
        Assert.Equal(window, PlacementPlanner.ClampIntoWorkAreaWithProtrusion(window, WorkArea, PetProtrusion));
    }

    [Fact]
    public void Protrusion_allows_docking_past_each_edge_within_limit()
    {
        Assert.Equal(new Rect(-30, 300, 220, 220), PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(-30, 300, 220, 220), WorkArea, PetProtrusion));
        Assert.Equal(new Rect(1671, 300, 220, 220), PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(1671, 300, 220, 220), WorkArea, PetProtrusion)); // right: 1920+29-220
        Assert.Equal(new Rect(500, -10, 220, 220), PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(500, -10, 220, 220), WorkArea, PetProtrusion));
        Assert.Equal(new Rect(500, 815, 220, 220), PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(500, 815, 220, 220), WorkArea, PetProtrusion)); // bottom: 1040+5-220
    }

    [Theory]
    [InlineData(-100, 300)]   // beyond left protrusion
    [InlineData(300, -100)]   // beyond top protrusion
    [InlineData(1800, 300)]   // beyond right protrusion (window left > 1920+29-220)
    [InlineData(300, 900)]    // beyond bottom protrusion
    public void Protrusion_snaps_back_when_dragged_beyond_the_limit(double left, double top)
    {
        var clamped = PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(left, top, 220, 220), WorkArea, PetProtrusion);

        Assert.True(clamped.Left >= WorkArea.Left - PetProtrusion.Left);
        Assert.True(clamped.Right <= WorkArea.Right + PetProtrusion.Right);
        Assert.True(clamped.Top >= WorkArea.Top - PetProtrusion.Top);
        Assert.True(clamped.Bottom <= WorkArea.Bottom + PetProtrusion.Bottom);
    }

    [Fact]
    public void Protrusion_handles_corners()
    {
        var clamped = PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(-100, -100, 220, 220), WorkArea, PetProtrusion);

        Assert.Equal(WorkArea.Left - PetProtrusion.Left, clamped.X);
        Assert.Equal(WorkArea.Top - PetProtrusion.Top, clamped.Y);
    }

    [Fact]
    public void Protrusion_centers_window_when_work_area_is_tiny()
    {
        var tiny = new Rect(0, 0, 100, 100);
        var clamped = PlacementPlanner.ClampIntoWorkAreaWithProtrusion(new Rect(0, 0, 220, 220), tiny, PetProtrusion);

        Assert.Equal(tiny.Left + (tiny.Width - 220) / 2, clamped.X);
        Assert.Equal(tiny.Top + (tiny.Height - 220) / 2, clamped.Y);
    }

    // ---------- NearestWorkArea ----------

    [Fact]
    public void NearestWorkArea_returns_itself_for_empty_list()
    {
        Assert.Equal(new Rect(100, 100, 220, 220), PlacementPlanner.NearestWorkArea(new Rect(100, 100, 220, 220), []));
    }

    [Fact]
    public void NearestWorkArea_picks_the_closest_monitor()
    {
        var primary = new Rect(0, 0, 1920, 1040);
        var secondary = new Rect(1920, 0, 1920, 980);
        var onSecondary = new Rect(2600, 400, 220, 220);
        var onPrimary = new Rect(800, 400, 220, 220);

        Assert.Equal(secondary, PlacementPlanner.NearestWorkArea(onSecondary, [primary, secondary]));
        Assert.Equal(primary, PlacementPlanner.NearestWorkArea(onPrimary, [primary, secondary]));
    }

    [Fact]
    public void NearestWorkArea_pulls_orphaned_position_onto_the_closest_monitor()
    {
        var primary = new Rect(0, 0, 1920, 1040);
        var secondary = new Rect(1920, 0, 1920, 980);
        // Far to the right of both monitors: the rightmost one wins.
        Assert.Equal(secondary, PlacementPlanner.NearestWorkArea(new Rect(9000, 400, 220, 220), [primary, secondary]));
    }
}
