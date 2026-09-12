using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace PetHelper;

internal sealed record StoredCharacterClip(string[] Frames, int[] Durations);
internal sealed record StoredCharacterExtra(string Name, string[] Frames, int[] Durations);
internal sealed record StoredCharacterState(StoredCharacterClip Primary, StoredCharacterExtra[] Extras);
internal sealed record StoredCharacter(int LibraryFormatVersion, string Name, PetStatusAnchor StatusAnchor,
    double Baseline, int ExtrasCooldownMs, Dictionary<string, StoredCharacterState> Actions);
internal sealed record CharacterInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed class CharacterAssetSource
{
    internal string Id { get; }
    internal StoredCharacter Document { get; }
    private readonly string directory;
    private readonly HashSet<string> references;
    private readonly Dictionary<PetAnimationKey, ResolvedStateProgram> programs = [];

    internal CharacterAssetSource(string id, string directory, StoredCharacter document)
    {
        Id = id;
        this.directory = CharacterFiles.CheckedPath(directory);
        Document = document;
        references = document.Actions.Values
            .SelectMany(state => state.Primary.Frames.Concat(state.Extras.SelectMany(extra => extra.Frames)))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, value) in document.Actions)
        {
            var animationKey = CharacterManifest.Keys[key];
            var primary = ToClip(animationKey, $"{id}-{key}-primary", value.Primary, document.StatusAnchor,
                PetClipPlaybackMode.Loop);
            // An extra is interleaved into the primary playback, so it must be one-shot: the
            // coordinator returns to the primary when the clip completes, and a looping clip
            // never completes (a single-frame loop is not even animating).
            var extras = value.Extras
                .Select((extra, index) => ToClip(animationKey, $"{id}-{key}-extra-{index}",
                    new StoredCharacterClip(extra.Frames, extra.Durations), document.StatusAnchor,
                    PetClipPlaybackMode.Once) with { Label = extra.Name })
                .ToImmutableArray();
            programs.Add(animationKey, new(animationKey, [], [primary], [], false)
            {
                Extras = extras,
                ExtrasCooldownMs = document.ExtrasCooldownMs,
            });
        }
    }

    private static ResolvedClip ToClip(PetAnimationKey key, string id, StoredCharacterClip clip, PetStatusAnchor anchor,
        PetClipPlaybackMode playback) =>
        new(key, id, clip.Frames.ToImmutableArray(), clip.Durations[0], playback, anchor)
        {
            FrameDurationsMs = clip.Durations.ToImmutableArray(),
        };

    internal ResolvedStateProgram ResolveProgram(PetAnimationKey key, Func<string, bool> available)
    {
        var program = programs.GetValueOrDefault(key) ?? programs[PetAnimationKey.Idle];
        if (program.Loop[0].Frames.All(available)) return program;
        // A broken selected character fails as a unit; never mix another character's actions.
        throw CharacterManifest.Invalid();
    }

    /// <summary>Lists the imported primary followed by the named extras, which the character window previews.</summary>
    internal ImmutableArray<PetActionChoice> ResolveActions(PetAnimationKey key, Func<string, bool> available)
    {
        var program = ResolveProgram(key, available);
        return PetActionCatalog.Build(program, "默认动作", "附加动作");
    }

    internal byte[] ReadFrame(string frame)
    {
        if (!references.Contains(frame)) throw CharacterManifest.Invalid();
        return CharacterFiles.Read(CharacterFiles.Child(directory, frame), 2 * 1024 * 1024);
    }

    internal bool HasFrame(string frame)
    {
        if (!references.Contains(frame)) return false;
        var path = CharacterFiles.Child(directory, frame);
        return File.Exists(path);
    }

    internal static StoredCharacter ParseStored(string json)
    {
        using var document = CharacterManifest.ReadJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw CharacterManifest.Invalid();
        var required = new[] { "libraryFormatVersion", "name", "statusAnchor", "baseline", "actions" };
        var version = CharacterManifest.Integer(root.GetProperty("libraryFormatVersion"), 1, 2);
        var allowed = version == 2
            ? required.Append("extrasCooldownMs").ToHashSet(StringComparer.Ordinal)
            : required.ToHashSet(StringComparer.Ordinal);
        var seen = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!seen.IsSubsetOf(allowed) || required.Any(name => !seen.Contains(name))) throw CharacterManifest.Invalid();
        var cooldownMs = version == 2 && root.TryGetProperty("extrasCooldownMs", out var cooldownElement)
            ? CharacterManifest.Integer(cooldownElement, 5000, 600000)
            : CharacterManifest.DefaultExtrasCooldownMs;

        var actions = new Dictionary<string, StoredCharacterState>(StringComparer.Ordinal);
        var input = root.GetProperty("actions");
        if (input.ValueKind != JsonValueKind.Object) throw CharacterManifest.Invalid();
        var total = 0;
        foreach (var entry in input.EnumerateObject())
        {
            if (!CharacterManifest.Keys.ContainsKey(entry.Name)) throw CharacterManifest.Invalid();
            if (version == 1)
            {
                actions.Add(entry.Name, new(ParseStoredClip(entry.Value, $"frames/{entry.Name}/", ref total), []));
                continue;
            }

            CharacterManifest.Fields(entry.Value, "primary", "extras");
            var primary = ParseStoredClip(entry.Value.GetProperty("primary"), $"frames/{entry.Name}/primary/", ref total);
            var extrasElement = entry.Value.GetProperty("extras");
            if (extrasElement.ValueKind != JsonValueKind.Array ||
                extrasElement.GetArrayLength() > CharacterManifest.MaximumExtrasPerState) throw CharacterManifest.Invalid();
            var extras = new List<StoredCharacterExtra>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < extrasElement.GetArrayLength(); index++)
            {
                var extraElement = extrasElement[index];
                CharacterManifest.Fields(extraElement, "name", "frames", "durations");
                var name = CharacterManifest.ValidateName(CharacterManifest.Text(extraElement.GetProperty("name")));
                if (!names.Add(name)) throw CharacterManifest.Invalid();
                var clip = ParseStoredClip(extraElement, $"frames/{entry.Name}/extra-{index}/", ref total, isExtra: true);
                extras.Add(new(name, clip.Frames, clip.Durations));
            }
            actions.Add(entry.Name, new(primary, extras.ToArray()));
        }
        if (!actions.ContainsKey("idle")) throw CharacterManifest.Invalid();
        return new(version, CharacterManifest.ValidateName(CharacterManifest.Text(root.GetProperty("name"))),
            CharacterManifest.Anchor(root.GetProperty("statusAnchor")), CharacterManifest.Unit(root.GetProperty("baseline")),
            cooldownMs, actions);
    }

    private static StoredCharacterClip ParseStoredClip(JsonElement element, string expectedPrefix, ref int total,
        bool isExtra = false)
    {
        // A stored extra keeps its display name in the same object as its clip, so its accepted
        // field set has exactly one more member than a stored primary.
        string[] expected = isExtra ? ["name", "frames", "durations"] : ["frames", "durations"];
        CharacterManifest.Fields(element, expected);
        var frameElements = element.GetProperty("frames");
        var delayElements = element.GetProperty("durations");
        if (frameElements.ValueKind != JsonValueKind.Array || delayElements.ValueKind != JsonValueKind.Array ||
            frameElements.GetArrayLength() is < 1 or > 240 ||
            frameElements.GetArrayLength() != delayElements.GetArrayLength()) throw CharacterManifest.Invalid();
        var frames = frameElements.EnumerateArray().Select(CharacterManifest.Text).ToArray();
        for (var i = 0; i < frames.Length; i++)
            if (frames[i] != $"{expectedPrefix}{i:D4}.png") throw CharacterManifest.Invalid();
        total += frames.Length;
        if (total > 1024) throw CharacterManifest.Invalid();
        return new(frames, delayElements.EnumerateArray().Select(d => CharacterManifest.Integer(d, 16, 10000)).ToArray());
    }
}
