using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows.Media.Imaging;

namespace PetHelper;

/// <summary>
/// One normalized action of an already imported character, staged outside the library until it is
/// committed.  The staging directory is deleted unless the commit moved it into the library.
/// </summary>
internal sealed class CharacterActionDraft : IDisposable
{
    internal string CharacterId { get; }
    internal string StateKey { get; }
    internal bool AsPrimary { get; }
    internal string Folder { get; }
    internal StoredCharacterClip Clip { get; set; } = null!;
    internal string DirectoryPath { get; }
    internal bool Committed { get; set; }
    private readonly string stagingRoot;

    internal CharacterActionDraft(string characterId, string stateKey, bool asPrimary, string folder, string stagingRoot)
    {
        CharacterId = characterId;
        StateKey = stateKey;
        AsPrimary = asPrimary;
        Folder = folder;
        this.stagingRoot = CharacterFiles.CheckedPath(stagingRoot);
        DirectoryPath = CharacterFiles.Child(this.stagingRoot, "build");
    }

    /// <summary>
    /// Drops the staging tree, which a successful commit has already emptied by moving the build
    /// directory into the library.  The wrapping folder goes too, so a committed action does not
    /// leave one empty <c>action-*</c> folder per write behind in staging.
    /// </summary>
    internal void Cleanup()
    {
        try
        {
            CharacterFiles.DeleteTree(stagingRoot, "build");
            // The staging root is an empty, already checked directory of ours at this point; removing
            // it is the same bounded cleanup the draft above performs for a discarded character.
            Directory.Delete(stagingRoot);
        }
        catch { /* Cleanup never logs source paths. */ }
    }

    public void Dispose()
    {
        if (Committed) return;
        Cleanup();
    }
}

internal sealed class CharacterDraft : IDisposable
{
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal string DirectoryPath { get; }
    internal StoredCharacter Document { get; set; } = null!;
    internal bool Committed { get; set; }
    private readonly string staging;
    internal CharacterDraft(string staging)
    {
        this.staging = staging;
        DirectoryPath = CharacterFiles.Child(staging, Id);
        Directory.CreateDirectory(DirectoryPath);
    }
    internal CharacterAssetSource Source => new(Id, DirectoryPath, Document);
    public void Dispose()
    {
        if (!Committed)
        {
            try { CharacterFiles.DeleteTree(staging, Id); } catch { /* Cleanup never logs source paths. */ }
        }
    }
}

internal sealed class CharacterLibrary
{
    private const long LibraryLimit = 1024L * 1024 * 1024;
    // The stored character manifest is a local, human-inspectable file that keeps the user's
    // character and action names verbatim, as the library design shows them.  Only non-ASCII text
    // is left unescaped: HTML-sensitive and control characters stay escaped, and this document is
    // never sent over the JSON Lines protocol or displayed as markup.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
    private readonly string root;
    private string LibraryPath => CharacterFiles.Child(root, "library");
    private string StagingPath => CharacterFiles.Child(root, "staging");
    internal CharacterLibrary() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DshPngPet", "Characters")) { }
    internal CharacterLibrary(string root) { this.root = Path.GetFullPath(root); }

    private FileStream OpenLock()
    {
        CharacterFiles.CheckedPath(root);
        Directory.CreateDirectory(root);
        return CharacterFiles.Open(CharacterFiles.Child(root, "library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private FileStream Lock() => OpenLock();

    /// <summary>Takes the library lock without waiting, or reports that another writer holds it.</summary>
    private FileStream? TryLock()
    {
        try { return OpenLock(); }
        catch { return null; }
    }

    private static string Id(string value)
    {
        if (value.Length != 32 || value.Any(c => !char.IsAsciiHexDigit(c)) || value != value.ToLowerInvariant())
            throw CharacterManifest.Invalid();
        return value;
    }

    /// <summary>
    /// Puts back a character a hard kill stranded in staging.  A commit renames the library entry out
    /// of the way and only then renames the rebuilt one in, so an interrupted commit leaves the
    /// character missing from the library with every byte still inside
    /// <c>staging/retired-&lt;id&gt;-&lt;guid&gt;</c>.  Every read of the library recovers first, so the
    /// orphaned state is never what the user sees.  A commit owns staging while it holds the library
    /// lock, and an id the library still has is never replaced by a copy that was left behind.
    /// </summary>
    private void RecoverStranded()
    {
        try
        {
            if (!Directory.Exists(StagingPath)) return;
            var stranded = Directory.EnumerateDirectories(StagingPath, "retired-*")
                .Select(path => (Path: path, Id: RetiredId(Path.GetFileName(path))))
                .Where(candidate => candidate.Id is not null &&
                    !Directory.Exists(CharacterFiles.Child(LibraryPath, candidate.Id!)))
                .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
                .ToArray();
            if (stranded.Length == 0) return;
            // A live commit holds the lock from before the first move to after the second one, so a
            // lock that cannot be taken means the retired tree belongs to a running commit, not to a
            // crash, and must be left alone.
            using var gate = TryLock();
            if (gate is null) return;
            Directory.CreateDirectory(LibraryPath);
            foreach (var candidate in stranded)
            {
                var target = CharacterFiles.Child(LibraryPath, candidate.Id!);
                if (Directory.Exists(target)) continue;
                try { Directory.Move(candidate.Path, target); }
                catch { /* Another reader adopted it first, or staging is not ours to move. */ }
            }
        }
        catch { /* Recovery is best effort: a read never fails because it could not run. */ }
    }

    /// <summary>The character id a retired staging directory was named for, or null for anything else.</summary>
    private static string? RetiredId(string name)
    {
        const string prefix = "retired-";
        if (name.Length < prefix.Length + 34 || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            name[prefix.Length + 32] != '-') return null;
        try { return Id(name.Substring(prefix.Length, 32)); }
        catch { return null; }
    }

    internal IReadOnlyList<CharacterInfo> List()
    {
        var result = new List<CharacterInfo>();
        RecoverStranded();
        if (!Directory.Exists(LibraryPath)) return result;
        foreach (var path in Directory.EnumerateDirectories(LibraryPath).Take(51))
        {
            try
            {
                var id = Id(Path.GetFileName(path));
                var source = Load(id);
                result.Add(new(id, source.Document.Name));
            }
            catch { /* Invalid entries are not interpreted or displayed. */ }
        }
        return result.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    }

    internal CharacterAssetSource Load(string id)
    {
        // The library is read before it is looked at: a character an interrupted commit stranded in
        // staging is restored here, which is also what lets the stored selection resolve again.
        RecoverStranded();
        var directory = CharacterFiles.Child(LibraryPath, Id(id));
        var document = CharacterAssetSource.ParseStored(Encoding.UTF8.GetString(
            CharacterFiles.Read(CharacterFiles.Child(directory, "character.json"), 65536)));
        return new(id, directory, document);
    }

    internal string? SelectedId
    {
        get
        {
            try
            {
                using var document = CharacterManifest.ReadJson(Encoding.UTF8.GetString(
                    CharacterFiles.Read(CharacterFiles.Child(root, "selection.json"), 512)));
                CharacterManifest.Fields(document.RootElement, "version", "selectedCharacterId");
                CharacterManifest.Integer(document.RootElement.GetProperty("version"), 1, 1);
                var selected = document.RootElement.GetProperty("selectedCharacterId");
                if (selected.ValueKind == JsonValueKind.Null) return null;
                var id = Id(CharacterManifest.Text(selected));
                Load(id);
                return id;
            }
            catch { return null; }
        }
    }

    internal void Select(string? id)
    {
        using var gate = Lock();
        if (id is not null) Load(id);
        WriteSelection(id);
    }

    private void WriteSelection(string? id)
    {
        var temporary = CharacterFiles.Child(root, "selection-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            CharacterFiles.WriteNew(temporary, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, selectedCharacterId = id }));
            File.Move(temporary, CharacterFiles.Child(root, "selection.json"), overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(CharacterFiles.CheckedPath(temporary)); }
    }

    internal CharacterDraft PrepareImage(string file, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var bytes = CharacterFiles.Read(file, 20 * 1024 * 1024);
        var gif = GifFrameImporter.IsGif(bytes);
        var manifest = new CharacterManifest("新人物", new(0.5, 0.12), 0.95, CharacterManifest.DefaultExtrasCooldownMs,
            new() { ["idle"] = new(new(gif ? "gif" : "png", ["image"], 100, DurationDeclared: false), []) });
        // The synthetic manifest's "image" reference is a placeholder: the source file was already
        // read, so every reference resolves to those same bytes.
        return Prepare(manifest, _ => bytes, cancellation);
    }

    internal CharacterDraft PrepareDirectory(string directory, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var manifest = CharacterManifest.Parse(Encoding.UTF8.GetString(
            CharacterFiles.Read(CharacterFiles.Child(directory, "character.json"), 65536)));
        // Manifest references are relative to the directory the user picked, never to the process
        // working directory; AssetReference has already rejected absolute and escaping values.
        return Prepare(manifest, reference => CharacterFiles.Read(CharacterFiles.Child(directory, reference), 20 * 1024 * 1024),
            cancellation);
    }

    private CharacterDraft Prepare(CharacterManifest manifest, Func<string, byte[]> readAsset, CancellationToken cancellation)
    {
        using var gate = Lock();
        Directory.CreateDirectory(LibraryPath);
        Directory.CreateDirectory(StagingPath);
        if (Directory.EnumerateDirectories(LibraryPath).Take(50).Count() >= 50) throw CharacterManifest.Invalid();
        var existingBytes = CharacterFiles.Size(root);
        if (existingBytes >= LibraryLimit) throw CharacterManifest.Invalid();
        var draft = new CharacterDraft(StagingPath);
        try
        {
            var budget = new CharacterImportBudget();
            var actions = new Dictionary<string, StoredCharacterState>(StringComparer.Ordinal);
            long outputBytes = 0;
            foreach (var (key, state) in manifest.Actions)
            {
                var primary = WriteActionFrames(draft.DirectoryPath, key, "primary", state.Primary, readAsset,
                    isExtra: false, budget, canvasSide: null, cancellation, ref outputBytes, existingBytes);
                var extras = new List<StoredCharacterExtra>();
                for (var index = 0; index < state.Extras.Length; index++)
                {
                    var clip = WriteActionFrames(draft.DirectoryPath, key, $"extra-{index}", state.Extras[index].Action,
                        readAsset, isExtra: true, budget, canvasSide: null, cancellation, ref outputBytes, existingBytes);
                    extras.Add(new(state.Extras[index].Name, clip.Frames, clip.Durations));
                }
                actions.Add(key, new(primary, extras.ToArray()));
            }
            var side = Math.Max(budget.Width, budget.Height);
            var anchor = new PetStatusAnchor((side - budget.Width) / (2d * side) + manifest.StatusAnchor.X * budget.Width / side,
                (side - budget.Height + manifest.StatusAnchor.Y * budget.Height) / side);
            var baseline = (side - budget.Height + manifest.Baseline * budget.Height) / side;
            draft.Document = new(2, manifest.Name, anchor, baseline, manifest.ExtrasCooldownMs, actions);
            cancellation.ThrowIfCancellationRequested();
            return draft;
        }
        catch { draft.Dispose(); throw; }
    }

    private static StoredCharacterClip WriteActionFrames(string characterDirectory, string key, string folder,
        CharacterAction action, Func<string, byte[]> readAsset, bool isExtra, CharacterImportBudget budget, int? canvasSide,
        CancellationToken cancellation, ref long outputBytes, long existingBytes)
    {
        var references = new List<string>();
        var delays = new List<int>();
        var written = outputBytes;
        var folderPath = $"frames/{key}/{folder}";
        // The declared duration means something different per action type, and the importer already
        // applies the right one: a png-sequence declares the duration of every frame and the non-GIF
        // branch stamps it on each of them, while a GIF carries its own per-frame delays.  Only the
        // single-frame GIF case needs the post-processing below.
        Directory.CreateDirectory(CharacterFiles.Child(characterDirectory, folderPath));
        foreach (var file in action.Files)
        {
            cancellation.ThrowIfCancellationRequested();
            // The importer never resolves source paths itself: the caller supplies the mapping from a
            // manifest asset reference to bytes (the chosen directory, or an already read image).
            GifFrameImporter.Decode(readAsset(file), action.Type == "gif", action.FrameDurationMs,
                budget, cancellation, canvasSide, (bitmap, delay) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (references.Count >= 240) throw CharacterManifest.Invalid();
                var reference = $"{folderPath}/{references.Count:D4}.png";
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var memory = new MemoryStream();
                encoder.Save(memory);
                var bytes = memory.ToArray();
                written += bytes.Length;
                if (written > 256L * 1024 * 1024 || existingBytes + written + 65536 > LibraryLimit)
                    throw CharacterManifest.Invalid();
                CharacterFiles.WriteNew(CharacterFiles.Child(characterDirectory, reference), bytes);
                references.Add(reference);
                delays.Add(delay);
            });
        }
        outputBytes = written;
        if (isExtra && references.Count == 1 && !action.DurationDeclared) throw CharacterManifest.Invalid();
        // A GIF's own per-frame delays win, so a multi-frame GIF keeps them even when the author
        // declared a duration.  A GIF that decodes to exactly one frame has no timing of its own to
        // keep: it would flash past at the importer's 100 ms default, so its declared duration wins.
        // Only an extra can declare a duration for a gif action, and a png-sequence never reaches here.
        if (isExtra && action.Type == "gif" && action.DurationDeclared && references.Count == 1)
            delays[0] = action.FrameDurationMs;
        return new(references.ToArray(), delays.ToArray());
    }

    /// <summary>Adds one named one-shot extra to a state of an already imported character.</summary>
    internal CharacterInfo AddExtra(string id, string file, string stateKey, string name, PetStatusAnchor anchor,
        double baseline, CancellationToken cancellation)
    {
        using var draft = PrepareAction(id, file, stateKey, asPrimary: false, cancellation);
        return CommitAction(draft, name, anchor, baseline);
    }

    /// <summary>Decodes one GIF or PNG into the staging directory, normalised to the character's existing canvas.</summary>
    internal CharacterActionDraft PrepareAction(string id, string file, string stateKey, bool asPrimary,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var gate = Lock();
        Id(id);
        if (!CharacterManifest.Keys.ContainsKey(stateKey)) throw CharacterManifest.Invalid();
        var document = Load(id).Document;
        var existing = document.Actions.TryGetValue(stateKey, out var state) ? state : null;
        if (!asPrimary && existing is not null && existing.Extras.Length >= CharacterManifest.MaximumExtrasPerState)
            throw CharacterManifest.Invalid();
        var directory = CharacterFiles.Child(LibraryPath, id);
        var canvasSide = CanvasSide(directory, document);
        var existingBytes = CharacterFiles.Size(root);
        if (existingBytes >= LibraryLimit) throw CharacterManifest.Invalid();
        var folder = asPrimary ? "primary" : $"extra-{existing?.Extras.Length ?? 0}";
        var stagingRoot = CharacterFiles.Child(StagingPath, "action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var draft = new CharacterActionDraft(id, stateKey, asPrimary, folder, stagingRoot);
        Directory.CreateDirectory(draft.DirectoryPath);
        long draftOutputBytes = 0;
        try
        {
            // The source file is read exactly once and never resolved again by the importer, which
            // would otherwise reopen a path the caller only lent us in memory.
            var bytes = CharacterFiles.Read(file, 20 * 1024 * 1024);
            var gif = GifFrameImporter.IsGif(bytes);
            // A single static frame must carry its own display duration; 1500 ms is the dialog default.
            var action = new CharacterAction(gif ? "gif" : "png", [file], gif ? 100 : 1500, DurationDeclared: !gif);
            draft.Clip = WriteActionFrames(draft.DirectoryPath, stateKey, folder, action, _ => bytes, isExtra: !asPrimary,
                new CharacterImportBudget(), canvasSide, cancellation, ref draftOutputBytes, existingBytes);
            return draft;
        }
        catch { draft.Dispose(); throw; }
    }

    /// <summary>
    /// Merges a prepared action into the stored document and publishes it.  A primary replaces that
    /// state's primary, an extra is appended under the next free <c>extra-N</c> folder.
    /// </summary>
    internal CharacterInfo CommitAction(CharacterActionDraft draft, string? name, PetStatusAnchor anchor, double baseline)
    {
        using var gate = Lock();
        if (draft.Committed || !anchor.IsWithinArtboard || !double.IsFinite(baseline) || baseline is < 0 or > 1)
            throw CharacterManifest.Invalid();
        var id = Id(draft.CharacterId);
        var document = Load(id).Document;
        var states = new Dictionary<string, StoredCharacterState>(document.Actions, StringComparer.Ordinal);
        var current = states.TryGetValue(draft.StateKey, out var existing)
            ? existing
            : new StoredCharacterState(new StoredCharacterClip([], []), []);
        // The draft fixed the folder it built its frames in when it was prepared, and this is the
        // folder the committed document will reference.  Re-derive it here so two drafts prepared
        // before either commit cannot both claim extra-N, and refuse before any directory move:
        // staged frames in a folder the document does not name would collide with the frames of the
        // extra that owns that folder, or be stored where the parser rejects them.
        if (!string.Equals(draft.Folder, draft.AsPrimary ? "primary" : $"extra-{current.Extras.Length}",
                StringComparison.Ordinal)) throw CharacterManifest.Invalid();
        // An extra is interleaved into its primary and needs one to return to; a state without a
        // primary cannot store an extra at all, so refuse instead of failing after the first move.
        if (!draft.AsPrimary && current.Primary.Frames.Length == 0) throw CharacterManifest.Invalid();
        StoredCharacterState updated;
        if (draft.AsPrimary)
        {
            updated = new(draft.Clip, current.Extras);
        }
        else
        {
            var extraName = CharacterManifest.ValidateName(name ?? throw CharacterManifest.Invalid());
            if (current.Extras.Any(extra => string.Equals(extra.Name, extraName, StringComparison.Ordinal)))
                throw CharacterManifest.Invalid();
            updated = new(current.Primary, [.. current.Extras, new(extraName, draft.Clip.Frames, draft.Clip.Durations)]);
        }
        states[draft.StateKey] = updated;
        var next = document with
        {
            // Any write upgrades a version one library to the version two layout, which the commit
            // below has to build frame by frame.
            LibraryFormatVersion = 2,
            Name = name is null ? document.Name : CharacterManifest.ValidateName(name),
            StatusAnchor = anchor,
            Baseline = baseline,
            Actions = UpgradeDocument(states),
        };
        // A new primary replaces the frames of that state, so the retired ones are not moved over.
        var dropped = draft.AsPrimary && current.Primary.Frames.Length > 0
            ? current.Primary.Frames.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        CommitDirectory(id, document, next, draft, dropped, new Dictionary<string, string>(StringComparer.Ordinal));
        draft.Committed = true;
        return new(id, next.Name);
    }

    /// <summary>Replaces one state's primary action, or supplies a primary for a state that had none.</summary>
    internal CharacterInfo ReplacePrimary(string id, string file, string stateKey, PetStatusAnchor anchor,
        double baseline, CancellationToken cancellation)
    {
        using var draft = PrepareAction(id, file, stateKey, asPrimary: true, cancellation);
        return CommitAction(draft, null, anchor, baseline);
    }

    /// <summary>
    /// Removes one named extra and renumbers the survivors, so the array index of every remaining
    /// extra still equals the <c>extra-N</c> folder its frames live in.
    /// </summary>
    internal void RemoveExtra(string id, string stateKey, string name)
    {
        using var gate = Lock();
        var checkedId = Id(id);
        if (!CharacterManifest.Keys.ContainsKey(stateKey)) throw CharacterManifest.Invalid();
        var document = Load(checkedId).Document;
        if (!document.Actions.TryGetValue(stateKey, out var state) || state.Extras.Length == 0)
            throw CharacterManifest.Invalid();
        var index = Array.FindIndex(state.Extras, extra => string.Equals(extra.Name, name, StringComparison.Ordinal));
        if (index < 0) throw CharacterManifest.Invalid();
        var removed = state.Extras[index];
        // Compacted order: element N of the result belongs in extra-N, wherever it lived before.
        var remaining = state.Extras.Where((_, position) => position != index).ToArray();
        var relocated = new Dictionary<string, string>(StringComparer.Ordinal);
        var rebuilt = new List<StoredCharacterExtra>(remaining.Length);
        for (var position = 0; position < remaining.Length; position++)
        {
            var extra = remaining[position];
            var frames = new string[extra.Frames.Length];
            for (var frame = 0; frame < extra.Frames.Length; frame++)
            {
                var target = $"frames/{stateKey}/extra-{position}/{frame:D4}.png";
                relocated[extra.Frames[frame]] = target;
                frames[frame] = target;
            }
            rebuilt.Add(new(extra.Name, frames, extra.Durations));
        }
        var states = new Dictionary<string, StoredCharacterState>(document.Actions, StringComparer.Ordinal)
        {
            [stateKey] = new(state.Primary, rebuilt.ToArray()),
        };
        var next = document with { Actions = states };
        var dropped = removed.Frames.ToHashSet(StringComparer.Ordinal);
        // There is no new clip to install: the draft only supplies the empty directory the rebuilt
        // character is assembled in, and the commit finishes by moving it over the retired one.  Its
        // own cleanup covers both outcomes, so Dispose below never has to delete a committed tree.
        using var draft = new CharacterActionDraft(checkedId, stateKey, asPrimary: false, "unused",
            CharacterFiles.Child(StagingPath, "action-" + Guid.NewGuid().ToString("N")));
        draft.Clip = new StoredCharacterClip([], []);
        CommitDirectory(checkedId, document, next, draft, dropped, relocated);
        draft.Committed = true;
    }

    /// <summary>
    /// Turns every primary clip of the document into its version two reference form.  A version one
    /// manifest keeps its frames directly under the state folder, so those references have to gain
    /// the <c>primary/</c> segment before the document can be validated and stored as version two.
    /// Extras only exist from version two and are already canonical.
    /// </summary>
    private static Dictionary<string, StoredCharacterState> UpgradeDocument(Dictionary<string, StoredCharacterState> states)
    {
        var upgraded = new Dictionary<string, StoredCharacterState>(states.Count, StringComparer.Ordinal);
        foreach (var (key, state) in states)
        {
            var frames = new string[state.Primary.Frames.Length];
            for (var index = 0; index < frames.Length; index++)
                frames[index] = $"frames/{key}/primary/{index:D4}.png";
            upgraded[key] = state with { Primary = state.Primary with { Frames = frames } };
        }
        return upgraded;
    }

    /// <summary>
    /// Rebuilds the character directory from the retired one plus the staged action.  Frames in
    /// <paramref name="droppedReferences"/> are left behind, frames listed in
    /// <paramref name="relocatedReferences"/> move to their new reference.  The library entry is
    /// absent only for the two directory moves, and any failure restores the previous one.
    /// </summary>
    private void CommitDirectory(string id, StoredCharacter previous, StoredCharacter next,
        CharacterActionDraft draft, IReadOnlySet<string> droppedReferences,
        IReadOnlyDictionary<string, string> relocatedReferences)
    {
        var target = CharacterFiles.Child(LibraryPath, id);
        // The retired name carries the character id, so a kill between the two moves below leaves a
        // tree that RecoverStranded can put back under the id the library looks for.
        var retired = CharacterFiles.Child(StagingPath, $"retired-{id}-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(target, retired);
            MoveExistingFrames(retired, draft.DirectoryPath, previous, droppedReferences, relocatedReferences);
            InstallDraftFrames(draft);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
            CharacterAssetSource.ParseStored(Encoding.UTF8.GetString(bytes));
            CharacterFiles.WriteNew(CharacterFiles.Child(draft.DirectoryPath, "character.json"), bytes);
            Directory.Move(draft.DirectoryPath, target);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(retired)) Directory.Move(retired, target);
            // The draft tree is deleted here as well, because a caller that pre-marked its draft as
            // committed must not leave one behind in staging.
            draft.Cleanup();
            throw;
        }
        finally
        {
            if (Directory.Exists(retired)) CharacterFiles.DeleteTree(StagingPath, Path.GetFileName(retired));
            // Both callers still hold the draft, but its staging root is spent now that the build
            // directory lives in the library.
            draft.Cleanup();
        }
    }

    private static void MoveExistingFrames(string retired, string building, StoredCharacter previous,
        IReadOnlySet<string> droppedReferences, IReadOnlyDictionary<string, string> relocatedReferences)
    {
        foreach (var (key, state) in previous.Actions)
        {
            MoveFrames(retired, building, state.Primary.Frames, key, droppedReferences, relocatedReferences);
            for (var index = 0; index < state.Extras.Length; index++)
                MoveFrames(retired, building, state.Extras[index].Frames, key, droppedReferences, relocatedReferences);
        }
    }

    private static void MoveFrames(string retired, string building, string[] frames, string key,
        IReadOnlySet<string> droppedReferences, IReadOnlyDictionary<string, string> relocatedReferences)
    {
        foreach (var reference in frames)
        {
            if (droppedReferences.Contains(reference)) continue;
            // Renumbering wins over the version one upgrade: a relocated reference already names its
            // final folder, while an untouched version one reference gains the primary/ segment.
            var upgraded = relocatedReferences.TryGetValue(reference, out var relocated)
                ? relocated
                : UpgradeReference(reference, key);
            var source = CharacterFiles.Child(retired, reference);
            var destination = CharacterFiles.Child(building, upgraded);
            if (!File.Exists(source)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: false);
        }
    }

    private static string UpgradeReference(string reference, string key)
    {
        var prefix = $"frames/{key}/";
        return reference.StartsWith(prefix, StringComparison.Ordinal) &&
            !reference[prefix.Length..].Contains('/')
            ? $"{prefix}primary/{reference[prefix.Length..]}"
            : reference;
    }

    /// <summary>Renames the staged frames to the reference layout the parser accepts for that folder.</summary>
    private static void InstallDraftFrames(CharacterActionDraft draft)
    {
        var expected = CharacterFiles.Child(draft.DirectoryPath, $"frames/{draft.StateKey}/{draft.Folder}");
        Directory.CreateDirectory(expected);
        var actual = draft.Clip.Frames;
        for (var index = 0; index < actual.Length; index++)
        {
            var reference = $"frames/{draft.StateKey}/{draft.Folder}/{index:D4}.png";
            if (reference == actual[index]) continue;
            File.Move(CharacterFiles.Child(draft.DirectoryPath, actual[index]),
                CharacterFiles.Child(draft.DirectoryPath, reference));
        }
        draft.Clip = draft.Clip with
        {
            Frames = Enumerable.Range(0, actual.Length)
                .Select(index => $"frames/{draft.StateKey}/{draft.Folder}/{index:D4}.png").ToArray(),
        };
    }

    /// <summary>Reads the canvas of the character's first stored frame, so a new action never resizes it.</summary>
    private static int CanvasSide(string directory, StoredCharacter document)
    {
        foreach (var state in document.Actions.Values)
        {
            foreach (var reference in state.Primary.Frames.Concat(state.Extras.SelectMany(extra => extra.Frames)))
                return ReadPngSide(CharacterFiles.Child(directory, reference));
        }
        return 512;
    }

    private static int ReadPngSide(string path)
    {
        var bytes = CharacterFiles.Read(path, 2 * 1024 * 1024);
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw CharacterManifest.Invalid();
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width != height || width is < 1 or > 512) throw CharacterManifest.Invalid();
        return width;
    }

    internal CharacterInfo Commit(CharacterDraft draft, string name, PetStatusAnchor anchor, double baseline)
    {
        using var gate = Lock();
        if (draft.Committed || draft.DirectoryPath != CharacterFiles.Child(StagingPath, Id(draft.Id)) ||
            !anchor.IsWithinArtboard || !double.IsFinite(baseline) || baseline is < 0 or > 1) throw CharacterManifest.Invalid();
        var document = draft.Document with { Name = CharacterManifest.ValidateName(name), StatusAnchor = anchor, Baseline = baseline };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        CharacterAssetSource.ParseStored(Encoding.UTF8.GetString(bytes));
        if (Directory.EnumerateDirectories(LibraryPath).Take(50).Count() >= 50 || CharacterFiles.Size(root) + bytes.Length > LibraryLimit)
            throw CharacterManifest.Invalid();
        CharacterFiles.WriteNew(CharacterFiles.Child(draft.DirectoryPath, "character.json"), bytes);
        CharacterFiles.Size(draft.DirectoryPath);
        Directory.Move(CharacterFiles.CheckedPath(draft.DirectoryPath), CharacterFiles.Child(LibraryPath, draft.Id));
        draft.Committed = true;
        return new(draft.Id, document.Name);
    }

    internal void Delete(string id)
    {
        using var gate = Lock();
        Id(id);
        if (SelectedId == id) WriteSelection(null);
        CharacterFiles.DeleteTree(LibraryPath, id);
    }
}
