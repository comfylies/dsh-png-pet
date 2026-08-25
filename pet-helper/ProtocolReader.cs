using System.Collections.Immutable;
using System.Text.Json;

namespace PetHelper;

public static class ProtocolReader
{
    private const long MaxSafeSequence = 9_007_199_254_740_991;

    public static ProtocolMessage? Parse(string line)
    {
        if (line.Length > 512) return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasVersionThree(root)) return null;

            return root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                ? kind.GetString() switch
                {
                    "hello" when HasExactlyProperties(root, "version", "kind") => new HelloMessage(),
                    "shutdown" when HasExactlyProperties(root, "version", "kind") => new ShutdownMessage(),
                    "config" => ParseConfig(root),
                    "state" => ParseState(root),
                    _ => null,
                }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ConfigMessage? ParseConfig(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "scale", "reducedMotion")
            || !root.TryGetProperty("scale", out var scale)
            || !root.TryGetProperty("reducedMotion", out var reducedMotion)
            || scale.ValueKind != JsonValueKind.Number
            || !scale.TryGetDouble(out var value)
            || reducedMotion.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || value is not (0.75d or 1d or 1.25d or 1.5d))
        {
            return null;
        }

        return new ConfigMessage(value, reducedMotion.GetBoolean());
    }

    private static StateMessage? ParseState(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "state", "activities", "label", "sequence")
            || !root.TryGetProperty("state", out var state)
            || !root.TryGetProperty("activities", out var activities)
            || !root.TryGetProperty("label", out var label)
            || !root.TryGetProperty("sequence", out var sequence)
            || state.ValueKind != JsonValueKind.String
            || activities.ValueKind != JsonValueKind.Array
            || label.ValueKind != JsonValueKind.String
            || sequence.ValueKind != JsonValueKind.Number
            || !sequence.TryGetInt64(out var value)
            || value < 0
            || value > MaxSafeSequence)
        {
            return null;
        }

        var activityItems = new List<string>();
        foreach (var activity in activities.EnumerateArray())
        {
            if (activity.ValueKind != JsonValueKind.String || activity.GetString() is not { } activityText)
            {
                return null;
            }

            activityItems.Add(activityText);
        }

        var stateText = state.GetString();
        var labelText = label.GetString();
        var immutableActivities = activityItems.ToImmutableArray();
        var displayState = PetDisplayState.From(stateText, immutableActivities, labelText, value);
        return displayState.State == stateText
            && displayState.Label == labelText
            && displayState.Sequence == value
            ? new StateMessage(displayState.State, immutableActivities, displayState.Label, displayState.Sequence)
            : null;
    }

    private static bool HasVersionThree(JsonElement root) =>
        root.TryGetProperty("version", out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out var value)
        && value == 3;

    private static bool HasExactlyProperties(JsonElement root, params string[] expected)
    {
        var names = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Remove(property.Name)) return false;
        }
        return names.Count == 0;
    }
}
