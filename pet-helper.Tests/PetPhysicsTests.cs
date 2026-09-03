using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetPhysicsTests
{
    private static readonly Rect WorkArea = new(0, 0, 1000, 700);
    private static readonly Size PetSize = new(100, 100);

    [Fact]
    public void Advance_applies_standard_gravity_in_device_independent_pixels()
    {
        var result = PetPhysics.Advance(
            PetPhysicsState.Moving(new Point(200, 100), new Vector(0, 0)),
            PetSize,
            WorkArea,
            bouncePercent: 65,
            elapsedSeconds: 0.01);

        Assert.Equal(100.09d, result.Position.Y, 3);
        Assert.Equal(PetPhysics.StandardGravity * 0.01d, result.Velocity.Y, 3);
        Assert.False(result.IsResting);
    }

    [Fact]
    public void Advance_bounces_from_the_floor_using_the_linear_bounce_slider()
    {
        var result = PetPhysics.Advance(
            PetPhysicsState.Moving(new Point(200, 599), new Vector(0, 100)),
            PetSize,
            WorkArea,
            bouncePercent: 50,
            elapsedSeconds: 0.01);

        Assert.Equal(600d, result.Position.Y, 3);
        Assert.Equal(-(100d + PetPhysics.StandardGravity * 0.01d) * 0.45d, result.Velocity.Y, 3);
        Assert.False(result.IsResting);
    }

    [Fact]
    public void Advance_stops_a_low_energy_floor_collision()
    {
        var result = PetPhysics.Advance(
            PetPhysicsState.Moving(new Point(200, 599.99), new Vector(0, 1)),
            PetSize,
            WorkArea,
            bouncePercent: 65,
            elapsedSeconds: 0.01);

        Assert.Equal(new Point(200, 600), result.Position);
        Assert.Equal(new Vector(0, 0), result.Velocity);
        Assert.True(result.IsResting);
    }

    [Fact]
    public void Advance_bounces_from_side_edges_and_keeps_the_pet_inside_the_work_area()
    {
        var result = PetPhysics.Advance(
            PetPhysicsState.Moving(new Point(899, 100), new Vector(300, 0)),
            PetSize,
            WorkArea,
            bouncePercent: 100,
            elapsedSeconds: 0.01);

        Assert.Equal(900d, result.Position.X, 3);
        Assert.Equal(-270d, result.Velocity.X, 3);
    }

    [Fact]
    public void Launch_caps_an_extreme_drag_velocity_and_restarts_motion()
    {
        var launched = PetPhysics.Launch(new Point(100, 100), new Vector(20_000, -20_000));

        Assert.False(launched.IsResting);
        Assert.InRange(launched.Velocity.Length, PetPhysics.MaxLaunchSpeed - 0.001d, PetPhysics.MaxLaunchSpeed + 0.001d);
    }
}
