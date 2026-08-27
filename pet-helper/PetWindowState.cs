namespace PetHelper;

public sealed record PetWindowState(double? Left, double? Top, double Scale)
{
    public const double BaseSize = 220d;
    public const double BaseHeight = 260d;

    public static PetWindowState Default { get; } = new(null, null, 1d);

    public double Width => BaseSize * Scale;

    public double Height => BaseHeight * Scale;

    public PetWindowState Reset() => Default;

    public static PetWindowState Normalize(double? left, double? top, double? scale)
    {
        var normalizedScale = scale is 0.75d or 1d or 1.25d or 1.5d ? scale.Value : 1d;
        var validPosition = left is { } validLeft && top is { } validTop
            && double.IsFinite(validLeft) && double.IsFinite(validTop)
            && validLeft is >= -10000d and <= 10000d
            && validTop is >= -10000d and <= 10000d;

        return validPosition
            ? new PetWindowState(left, top, normalizedScale)
            : new PetWindowState(null, null, normalizedScale);
    }
}
