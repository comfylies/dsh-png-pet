using System.IO;
using System.Text;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dsh-character-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Imports_a_copy_and_restores_selection_without_retaining_source_names()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "private-source-name.gif");
        File.WriteAllBytes(input, TinyGif());
        var library = new CharacterLibrary(Path.Combine(root, "library-root"));
        using var draft = library.PrepareImage(input, CancellationToken.None);
        var character = library.Commit(draft, "小狐狸", new(0.5, 0.1), 0.95);
        library.Select(character.Id);
        File.Delete(input);
        var restored = new CharacterLibrary(Path.Combine(root, "library-root"));
        Assert.Equal(character.Id, restored.SelectedId);
        var source = restored.Load(character.Id);
        var idle = source.ResolveProgram(PetAnimationKey.Idle, _ => true);
        var working = source.ResolveProgram(PetAnimationKey.Working, _ => true);
        Assert.Equal(idle, working);
        Assert.NotEmpty(source.ReadFrame(idle.Loop[0].Frames[0]));
        foreach (var json in Directory.EnumerateFiles(Path.Combine(root, "library-root"), "*.json", SearchOption.AllDirectories))
            Assert.DoesNotContain("private-source-name", File.ReadAllText(json));
        library.Delete(character.Id);
        Assert.Empty(library.List());
        Assert.Null(library.SelectedId);
    }

    [Theory]
    [InlineData("../secret.gif")]
    [InlineData("C:/secret.gif")]
    [InlineData("https://example.com/a.gif")]
    [InlineData("a.gif:stream")]
    [InlineData("CON.gif")]
    [InlineData("a\\b.gif")]
    public void Rejects_unsafe_asset_references(string file)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new {
            characterFormatVersion = 1, name = "test", statusAnchor = new { x = .5, y = .1 }, baseline = .95,
            actions = new { idle = new { type = "gif", file } }
        });
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(json));
    }

    [Fact]
    public void Rejects_unknown_fields_duplicate_keys_and_missing_idle()
    {
        var valid = """{"characterFormatVersion":1,"name":"test","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,"actions":{"idle":{"type":"gif","file":"idle.gif"}}}""";
        Assert.NotNull(CharacterManifest.Parse(valid));
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("\"name\":", "\"extra\":0,\"name\":")));
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("\"name\":", "\"name\":\"duplicate\",\"name\":")));
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("\"idle\"", "\"working\"")));
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("Version\":1", "Version\":2")));
    }

    [Fact]
    public void Cancellation_and_bad_images_never_commit_a_character()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "bad.gif");
        File.WriteAllText(input, "not a picture");
        var library = new CharacterLibrary(Path.Combine(root, "library-root"));
        Assert.ThrowsAny<Exception>(() => library.PrepareImage(input, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => library.PrepareImage(input, new CancellationToken(true)));
        Assert.Empty(library.List());
    }

    internal static byte[] TinyGif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    [Fact]
    public void Imports_a_multistate_directory_and_keeps_gif_delays()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        // Every action must have the same canvas.
        File.WriteAllBytes(Path.Combine(root, "work.gif"), TinyGif());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":1,"name":"multi","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{"type":"gif","file":"idle.gif"},"working":{"type":"gif","file":"work.gif"}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));
        using var draft = library.PrepareDirectory(root, CancellationToken.None);
        var info = library.Commit(draft, "多状态", new(.5, .1), .95);
        var source = library.Load(info.Id);
        Assert.Equal(PetAnimationKey.Working, source.ResolveProgram(PetAnimationKey.Working, _ => true).EffectiveKey);
        Assert.Equal(PetAnimationKey.Idle, source.ResolveProgram(PetAnimationKey.Error, _ => true).EffectiveKey);
        Assert.Equal(100, source.ResolveProgram(PetAnimationKey.Working, _ => true).Loop[0].FrameDurationsMs[0]);
        // An unrecognized directory file is never copied.
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(root, "output"), "*", SearchOption.AllDirectories),
            file => file.EndsWith("work.gif", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_tampered_stored_references_and_corrupt_selection()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.gif"); File.WriteAllBytes(input, TinyGif());
        var location = Path.Combine(root, "output");
        var library = new CharacterLibrary(location);
        using var draft = library.PrepareImage(input, CancellationToken.None);
        var info = library.Commit(draft, "test", new(.5, .1), .95);
        library.Select(info.Id);
        var manifest = Path.Combine(location, "library", info.Id, "character.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("frames/idle/0000.png", "../../outside.png"));
        Assert.Throws<FormatException>(() => library.Load(info.Id));
        Assert.Null(library.SelectedId);
        File.WriteAllText(Path.Combine(location, "selection.json"), "not json");
        Assert.Null(library.SelectedId);
    }

    [Fact]
    public void Normalized_user_assets_are_not_embedded_in_the_helper()
    {
        var assembly = typeof(PetAnimationPlayer).Assembly;
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), name => name.Contains("Characters", StringComparison.OrdinalIgnoreCase));
        using var resources = assembly.GetManifestResourceStream("pet-helper.g.resources");
        Assert.NotNull(resources);
        using var reader = new System.Resources.ResourceReader(resources!);
        foreach (System.Collections.DictionaryEntry item in reader)
        {
            var name = Assert.IsType<string>(item.Key);
            Assert.False(name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
            Assert.False(name.Contains("/characters/", StringComparison.OrdinalIgnoreCase));
            Assert.False(name.Contains("/library/", StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
