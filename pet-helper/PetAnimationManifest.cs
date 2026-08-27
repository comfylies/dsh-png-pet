using System.Collections.Immutable;
using System.Text.Json;

namespace PetHelper;

public sealed record ResolvedAnimation(PetAnimationKey Key, ImmutableArray<string> Frames, int IntervalMs);

public sealed class PetAnimationManifest
{
    private const int TargetCycleMs = 1000;
    private const int MaximumFramesPerAction = 64;

    private static readonly ImmutableDictionary<string, PetAnimationKey> KeysByName =
        new Dictionary<string, PetAnimationKey>(StringComparer.Ordinal)
        {
            ["idle"] = PetAnimationKey.Idle,
            ["thinking"] = PetAnimationKey.Thinking,
            ["working"] = PetAnimationKey.Working,
            ["thinking-working"] = PetAnimationKey.ThinkingWorking,
            ["waiting"] = PetAnimationKey.Waiting,
            ["success"] = PetAnimationKey.Success,
            ["error"] = PetAnimationKey.Error,
            ["disconnected"] = PetAnimationKey.Disconnected,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private readonly ImmutableDictionary<PetAnimationKey, AnimationDefinition> definitions;

    private PetAnimationManifest(ImmutableDictionary<PetAnimationKey, AnimationDefinition> definitions)
    {
        this.definitions = definitions;
    }

    public static PetAnimationManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidManifest();
            }

            var definitions = ImmutableDictionary.CreateBuilder<PetAnimationKey, AnimationDefinition>();
            var seenActionNames = new HashSet<string>(StringComparer.Ordinal);
            var allFrames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var action in document.RootElement.EnumerateObject())
            {
                if (!seenActionNames.Add(action.Name) ||
                    !KeysByName.TryGetValue(action.Name, out var key))
                {
                    throw InvalidManifest();
                }

                if (action.Value.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidManifest();
                }

                var definition = ParseDefinition(action.Value, allFrames);
                definitions.Add(key, definition);
            }

            if (!definitions.TryGetValue(PetAnimationKey.Idle, out var idle) ||
                idle.Frames.IsEmpty ||
                idle.Fallback is not null)
            {
                throw InvalidManifest();
            }

            foreach (var definition in definitions)
            {
                if (definition.Value.Fallback is { } fallback &&
                    !definitions.ContainsKey(fallback))
                {
                    throw InvalidManifest();
                }
            }

            RejectFallbackCycles(definitions);
            return new PetAnimationManifest(definitions.ToImmutable());
        }
        catch (JsonException exception)
        {
            throw new FormatException("The pet animation manifest is not valid JSON.", exception);
        }
    }

    public ResolvedAnimation Resolve(PetAnimationKey requested, Func<string, bool> isFrameAvailable)
    {
        ArgumentNullException.ThrowIfNull(isFrameAvailable);

        var visited = new HashSet<PetAnimationKey>();
        var current = definitions.ContainsKey(requested)
            ? requested
            : PetAnimationKey.Idle;

        while (visited.Add(current))
        {
            if (!definitions.TryGetValue(current, out var definition))
            {
                break;
            }

            if (!definition.Frames.IsEmpty && definition.Frames.All(isFrameAvailable))
            {
                return new ResolvedAnimation(
                    current,
                    definition.Frames,
                    CalculateIntervalMs(definition.Frames.Length));
            }

            if (definition.Fallback is not { } fallback)
            {
                break;
            }

            current = fallback;
        }

        throw new InvalidOperationException("No available pet animation action could be resolved.");
    }

    private static AnimationDefinition ParseDefinition(JsonElement action, HashSet<string> allFrames)
    {
        ImmutableArray<string>? frames = null;
        PetAnimationKey? fallback = null;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in action.EnumerateObject())
        {
            if (!seenFields.Add(field.Name))
            {
                throw InvalidManifest();
            }

            switch (field.Name)
            {
                case "frames":
                    frames = ParseFrames(field.Value, allFrames);
                    break;
                case "fallback":
                    fallback = ParseFallback(field.Value);
                    break;
                default:
                    throw InvalidManifest();
            }
        }

        if (frames is null)
        {
            throw InvalidManifest();
        }

        return new AnimationDefinition(frames.Value, fallback);
    }

    private static ImmutableArray<string> ParseFrames(JsonElement element, HashSet<string> allFrames)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw InvalidManifest();
        }

        var frames = ImmutableArray.CreateBuilder<string>();
        foreach (var frame in element.EnumerateArray())
        {
            if (frame.ValueKind != JsonValueKind.String)
            {
                throw InvalidManifest();
            }

            var identifier = frame.GetString();
            if (!IsSafeFrameIdentifier(identifier) || !allFrames.Add(identifier!))
            {
                throw InvalidManifest();
            }

            if (frames.Count >= MaximumFramesPerAction)
            {
                throw InvalidManifest();
            }

            frames.Add(identifier!);
        }

        return frames.ToImmutable();
    }

    private static int CalculateIntervalMs(int frameCount) =>
        (int)Math.Round(
            TargetCycleMs / (double)frameCount,
            MidpointRounding.AwayFromZero);

    private static PetAnimationKey ParseFallback(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String ||
            !KeysByName.TryGetValue(element.GetString() ?? string.Empty, out var fallback))
        {
            throw InvalidManifest();
        }

        return fallback;
    }

    private static bool IsSafeFrameIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            identifier.Contains('\\') ||
            identifier.StartsWith("/", StringComparison.Ordinal) ||
            identifier.Contains(':') ||
            identifier.Contains('%') ||
            !identifier.EndsWith(".png", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = identifier.Split('/');
        return segments.All(segment =>
            !string.IsNullOrWhiteSpace(segment) &&
            segment == segment.Trim() &&
            segment is not "." and not "..");
    }

    private static void RejectFallbackCycles(ImmutableDictionary<PetAnimationKey, AnimationDefinition>.Builder definitions)
    {
        foreach (var start in definitions.Keys)
        {
            var visited = new HashSet<PetAnimationKey>();
            var current = start;

            while (true)
            {
                if (!visited.Add(current))
                {
                    throw InvalidManifest();
                }

                var fallback = definitions[current].Fallback;
                if (fallback is not { } next)
                {
                    break;
                }

                current = next;
            }
        }
    }

    private static FormatException InvalidManifest() =>
        new("The pet animation manifest has an invalid format.");

    private sealed record AnimationDefinition(
        ImmutableArray<string> Frames,
        PetAnimationKey? Fallback);
}
