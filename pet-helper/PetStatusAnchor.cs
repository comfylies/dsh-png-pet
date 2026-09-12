using System.Text.Json.Serialization;

namespace PetHelper;

/// <summary>
/// The normalized top-centre point of the pet's head in an animation artboard.
/// </summary>
public sealed record PetStatusAnchor(double X, double Y)
{
    public static PetStatusAnchor Default { get; } = new(0.5d, 0d);

    // Derived, and the stored character manifest must carry exactly { x, y }: serializing the
    // record straight to the library would otherwise add this member and fail its own reader.
    [JsonIgnore]
    public bool IsWithinArtboard =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        X is >= 0d and <= 1d &&
        Y is >= 0d and <= 1d;
}
