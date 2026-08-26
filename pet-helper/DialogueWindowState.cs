using System.IO;
using System.Text.Json;

namespace PetHelper;

public sealed record DialogueWindowState(double? Left, double? Top, double Width, double Height)
{
    public const double DefaultWidth = 280d;
    public const double DefaultHeight = 380d;
    public const double MinWidth = 220d;
    public const double MinHeight = 240d;
    public const double MaxWidth = 800d;
    public const double MaxHeight = 900d;

    public static DialogueWindowState Default { get; } = new(null, null, DefaultWidth, DefaultHeight);

    public static DialogueWindowState Normalize(double? left, double? top, double? width, double? height)
    {
        var validPosition = left is { } validLeft && top is { } validTop
            && double.IsFinite(validLeft) && double.IsFinite(validTop)
            && validLeft is >= -10000d and <= 10000d
            && validTop is >= -10000d and <= 10000d;
        var validSize = width is { } validWidth && height is { } validHeight
            && double.IsFinite(validWidth) && double.IsFinite(validHeight)
            && validWidth is >= MinWidth and <= MaxWidth
            && validHeight is >= MinHeight and <= MaxHeight;

        return validPosition && validSize
            ? new DialogueWindowState(left, top, width!.Value, height!.Value)
            : Default;
    }
}

public sealed class DialogueWindowStateStore
{
    private readonly string path;

    public DialogueWindowStateStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DshPngPet",
            "dialogue-window-state.json");
    }

    public DialogueWindowState Load()
    {
        try
        {
            if (!File.Exists(path)) return DialogueWindowState.Default;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            double? left = TryReadDouble(root, "left");
            double? top = TryReadDouble(root, "top");
            var width = TryReadDouble(root, "width");
            var height = TryReadDouble(root, "height");
            return DialogueWindowState.Normalize(left, top, width, height);
        }
        catch
        {
            return DialogueWindowState.Default;
        }
    }

    public void Save(DialogueWindowState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                left = state.Left,
                top = state.Top,
                width = state.Width,
                height = state.Height,
            }));
        }
        catch
        {
            // Best effort: a failed save never blocks the pet.
        }
    }

    private static double? TryReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
