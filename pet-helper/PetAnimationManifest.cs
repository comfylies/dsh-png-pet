using System.Collections.Immutable;
using System.Text.Json;

namespace PetHelper;

public enum PetClipPlaybackMode { Loop, Once }

public sealed record ResolvedClip(
    PetAnimationKey Key,
    string Id,
    ImmutableArray<string> Frames,
    int FrameDurationMs,
    PetClipPlaybackMode Playback)
{
    // Retained for the existing WPF player and its tests while callers migrate to Clip naming.
    public int IntervalMs => FrameDurationMs;
}

public sealed class PetAnimationManifest
{
    private const int LegacyTargetCycleMs = 1000;
    private const int MaximumFramesPerClip = 240;
    private const int MaximumFramesPerManifest = 1024;
    private const int MaximumClipsPerAction = 8;
    private const int MinimumFrameDurationMs = 16;
    private const int MaximumFrameDurationMs = 1000;

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

    private readonly ImmutableDictionary<PetAnimationKey, ActionDefinition> actions;
    private readonly ImmutableDictionary<string, ClipDefinition> clips;

    private PetAnimationManifest(
        ImmutableDictionary<PetAnimationKey, ActionDefinition> actions,
        ImmutableDictionary<string, ClipDefinition> clips)
    {
        this.actions = actions;
        this.clips = clips;
    }

    public static PetAnimationManifest Parse(string json) => Parse(json, null);

    public static PetAnimationManifest Parse(string json, Func<string, string>? actionManifestReader)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw InvalidManifest();
            if (!document.RootElement.TryGetProperty("formatVersion", out var formatVersion))
            {
                return ParseLegacyVersionOne(document.RootElement);
            }
            if (formatVersion.ValueKind != JsonValueKind.Number || !formatVersion.TryGetInt32(out var version))
            {
                throw InvalidManifest();
            }
            return version switch
            {
                2 => ParseVersionTwo(document.RootElement),
                3 => ParseVersionThree(document.RootElement, actionManifestReader ?? throw InvalidManifest()),
                _ => throw InvalidManifest(),
            };
        }
        catch (JsonException exception)
        {
            throw new FormatException("The pet animation manifest is not valid JSON.", exception);
        }
    }

    public ResolvedClip Resolve(PetAnimationKey requested, Func<string, bool> isFrameAvailable)
    {
        ArgumentNullException.ThrowIfNull(isFrameAvailable);
        var visited = new HashSet<PetAnimationKey>();
        var current = actions.ContainsKey(requested) ? requested : PetAnimationKey.Idle;
        while (visited.Add(current))
        {
            if (!actions.TryGetValue(current, out var action)) break;
            foreach (var clipId in action.ClipIds)
            {
                var clip = clips[clipId];
                if (clip.Frames.All(isFrameAvailable))
                {
                    return new ResolvedClip(current, clipId, clip.Frames, clip.FrameDurationMs, clip.Playback);
                }
            }
            if (action.Fallback is not { } fallback) break;
            current = fallback;
        }
        throw new InvalidOperationException("No available pet animation clip could be resolved.");
    }

    private static PetAnimationManifest ParseVersionTwo(JsonElement root)
    {
        JsonElement clipsElement = default;
        JsonElement actionsElement = default;
        var hasClips = false;
        var hasActions = false;
        var seenRootFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!seenRootFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "formatVersion":
                    if (field.Value.ValueKind != JsonValueKind.Number ||
                        !field.Value.TryGetInt32(out var version) || version != 2) throw InvalidManifest();
                    break;
                case "clips": clipsElement = field.Value; hasClips = true; break;
                case "actions": actionsElement = field.Value; hasActions = true; break;
                default: throw InvalidManifest();
            }
        }
        if (!hasClips || !hasActions || clipsElement.ValueKind != JsonValueKind.Object ||
            actionsElement.ValueKind != JsonValueKind.Object) throw InvalidManifest();

        var allFrames = new HashSet<string>(StringComparer.Ordinal);
        var clips = ImmutableDictionary.CreateBuilder<string, ClipDefinition>(StringComparer.Ordinal);
        var totalFrames = 0;
        foreach (var property in clipsElement.EnumerateObject())
        {
            if (!IsSafeClipId(property.Name) || property.Value.ValueKind != JsonValueKind.Object ||
                !clips.TryAdd(property.Name, ParseClip(property.Value, allFrames, ref totalFrames)))
            {
                throw InvalidManifest();
            }
        }
        var actions = ParseVersionTwoActions(actionsElement, clips.Keys);
        ValidateActions(actions, PetAnimationKey.Idle);
        return new PetAnimationManifest(actions.ToImmutable(), clips.ToImmutable());
    }

    private static PetAnimationManifest ParseVersionThree(
        JsonElement root,
        Func<string, string> actionManifestReader)
    {
        JsonElement actionsElement = default;
        var hasActions = false;
        var seenRootFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!seenRootFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "formatVersion":
                    if (field.Value.ValueKind != JsonValueKind.Number ||
                        !field.Value.TryGetInt32(out var version) || version != 3) throw InvalidManifest();
                    break;
                case "actions": actionsElement = field.Value; hasActions = true; break;
                default: throw InvalidManifest();
            }
        }
        if (!hasActions || actionsElement.ValueKind != JsonValueKind.Object) throw InvalidManifest();

        var allFrames = new HashSet<string>(StringComparer.Ordinal);
        var clips = ImmutableDictionary.CreateBuilder<string, ClipDefinition>(StringComparer.Ordinal);
        var actions = ImmutableDictionary.CreateBuilder<PetAnimationKey, ActionDefinition>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var totalFrames = 0;
        foreach (var action in actionsElement.EnumerateObject())
        {
            if (!seenNames.Add(action.Name) || !KeysByName.TryGetValue(action.Name, out var key) ||
                action.Value.ValueKind != JsonValueKind.Object) throw InvalidManifest();
            actions.Add(key, ParseVersionThreeAction(
                action.Name,
                action.Value,
                actionManifestReader,
                clips,
                allFrames,
                ref totalFrames));
        }

        if (actions.Count != KeysByName.Count) throw InvalidManifest();
        ValidateActions(actions, PetAnimationKey.Idle);
        return new PetAnimationManifest(actions.ToImmutable(), clips.ToImmutable());
    }

    private static ActionDefinition ParseVersionThreeAction(
        string actionName,
        JsonElement element,
        Func<string, string> actionManifestReader,
        ImmutableDictionary<string, ClipDefinition>.Builder clips,
        HashSet<string> allFrames,
        ref int totalFrames)
    {
        string? manifestPath = null;
        PetAnimationKey? fallback = null;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seenFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "manifest":
                    if (field.Value.ValueKind != JsonValueKind.String) throw InvalidManifest();
                    manifestPath = field.Value.GetString();
                    break;
                case "fallback": fallback = ParseFallback(field.Value); break;
                default: throw InvalidManifest();
            }
        }

        var expectedPath = $"Animations/{actionName}/animation.json";
        if (!string.Equals(manifestPath, expectedPath, StringComparison.Ordinal)) throw InvalidManifest();
        var childJson = actionManifestReader(expectedPath);
        if (childJson is null) throw InvalidManifest();
        var clipIds = ParseStateManifest(actionName, childJson, clips, allFrames, ref totalFrames);
        return new ActionDefinition(clipIds, fallback);
    }

    private static ImmutableArray<string> ParseStateManifest(
        string actionName,
        string json,
        ImmutableDictionary<string, ClipDefinition>.Builder clips,
        HashSet<string> allFrames,
        ref int totalFrames)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw InvalidManifest();
            JsonElement clipsElement = default;
            var hasClips = false;
            var seenRootFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!seenRootFields.Add(field.Name) || field.Name != "clips") throw InvalidManifest();
                clipsElement = field.Value;
                hasClips = true;
            }
            if (!hasClips || clipsElement.ValueKind != JsonValueKind.Object) throw InvalidManifest();

            var clipIds = ImmutableArray.CreateBuilder<string>();
            foreach (var clip in clipsElement.EnumerateObject())
            {
                var id = $"{actionName}-{clip.Name}";
                if (!IsSafeClipId(clip.Name) || !IsSafeClipId(id) ||
                    clip.Value.ValueKind != JsonValueKind.Object ||
                    clipIds.Count >= MaximumClipsPerAction ||
                    !clips.TryAdd(id, ParseClip(
                        clip.Value,
                        allFrames,
                        ref totalFrames,
                        $"Animations/{actionName}/")))
                {
                    throw InvalidManifest();
                }
                clipIds.Add(id);
            }
            return clipIds.ToImmutable();
        }
        catch (JsonException exception)
        {
            throw new FormatException("The pet state animation manifest is not valid JSON.", exception);
        }
    }

    private static PetAnimationManifest ParseLegacyVersionOne(JsonElement root)
    {
        var allFrames = new HashSet<string>(StringComparer.Ordinal);
        var clips = ImmutableDictionary.CreateBuilder<string, ClipDefinition>(StringComparer.Ordinal);
        var actions = ImmutableDictionary.CreateBuilder<PetAnimationKey, ActionDefinition>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var totalFrames = 0;
        foreach (var action in root.EnumerateObject())
        {
            if (!seenNames.Add(action.Name) || !KeysByName.TryGetValue(action.Name, out var key) ||
                action.Value.ValueKind != JsonValueKind.Object) throw InvalidManifest();
            var legacy = ParseLegacyAction(action.Value, allFrames, ref totalFrames);
            var clipIds = ImmutableArray<string>.Empty;
            if (!legacy.Frames.IsEmpty)
            {
                var clipId = $"{action.Name}-default";
                clips.Add(clipId, new ClipDefinition(
                    legacy.Frames,
                    CalculateLegacyFrameDurationMs(legacy.Frames.Length),
                    PetClipPlaybackMode.Loop));
                clipIds = ImmutableArray.Create(clipId);
            }
            actions.Add(key, new ActionDefinition(clipIds, legacy.Fallback));
        }
        ValidateActions(actions, PetAnimationKey.Idle);
        return new PetAnimationManifest(actions.ToImmutable(), clips.ToImmutable());
    }

    private static ImmutableDictionary<PetAnimationKey, ActionDefinition>.Builder ParseVersionTwoActions(
        JsonElement element,
        IEnumerable<string> availableClipIds)
    {
        var available = availableClipIds.ToHashSet(StringComparer.Ordinal);
        var actions = ImmutableDictionary.CreateBuilder<PetAnimationKey, ActionDefinition>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var assignedClips = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in element.EnumerateObject())
        {
            if (!seenNames.Add(action.Name) || !KeysByName.TryGetValue(action.Name, out var key) ||
                action.Value.ValueKind != JsonValueKind.Object) throw InvalidManifest();
            var definition = ParseVersionTwoAction(action.Value, available);
            if (definition.ClipIds.Any(clipId => !assignedClips.Add(clipId))) throw InvalidManifest();
            actions.Add(key, definition);
        }
        return actions;
    }

    private static ActionDefinition ParseVersionTwoAction(JsonElement element, HashSet<string> availableClipIds)
    {
        ImmutableArray<string>? clipIds = null;
        PetAnimationKey? fallback = null;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seenFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "clips": clipIds = ParseClipIds(field.Value, availableClipIds); break;
                case "fallback": fallback = ParseFallback(field.Value); break;
                default: throw InvalidManifest();
            }
        }
        return new ActionDefinition(clipIds ?? throw InvalidManifest(), fallback);
    }

    private static LegacyActionDefinition ParseLegacyAction(
        JsonElement element,
        HashSet<string> allFrames,
        ref int totalFrames)
    {
        ImmutableArray<string>? frames = null;
        PetAnimationKey? fallback = null;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seenFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "frames": frames = ParseFrames(field.Value, allFrames, ref totalFrames); break;
                case "fallback": fallback = ParseFallback(field.Value); break;
                default: throw InvalidManifest();
            }
        }
        return new LegacyActionDefinition(frames ?? throw InvalidManifest(), fallback);
    }

    private static ClipDefinition ParseClip(
        JsonElement element,
        HashSet<string> allFrames,
        ref int totalFrames,
        string? framePrefix = null)
    {
        ImmutableArray<string>? frames = null;
        int? frameDurationMs = null;
        PetClipPlaybackMode? playback = null;
        var hasStatusAnchor = false;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seenFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "frames": frames = ParseFrames(field.Value, allFrames, ref totalFrames, framePrefix); break;
                case "frameDurationMs": frameDurationMs = ParseFrameDuration(field.Value); break;
                case "playback": playback = ParsePlayback(field.Value); break;
                case "statusAnchor": ParseStatusAnchor(field.Value); hasStatusAnchor = true; break;
                default: throw InvalidManifest();
            }
        }
        if (!hasStatusAnchor) throw InvalidManifest();
        return new ClipDefinition(
            frames ?? throw InvalidManifest(),
            frameDurationMs ?? throw InvalidManifest(),
            playback ?? throw InvalidManifest());
    }

    private static ImmutableArray<string> ParseFrames(
        JsonElement element,
        HashSet<string> allFrames,
        ref int totalFrames,
        string? framePrefix = null)
    {
        if (element.ValueKind != JsonValueKind.Array) throw InvalidManifest();
        var frames = ImmutableArray.CreateBuilder<string>();
        foreach (var frame in element.EnumerateArray())
        {
            if (frame.ValueKind != JsonValueKind.String) throw InvalidManifest();
            var identifier = frame.GetString();
            var resolvedIdentifier = framePrefix is null ? identifier : $"{framePrefix}{identifier}";
            if ((framePrefix is null ? !IsSafeFrameIdentifier(identifier) : !IsSafeLocalFrameIdentifier(identifier)) ||
                !allFrames.Add(resolvedIdentifier!) ||
                frames.Count >= MaximumFramesPerClip || totalFrames >= MaximumFramesPerManifest)
            {
                throw InvalidManifest();
            }
            frames.Add(resolvedIdentifier!);
            totalFrames++;
        }
        return frames.ToImmutable();
    }

    private static ImmutableArray<string> ParseClipIds(JsonElement element, HashSet<string> availableClipIds)
    {
        if (element.ValueKind != JsonValueKind.Array) throw InvalidManifest();
        var clipIds = ImmutableArray.CreateBuilder<string>();
        foreach (var clip in element.EnumerateArray())
        {
            if (clip.ValueKind != JsonValueKind.String ||
                !availableClipIds.Contains(clip.GetString() ?? string.Empty) ||
                clipIds.Count >= MaximumClipsPerAction) throw InvalidManifest();
            clipIds.Add(clip.GetString()!);
        }
        return clipIds.ToImmutable();
    }

    private static int ParseFrameDuration(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) ||
            value < MinimumFrameDurationMs || value > MaximumFrameDurationMs) throw InvalidManifest();
        return value;
    }

    private static PetClipPlaybackMode ParsePlayback(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
            {
                "loop" => PetClipPlaybackMode.Loop,
                "once" => PetClipPlaybackMode.Once,
                _ => throw InvalidManifest(),
            }
            : throw InvalidManifest();

    private static void ParseStatusAnchor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidManifest();
        double? x = null;
        double? y = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetDouble(out var value) || double.IsNaN(value) ||
                double.IsInfinity(value) || value < 0 || value > 1) throw InvalidManifest();
            switch (property.Name)
            {
                case "x": x = value; break;
                case "y": y = value; break;
                default: throw InvalidManifest();
            }
        }
        if (x is null || y is null) throw InvalidManifest();
    }

    private static void ValidateActions(
        ImmutableDictionary<PetAnimationKey, ActionDefinition>.Builder actions,
        PetAnimationKey idleKey)
    {
        if (!actions.TryGetValue(idleKey, out var idle) || idle.ClipIds.IsEmpty || idle.Fallback is not null)
            throw InvalidManifest();
        foreach (var action in actions)
        {
            if (action.Value.Fallback is { } fallback && !actions.ContainsKey(fallback)) throw InvalidManifest();
        }
        foreach (var start in actions.Keys)
        {
            var visited = new HashSet<PetAnimationKey>();
            var current = start;
            while (true)
            {
                if (!visited.Add(current)) throw InvalidManifest();
                if (actions[current].Fallback is not { } next) break;
                current = next;
            }
        }
    }

    private static int CalculateLegacyFrameDurationMs(int frameCount) =>
        (int)Math.Round(LegacyTargetCycleMs / (double)frameCount, MidpointRounding.AwayFromZero);

    private static PetAnimationKey ParseFallback(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String ||
            !KeysByName.TryGetValue(element.GetString() ?? string.Empty, out var fallback)) throw InvalidManifest();
        return fallback;
    }

    private static bool IsSafeClipId(string identifier) =>
        identifier.Length is >= 1 and <= 48 && identifier.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsSafeFrameIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Contains('\\') ||
            identifier.StartsWith("/", StringComparison.Ordinal) || identifier.Contains(':') ||
            identifier.Contains('%') || !identifier.EndsWith(".png", StringComparison.Ordinal)) return false;
        return identifier.Split('/').All(segment => !string.IsNullOrWhiteSpace(segment) &&
            segment == segment.Trim() && segment is not "." and not "..");
    }

    private static bool IsSafeLocalFrameIdentifier(string? identifier) =>
        IsSafeFrameIdentifier(identifier) &&
        !identifier!.StartsWith("Animations/", StringComparison.Ordinal);

    private static FormatException InvalidManifest() => new("The pet animation manifest has an invalid format.");

    private sealed record ActionDefinition(ImmutableArray<string> ClipIds, PetAnimationKey? Fallback);
    private sealed record LegacyActionDefinition(ImmutableArray<string> Frames, PetAnimationKey? Fallback);
    private sealed record ClipDefinition(
        ImmutableArray<string> Frames,
        int FrameDurationMs,
        PetClipPlaybackMode Playback);
}
