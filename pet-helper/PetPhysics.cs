using System.Windows;

namespace PetHelper;

/// <summary>
/// Small, deterministic window-physics model used only by the local WPF Helper.
/// Positions and velocities are in WPF device-independent pixels and seconds.
/// </summary>
public static class PetPhysics
{
    /// <summary>Deliberately game-like, standard downward acceleration in DIPs per second squared.</summary>
    public const double StandardGravity = 1800d;

    /// <summary>Prevents an extreme native-drag sample from launching the window uncontrollably.</summary>
    public const double MaxLaunchSpeed = 2400d;

    private const double MaximumStepSeconds = 1d / 30d;
    private const double RestingImpactSpeed = 95d;

    /// <summary>
    /// Advances one bounded simulation step. The bounce slider maps linearly from 0% to a
    /// 0.90 restitution coefficient, so zero settles immediately and 100 remains lively.
    /// </summary>
    public static PetPhysicsState Advance(
        PetPhysicsState state,
        Size petSize,
        Rect workArea,
        int bouncePercent,
        double elapsedSeconds)
    {
        if (state.IsResting || !IsUsable(workArea) || !IsUsable(petSize)) return state;

        var step = Math.Clamp(elapsedSeconds, 0d, MaximumStepSeconds);
        if (step == 0d) return state;

        var velocity = new Vector(state.Velocity.X, state.Velocity.Y + StandardGravity * step);
        var position = new Point(
            state.Position.X + velocity.X * step,
            state.Position.Y + velocity.Y * step - StandardGravity * step * step / 2d);
        var restitution = BounceCoefficientFor(bouncePercent);
        var floor = workArea.Bottom - petSize.Height;
        var right = workArea.Right - petSize.Width;
        var resting = false;

        if (position.X < workArea.Left)
        {
            position.X = workArea.Left;
            velocity.X = Math.Abs(velocity.X) * restitution;
        }
        else if (position.X > right)
        {
            position.X = right;
            velocity.X = -Math.Abs(velocity.X) * restitution;
        }

        if (position.Y < workArea.Top)
        {
            position.Y = workArea.Top;
            velocity.Y = Math.Abs(velocity.Y) * restitution;
        }
        else if (position.Y > floor)
        {
            position.Y = floor;
            if (Math.Abs(velocity.Y) < RestingImpactSpeed || restitution == 0d)
            {
                velocity.Y = 0d;
                if (Math.Abs(velocity.X) < RestingImpactSpeed) velocity.X = 0d;
                resting = velocity.X == 0d;
            }
            else
            {
                velocity.Y = -Math.Abs(velocity.Y) * restitution;
            }
        }

        return new PetPhysicsState(position, velocity, resting);
    }

    public static PetPhysicsState Launch(Point position, Vector velocity)
    {
        var length = velocity.Length;
        if (length > MaxLaunchSpeed)
        {
            velocity *= MaxLaunchSpeed / length;
        }
        return new PetPhysicsState(position, velocity, IsResting: false);
    }

    public static PetPhysicsState StartFalling(Point position) =>
        new(position, new Vector(0d, 0d), IsResting: false);

    public static PetPhysicsState Rest(Point position) =>
        new(position, new Vector(0d, 0d), IsResting: true);

    public static double BounceCoefficientFor(int bouncePercent) =>
        Math.Clamp(bouncePercent, 0, 100) * 0.009d;

    private static bool IsUsable(Size size) =>
        double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0d && size.Height > 0d;

    private static bool IsUsable(Rect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height)
        && rect.Width > 0d && rect.Height > 0d;
}

public readonly record struct PetPhysicsState(Point Position, Vector Velocity, bool IsResting)
{
    public static PetPhysicsState Moving(Point position, Vector velocity) => new(position, velocity, IsResting: false);
}
