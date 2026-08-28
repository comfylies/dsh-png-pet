namespace PetHelper;

/// <summary>
/// The normalized top-centre point of the pet's head in an animation artboard.
/// </summary>
public sealed record PetStatusAnchor(double X, double Y)
{
    public static PetStatusAnchor Default { get; } = new(0.5d, 0d);

    public bool IsWithinArtboard =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        X is >= 0d and <= 1d &&
        Y is >= 0d and <= 1d;
}
