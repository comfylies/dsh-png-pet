using System.IO;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterActionDraftTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dsh-action-draft-" + Guid.NewGuid().ToString("N"));

    private (CharacterLibrary Library, string Id) CreateCharacter()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "source.gif");
        File.WriteAllBytes(input, CharacterLibraryTests.TinyGif());
        var library = new CharacterLibrary(Path.Combine(root, "output"));
        using var draft = library.PrepareImage(input, CancellationToken.None);
        var info = library.Commit(draft, "维维美", new(.5, .1), .95);
        return (library, info.Id);
    }

    private string CharacterDirectory(string id) => Path.Combine(root, "output", "library", id);

    /// <summary>Every stored file and byte, so a failure can be proved to leave the character untouched.</summary>
    private static Dictionary<string, byte[]> Snapshot(string directory) => Directory
        .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);

    [Fact]
    public void Adding_an_extra_keeps_the_primary_action_and_writes_the_extra_folder()
    {
        var (library, id) = CreateCharacter();
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());

        var info = library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);

        var program = library.Load(info.Id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Single(program.Loop);
        Assert.Single(program.Extras);
        Assert.Equal("伸懒腰", program.Extras[0].Label);
        Assert.Equal(1500, program.Extras[0].FrameDurationsMs[0]);
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "extra-0", "0000.png")));
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "primary", "0000.png")));
        Assert.Contains("\"libraryFormatVersion\":2",
            File.ReadAllText(Path.Combine(CharacterDirectory(id), "character.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_extra_upgrades_a_version_one_library_in_place()
    {
        var (library, id) = CreateCharacter();
        var directory = CharacterDirectory(id);
        // Emulate an installed v0.2.11 entry: flat frames plus a version one manifest.
        Directory.CreateDirectory(Path.Combine(directory, "frames", "idle"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory, "frames", "idle", "primary")))
            File.Move(file, Path.Combine(directory, "frames", "idle", Path.GetFileName(file)));
        Directory.Delete(Path.Combine(directory, "frames", "idle", "primary"));
        File.WriteAllText(Path.Combine(directory, "character.json"),
            """{"libraryFormatVersion":1,"name":"维维美","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,"actions":{"idle":{"frames":["frames/idle/0000.png"],"durations":[100]}}}""");
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());

        library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(directory, "frames", "idle", "0000.png")));
        Assert.True(File.Exists(Path.Combine(directory, "frames", "idle", "primary", "0000.png")));
        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal("frames/idle/primary/0000.png", program.Loop[0].Frames[0]);
        Assert.Single(program.Extras);
    }

    [Fact]
    public void A_failed_commit_leaves_the_existing_character_untouched()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());
        library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);
        var directory = CharacterDirectory(id);
        var before = Snapshot(directory);

        // A duplicate extra name is rejected while committing.
        Assert.Throws<FormatException>(() =>
            library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None));

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Single(program.Extras);
        var after = Snapshot(directory);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "output", "staging")));
    }

    [Fact]
    public void Adding_an_extra_never_upscales_beyond_the_existing_canvas()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "small.png"), CharacterLibraryTests.TinyPng());
        library.AddExtra(id, Path.Combine(root, "small.png"), "idle", "小动作", new(.5, .1), .95, CancellationToken.None);

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal(1, GifFrameImporterTests.PngSide(
            Path.Combine(CharacterDirectory(id), program.Extras[0].Frames[0])));
        Assert.Equal(1, GifFrameImporterTests.PngSide(
            Path.Combine(CharacterDirectory(id), program.Loop[0].Frames[0])));
    }

    [Fact]
    public void Replacing_the_primary_action_swaps_the_frames_of_that_state()
    {
        var (library, id) = CreateCharacter();
        var replacement = Path.Combine(root, "replacement.gif");
        File.WriteAllBytes(replacement, GifFrameImporterTests.Gif(1));

        library.ReplacePrimary(id, replacement, "idle", new(.5, .1), .95, CancellationToken.None);

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal(new[] { 40, 250, 100 }, program.Loop[0].FrameDurationsMs);
        Assert.Empty(program.Extras);
        var primaryFolder = Path.Combine(CharacterDirectory(id), "frames", "idle", "primary");
        Assert.Equal(3, Directory.EnumerateFiles(primaryFolder, "*.png").Count());
    }

    [Fact]
    public void Removing_an_extra_renumbers_the_remaining_ones()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "a.png"), CharacterLibraryTests.TinyPng());
        library.AddExtra(id, Path.Combine(root, "a.png"), "idle", "第一个", new(.5, .1), .95, CancellationToken.None);
        library.AddExtra(id, Path.Combine(root, "a.png"), "idle", "第二个", new(.5, .1), .95, CancellationToken.None);
        Assert.Equal(2, library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true).Extras.Length);

        library.RemoveExtra(id, "idle", "第一个");

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Single(program.Extras);
        Assert.Equal("第二个", program.Extras[0].Label);
        Assert.StartsWith("frames/idle/extra-0/", program.Extras[0].Frames[0], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "extra-0", "0000.png")));
        Assert.False(Directory.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "extra-1")));
    }

    [Fact]
    public void Removing_an_unknown_extra_is_rejected()
    {
        var (library, id) = CreateCharacter();
        var directory = CharacterDirectory(id);
        var before = Snapshot(directory);

        Assert.Throws<FormatException>(() => library.RemoveExtra(id, "idle", "不存在"));

        // A rejected removal still has to leave the stored character byte for byte as it was.
        var after = Snapshot(directory);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "output", "staging")));
    }

    [Fact]
    public void Recovers_a_character_stranded_in_staging_by_an_interrupted_commit()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());
        library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);
        var staging = Path.Combine(root, "output", "staging");
        // The state a hard kill between the two directory moves of a commit leaves behind: the
        // library entry is gone and every byte of the character sits in a retired staging tree.
        Directory.Move(CharacterDirectory(id), Path.Combine(staging, $"retired-{id}-interrupted"));

        // While a commit still holds the library lock it owns staging, so a read that runs into one
        // reports the character as missing instead of taking the tree away from the live commit.
        using (new FileStream(Path.Combine(root, "output", "library.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(library.List());
        }

        var restored = Assert.Single(library.List());
        Assert.Equal(id, restored.Id);
        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal("伸懒腰", program.Extras[0].Label);
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "character.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
    }

    [Fact]
    public void Never_adopts_a_stranded_copy_over_an_existing_character()
    {
        var (library, id) = CreateCharacter();
        var directory = CharacterDirectory(id);
        var before = Snapshot(directory);
        var stranded = Path.Combine(root, "output", "staging", $"retired-{id}-older");
        Directory.CreateDirectory(stranded);
        File.WriteAllText(Path.Combine(stranded, "character.json"),
            """{"libraryFormatVersion":1,"name":"旧副本","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,"actions":{"idle":{"frames":["frames/idle/0000.png"],"durations":[100]}}}""");

        Assert.Equal("维维美", Assert.Single(library.List()).Name);
        Assert.Equal("维维美", library.Load(id).Document.Name);

        var after = Snapshot(directory);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        // A reader never deletes library bytes: the copy stays where the crash left it.
        Assert.True(Directory.Exists(stranded));
    }

    [Fact]
    public void Adding_an_extra_never_renames_the_character()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());

        library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);

        // The name of an added action belongs to the action, not to the character it was added to.
        Assert.Equal("维维美", library.Load(id).Document.Name);
        Assert.Equal("维维美", Assert.Single(library.List()).Name);
        Assert.Equal("伸懒腰", library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true).Extras[0].Label);
    }

    [Fact]
    public void Rejects_an_action_draft_whose_extra_folder_became_stale()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "a.png"), CharacterLibraryTests.TinyPng());
        // Two drafts prepared from the same document both claim the next free extra folder.
        using var first = library.PrepareAction(id, Path.Combine(root, "a.png"), "idle", asPrimary: false, CancellationToken.None);
        var stale = library.PrepareAction(id, Path.Combine(root, "a.png"), "idle", asPrimary: false, CancellationToken.None);
        library.CommitAction(first, "第一个", new(.5, .1), .95);
        var before = Snapshot(CharacterDirectory(id));

        Assert.Throws<FormatException>(() => library.CommitAction(stale, "第二个", new(.5, .1), .95));

        // Refused before any directory move: the stored character is byte for byte as it was.
        var after = Snapshot(CharacterDirectory(id));
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal("第一个", Assert.Single(program.Extras).Label);
        // The refused draft still owns its staging tree until the caller releases it.
        stale.Dispose();
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "output", "staging")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
