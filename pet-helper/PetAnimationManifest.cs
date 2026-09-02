using System.Collections.Immutable;
using System.Text.Json;

namespace PetHelper;

public enum PetClipPlaybackMode { Loop, Once }

public sealed record ResolvedClip(
    PetAnimationKey Key,
    string Id,
    ImmutableArray<string> Frames,
    int FrameDurationMs,
    PetClipPlaybackMode Playback,
    PetStatusAnchor StatusAnchor,
    PetRenderTransform RenderTransform)
{
    public ResolvedClip(
        PetAnimationKey key,
        string id,
        ImmutableArray<string> frames,
        int frameDurationMs,
        PetClipPlaybackMode playback,
        PetStatusAnchor statusAnchor)
        : this(key, id, frames, frameDurationMs, playback, statusAnchor, PetRenderTransform.Identity)
    {
    }

    // Retained for the existing WPF player and its tests while callers migrate to Clip naming.
    public int IntervalMs => FrameDurationMs;
}

public sealed record ResolvedTransition(
    ImmutableHashSet<PetAnimationKey> Targets,
    ImmutableArray<ResolvedClip> Clips);

public sealed record ResolvedStateProgram(
    PetAnimationKey EffectiveKey,
    ImmutableArray<ResolvedClip> Enter,
    ImmutableArray<ResolvedClip> Loop,
    ImmutableArray<ResolvedTransition> Transitions,
    bool LoopRepeats);

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
            ["responding"] = PetAnimationKey.Responding,
            ["waiting"] = PetAnimationKey.Waiting,
            ["question"] = PetAnimationKey.Question,
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
                4 => ParseVersionFour(document.RootElement, actionManifestReader ?? throw InvalidManifest()),
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
        var program = ResolveProgram(requested, isFrameAvailable);
        return program.Loop[0];
    }

    public ResolvedStateProgram ResolveProgram(PetAnimationKey requested, Func<string, bool> isFrameAvailable)
    {
        ArgumentNullException.ThrowIfNull(isFrameAvailable);
        var visited = new HashSet<PetAnimationKey>();
        var current = actions.ContainsKey(requested) ? requested : PetAnimationKey.Idle;
        while (visited.Add(current))
        {
            if (!actions.TryGetValue(current, out var action)) break;
            var program = action.Program ?? new ProgramDefinition(ImmutableArray<string>.Empty, action.ClipIds, false);
            var loop = ResolveClips(current, program.Loop, isFrameAvailable);
            if (!loop.IsEmpty)
            {
                var enter = ResolveClips(current, program.Enter, isFrameAvailable);
                var transitions = (action.Transitions ?? ImmutableArray<TransitionDefinition>.Empty)
                    .Select(transition => new ResolvedTransition(
                        transition.Targets,
                        ResolveClips(current, transition.ClipIds, isFrameAvailable)))
                    .Where(transition => !transition.Clips.IsEmpty)
                    .ToImmutableArray();
                return new ResolvedStateProgram(current, enter, loop, transitions, program.LoopRepeats);
            }
            if (action.Fallback is not { } fallback) break;
            current = fallback;
        }
        throw new InvalidOperationException("No available pet animation clip could be resolved.");
    }

    private ImmutableArray<ResolvedClip> ResolveClips(
        PetAnimationKey key,
        ImmutableArray<string> clipIds,
        Func<string, bool> isFrameAvailable) => clipIds
        .Where(clipId => clips[clipId].Frames.All(isFrameAvailable))
        .Select(clipId => ToResolvedClip(key, clipId, clips[clipId]))
        .ToImmutableArray();

    private static ResolvedClip ToResolvedClip(PetAnimationKey key, string id, ClipDefinition clip) => new(
        key,
        id,
        clip.Frames,
        clip.FrameDurationMs,
        clip.Playback,
        clip.StatusAnchor,
        clip.RenderTransform);

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

    private static PetAnimationManifest ParseVersionFour(
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
                        !field.Value.TryGetInt32(out var version) || version != 4) throw InvalidManifest();
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
            actions.Add(key, ParseVersionFourAction(
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

    private static ActionDefinition ParseVersionFourAction(
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
        var state = ParseVersionFourStateManifest(actionName, childJson, clips, allFrames, ref totalFrames);
        return new ActionDefinition(state.ClipIds, fallback, state.Program, state.Transitions);
    }

    private static StateManifestDefinition ParseVersionFourStateManifest(
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
            JsonElement programElement = default;
            JsonElement transitionsElement = default;
            var hasClips = false;
            var hasProgram = false;
            var hasTransitions = false;
            var seenRootFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!seenRootFields.Add(field.Name)) throw InvalidManifest();
                switch (field.Name)
                {
                    case "clips": clipsElement = field.Value; hasClips = true; break;
                    case "program": programElement = field.Value; hasProgram = true; break;
                    case "transitions": transitionsElement = field.Value; hasTransitions = true; break;
                    default: throw InvalidManifest();
                }
            }
            if (!hasClips || clipsElement.ValueKind != JsonValueKind.Object) throw InvalidManifest();

            var localIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var clipIds = ImmutableArray.CreateBuilder<string>();
            foreach (var clip in clipsElement.EnumerateObject())
            {
                var id = $"{actionName}-{clip.Name}";
                if (!IsSafeClipId(clip.Name) || !IsSafeClipId(id) ||
                    clip.Value.ValueKind != JsonValueKind.Object ||
                    clipIds.Count >= MaximumClipsPerAction ||
                    !localIds.TryAdd(clip.Name, id) ||
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

            var program = hasProgram
                ? ParseProgram(programElement, localIds, clips)
                : new ProgramDefinition(ImmutableArray<string>.Empty, clipIds.ToImmutable(), false);
            var transitions = hasTransitions
                ? ParseTransitions(transitionsElement, localIds, clips)
                : ImmutableArray<TransitionDefinition>.Empty;
            return new StateManifestDefinition(clipIds.ToImmutable(), program, transitions);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The pet state animation manifest is not valid JSON.", exception);
        }
    }

    private static ProgramDefinition ParseProgram(
        JsonElement element,
        IReadOnlyDictionary<string, string> localIds,
        ImmutableDictionary<string, ClipDefinition>.Builder clips)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidManifest();
        ImmutableArray<string>? enter = null;
        ImmutableArray<string>? loop = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "enter": enter = ParseLocalClipIds(field.Value, localIds, 4); break;
                case "loop": loop = ParseLocalClipIds(field.Value, localIds, MaximumClipsPerAction); break;
                default: throw InvalidManifest();
            }
        }
        if (enter is null || loop is not { IsEmpty: false }) throw InvalidManifest();
        ValidateOnceClips(enter.Value, clips);
        ValidateOnceClips(loop.Value, clips);
        return new ProgramDefinition(enter.Value, loop.Value, true);
    }

    private static ImmutableArray<TransitionDefinition> ParseTransitions(
        JsonElement element,
        IReadOnlyDictionary<string, string> localIds,
        ImmutableDictionary<string, ClipDefinition>.Builder clips)
    {
        if (element.ValueKind != JsonValueKind.Array) throw InvalidManifest();
        var transitions = ImmutableArray.CreateBuilder<TransitionDefinition>();
        var assignedTargets = new HashSet<PetAnimationKey>();
        foreach (var transition in element.EnumerateArray())
        {
            if (transition.ValueKind != JsonValueKind.Object || transitions.Count >= MaximumClipsPerAction)
                throw InvalidManifest();
            JsonElement toElement = default;
            JsonElement clipsElement = default;
            var hasTo = false;
            var hasClips = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in transition.EnumerateObject())
            {
                if (!seen.Add(field.Name)) throw InvalidManifest();
                switch (field.Name)
                {
                    case "to": toElement = field.Value; hasTo = true; break;
                    case "clips": clipsElement = field.Value; hasClips = true; break;
                    default: throw InvalidManifest();
                }
            }
            if (!hasTo || !hasClips || toElement.ValueKind != JsonValueKind.Array) throw InvalidManifest();
            var targets = ImmutableHashSet.CreateBuilder<PetAnimationKey>();
            foreach (var target in toElement.EnumerateArray())
            {
                if (target.ValueKind != JsonValueKind.String ||
                    !KeysByName.TryGetValue(target.GetString() ?? string.Empty, out var key) ||
                    !targets.Add(key) || !assignedTargets.Add(key)) throw InvalidManifest();
            }
            if (targets.Count == 0) throw InvalidManifest();
            var transitionClips = ParseLocalClipIds(clipsElement, localIds, 4);
            if (transitionClips.IsEmpty) throw InvalidManifest();
            ValidateOnceClips(transitionClips, clips);
            transitions.Add(new TransitionDefinition(targets.ToImmutable(), transitionClips));
        }
        return transitions.ToImmutable();
    }

    private static ImmutableArray<string> ParseLocalClipIds(
        JsonElement element,
        IReadOnlyDictionary<string, string> localIds,
        int maximum)
    {
        if (element.ValueKind != JsonValueKind.Array) throw InvalidManifest();
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !localIds.TryGetValue(item.GetString() ?? string.Empty, out var id) ||
                !seen.Add(id) || result.Count >= maximum) throw InvalidManifest();
            result.Add(id);
        }
        return result.ToImmutable();
    }

    private static void ValidateOnceClips(
        ImmutableArray<string> clipIds,
        ImmutableDictionary<string, ClipDefinition>.Builder clips)
    {
        if (clipIds.Any(id => clips[id].Playback != PetClipPlaybackMode.Once)) throw InvalidManifest();
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
                    PetClipPlaybackMode.Loop,
                    PetStatusAnchor.Default,
                    PetRenderTransform.Identity));
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
        PetStatusAnchor? statusAnchor = null;
        var renderTransform = PetRenderTransform.Identity;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seenFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "frames": frames = ParseFrames(field.Value, allFrames, ref totalFrames, framePrefix); break;
                case "frameDurationMs": frameDurationMs = ParseFrameDuration(field.Value); break;
                case "playback": playback = ParsePlayback(field.Value); break;
                case "statusAnchor": statusAnchor = ParseStatusAnchor(field.Value); break;
                case "renderTransform": renderTransform = ParseRenderTransform(field.Value); break;
                default: throw InvalidManifest();
            }
        }
        return new ClipDefinition(
            frames ?? throw InvalidManifest(),
            frameDurationMs ?? throw InvalidManifest(),
            playback ?? throw InvalidManifest(),
            statusAnchor ?? throw InvalidManifest(),
            renderTransform);
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

    private static PetStatusAnchor ParseStatusAnchor(JsonElement element)
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
        return x is { } anchorX && y is { } anchorY
            ? new PetStatusAnchor(anchorX, anchorY)
            : throw InvalidManifest();
    }

    private static PetRenderTransform ParseRenderTransform(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidManifest();
        double? scale = null;
        PetRenderPoint? origin = null;
        PetRenderOffset? offset = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw InvalidManifest();
            switch (property.Name)
            {
                case "scale":
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetDouble(out var scaleValue) ||
                        !double.IsFinite(scaleValue) || scaleValue < 0.5d || scaleValue > 1.5d)
                    {
                        throw InvalidManifest();
                    }
                    scale = scaleValue;
                    break;
                case "origin": origin = ParseRenderPoint(property.Value); break;
                case "offset": offset = ParseRenderOffset(property.Value); break;
                default: throw InvalidManifest();
            }
        }
        return scale is { } validScale && origin is { } validOrigin && offset is { } validOffset
            ? new PetRenderTransform(validScale, validOrigin, validOffset)
            : throw InvalidManifest();
    }

    private static PetRenderPoint ParseRenderPoint(JsonElement element)
    {
        var (x, y) = ParseRenderCoordinates(element, 0d, 1d);
        return new PetRenderPoint(x, y);
    }

    private static PetRenderOffset ParseRenderOffset(JsonElement element)
    {
        var (x, y) = ParseRenderCoordinates(element, -0.25d, 0.25d);
        return new PetRenderOffset(x, y);
    }

    private static (double X, double Y) ParseRenderCoordinates(JsonElement element, double minimum, double maximum)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidManifest();
        double? x = null;
        double? y = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetDouble(out var value) || !double.IsFinite(value) ||
                value < minimum || value > maximum)
            {
                throw InvalidManifest();
            }
            switch (property.Name)
            {
                case "x": x = value; break;
                case "y": y = value; break;
                default: throw InvalidManifest();
            }
        }
        return x is { } coordinateX && y is { } coordinateY
            ? (coordinateX, coordinateY)
            : throw InvalidManifest();
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

    private sealed record ActionDefinition(
        ImmutableArray<string> ClipIds,
        PetAnimationKey? Fallback,
        ProgramDefinition? Program = null,
        ImmutableArray<TransitionDefinition>? Transitions = null);
    private sealed record ProgramDefinition(
        ImmutableArray<string> Enter,
        ImmutableArray<string> Loop,
        bool LoopRepeats);
    private sealed record TransitionDefinition(
        ImmutableHashSet<PetAnimationKey> Targets,
        ImmutableArray<string> ClipIds);
    private sealed record StateManifestDefinition(
        ImmutableArray<string> ClipIds,
        ProgramDefinition Program,
        ImmutableArray<TransitionDefinition> Transitions);
    private sealed record LegacyActionDefinition(ImmutableArray<string> Frames, PetAnimationKey? Fallback);
    private sealed record ClipDefinition(
        ImmutableArray<string> Frames,
        int FrameDurationMs,
        PetClipPlaybackMode Playback,
        PetStatusAnchor StatusAnchor,
        PetRenderTransform RenderTransform);
}
