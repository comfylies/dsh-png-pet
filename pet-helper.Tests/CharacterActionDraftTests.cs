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

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
