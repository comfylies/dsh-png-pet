using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows.Media.Imaging;

namespace PetHelper;

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

    private FileStream Lock()
    {
        CharacterFiles.CheckedPath(root);
        Directory.CreateDirectory(root);
        return CharacterFiles.Open(CharacterFiles.Child(root, "library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static string Id(string value)
    {
        if (value.Length != 32 || value.Any(c => !char.IsAsciiHexDigit(c)) || value != value.ToLowerInvariant())
            throw CharacterManifest.Invalid();
        return value;
    }

    internal IReadOnlyList<CharacterInfo> List()
    {
        var result = new List<CharacterInfo>();
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
        // A one-frame extra would flash past at the importer's 100 ms default, so it must declare its
        // own display duration; GIF extras that decode to a single frame are rejected here as well.
        if (isExtra && references.Count == 1 && !action.DurationDeclared) throw CharacterManifest.Invalid();
        return new(references.ToArray(), delays.ToArray());
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
