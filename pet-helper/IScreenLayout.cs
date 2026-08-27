using System.Windows;

namespace PetHelper;

/// <summary>
/// Supplies monitor work areas in WPF device-independent pixels (DIPs), the same coordinate
/// space Window.Left/Top/Width/Height live in. Abstracted so the placement planner stays pure
/// and the real implementation stays a thin Win32 adapter.
/// </summary>
public interface IScreenLayout
{
    /// <summary>Work area of the primary monitor.</summary>
    Rect PrimaryWorkArea { get; }

    /// <summary>Work areas of every monitor, primary first.</summary>
    IReadOnlyList<Rect> WorkAreas { get; }

    /// <summary>
    /// The work area of the monitor that best contains <paramref name="windowRect"/>
    /// (chosen by the rect's center).
    /// </summary>
    Rect WorkAreaFor(Rect windowRect);
}
