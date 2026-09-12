using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace PetHelper;

internal sealed record StoredCharacterClip(string[] Frames, int[] Durations);
internal sealed record StoredCharacter(int LibraryFormatVersion, string Name, PetStatusAnchor StatusAnchor,
    double Baseline, Dictionary<string, StoredCharacterClip> Actions);
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
        references = document.Actions.Values.SelectMany(c => c.Frames).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, value) in document.Actions)
        {
            var animationKey = CharacterManifest.Keys[key];
            var clip = new ResolvedClip(animationKey, id + "-" + key, value.Frames.ToImmutableArray(), value.Durations[0],
                PetClipPlaybackMode.Loop, document.StatusAnchor) { FrameDurationsMs = value.Durations.ToImmutableArray() };
            programs.Add(animationKey, new(animationKey, [], [clip], [], false));
        }
    }

    internal ResolvedStateProgram ResolveProgram(PetAnimationKey key, Func<string, bool> available)
    {
        var program = programs.GetValueOrDefault(key) ?? programs[PetAnimationKey.Idle];
        if (program.Loop[0].Frames.All(available)) return program;
        // A broken selected character fails as a unit; never mix another character's actions.
        throw CharacterManifest.Invalid();
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
        CharacterManifest.Fields(root, "libraryFormatVersion", "name", "statusAnchor", "baseline", "actions");
        CharacterManifest.Integer(root.GetProperty("libraryFormatVersion"), 1, 1);
        var actions = new Dictionary<string, StoredCharacterClip>(StringComparer.Ordinal);
        var input = root.GetProperty("actions");
        if (input.ValueKind != JsonValueKind.Object) throw CharacterManifest.Invalid();
        var total = 0;
        foreach (var entry in input.EnumerateObject())
        {
            if (!CharacterManifest.Keys.ContainsKey(entry.Name)) throw CharacterManifest.Invalid();
            CharacterManifest.Fields(entry.Value, "frames", "durations");
            var frameElements = entry.Value.GetProperty("frames");
            var delayElements = entry.Value.GetProperty("durations");
            if (frameElements.ValueKind != JsonValueKind.Array || delayElements.ValueKind != JsonValueKind.Array ||
                frameElements.GetArrayLength() is < 1 or > 240 || frameElements.GetArrayLength() != delayElements.GetArrayLength())
                throw CharacterManifest.Invalid();
            var frames = frameElements.EnumerateArray().Select(CharacterManifest.Text).ToArray();
            for (var i = 0; i < frames.Length; i++)
                if (frames[i] != $"frames/{entry.Name}/{i:D4}.png") throw CharacterManifest.Invalid();
            total += frames.Length;
            if (total > 1024) throw CharacterManifest.Invalid();
            actions.Add(entry.Name, new(frames, delayElements.EnumerateArray().Select(d => CharacterManifest.Integer(d, 16, 10000)).ToArray()));
        }
        if (!actions.ContainsKey("idle")) throw CharacterManifest.Invalid();
        return new(1, CharacterManifest.ValidateName(CharacterManifest.Text(root.GetProperty("name"))),
            CharacterManifest.Anchor(root.GetProperty("statusAnchor")), CharacterManifest.Unit(root.GetProperty("baseline")), actions);
    }
}
