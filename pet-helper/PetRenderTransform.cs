namespace PetHelper;

/// <summary>
/// A bounded, clip-wide presentation adjustment in normalized artboard coordinates.
/// It affects every frame in one clip identically, so animation frames cannot visibly pop.
/// </summary>
public sealed record PetRenderTransform(
    double Scale,
    PetRenderPoint Origin,
    PetRenderOffset Offset)
{
    public static PetRenderTransform Identity { get; } = new(
        1d,
        new PetRenderPoint(0.5d, 0.5d),
        new PetRenderOffset(0d, 0d));

    public PetRenderPoint TransformPoint(PetRenderPoint point) => new(
        Origin.X + (point.X - Origin.X) * Scale + Offset.X,
        Origin.Y + (point.Y - Origin.Y) * Scale + Offset.Y);
}

public sealed record PetRenderPoint(double X, double Y);

public sealed record PetRenderOffset(double X, double Y);
