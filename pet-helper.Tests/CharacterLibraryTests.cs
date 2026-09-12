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
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("Version\":1", "Version\":3")));
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
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("frames/idle/primary/0000.png", "../../outside.png"));
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

    [Fact]
    public void Imports_a_version_two_character_with_extras_into_the_version_two_layout()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "extrasCooldownMs":45000,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png","frameDurationMs":1500}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        using (var draft = library.PrepareDirectory(root, CancellationToken.None))
        {
            var info = library.Commit(draft, "多动作", new(.5, .1), .95);
            var source = library.Load(info.Id);
            var program = source.ResolveProgram(PetAnimationKey.Idle, _ => true);

            Assert.Single(program.Loop);
            Assert.Single(program.Extras);
            Assert.Equal(45000, program.ExtrasCooldownMs);
            Assert.Equal("frames/idle/extra-0/0000.png", program.Extras[0].Frames[0]);
            Assert.Equal(1500, program.Extras[0].FrameDurationsMs[0]);
            Assert.StartsWith("frames/idle/primary/", program.Loop[0].Frames[0], StringComparison.Ordinal);
            var manifestPath = Path.Combine(root, "output", "library", info.Id, "character.json");
            var manifest = File.ReadAllText(manifestPath);
            Assert.Contains("\"libraryFormatVersion\":2", manifest, StringComparison.Ordinal);
            Assert.Contains("\"extrasCooldownMs\":45000", manifest, StringComparison.Ordinal);
            Assert.Contains("伸懒腰", manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Imports_extras_as_one_shot_clips_that_return_to_the_looping_primary()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch-a.png"), TinyPng());
        File.WriteAllBytes(Path.Combine(root, "stretch-b.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "extrasCooldownMs":5000,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png-sequence","frames":["stretch-a.png","stretch-b.png"],"frameDurationMs":100}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        using (var draft = library.PrepareDirectory(root, CancellationToken.None))
        {
            var info = library.Commit(draft, "多动作", new(.5, .1), .95);
            var source = library.Load(info.Id);
            var program = source.ResolveProgram(PetAnimationKey.Idle, _ => true);

            Assert.Equal(PetClipPlaybackMode.Loop, program.Loop[0].Playback);
            Assert.Equal(PetClipPlaybackMode.Once, program.Extras[0].Playback);
            Assert.Equal(new[] { "frames/idle/extra-0/0000.png", "frames/idle/extra-0/0001.png" }, program.Extras[0].Frames);
            Assert.Equal(new[] { 100, 100 }, program.Extras[0].FrameDurationsMs);

            // The coordinator plays an extra until the clip completes and then restarts the primary,
            // which a looping extra never does: a multi-frame loop animates forever and a single-frame
            // one is not even animating.  The primary is a single static frame, so the five seconds of
            // cooldown are measured by the one second heartbeat.
            var coordinator = new PetStateAnimationCoordinator(source.ResolveProgram, _ => true, _ => 0);
            coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
            Assert.Equal("frames/idle/primary/0000.png", coordinator.Frame);

            for (var tick = 0; tick < 5; tick++) coordinator.Advance();
            Assert.Equal("frames/idle/extra-0/0000.png", coordinator.Frame);

            coordinator.Advance();
            Assert.Equal("frames/idle/extra-0/0001.png", coordinator.Frame);

            coordinator.Advance();
            Assert.Equal("frames/idle/primary/0000.png", coordinator.Frame);
        }
    }

    [Fact]
    public void Rejects_a_single_frame_png_extra_without_a_declared_duration()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png"}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        Assert.Throws<FormatException>(() => library.PrepareDirectory(root, CancellationToken.None));
    }

    [Fact]
    public void Keeps_the_declared_duration_of_a_single_frame_gif_extra()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.gif"), TinyGif());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"gif","file":"stretch.gif","frameDurationMs":1500}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        using var draft = library.PrepareDirectory(root, CancellationToken.None);
        var info = library.Commit(draft, "多动作", new(.5, .1), .95);
        var program = library.Load(info.Id).ResolveProgram(PetAnimationKey.Idle, _ => true);

        // The single frame of the extra is a GIF frame whose own delay is the importer's 100 ms
        // default; the declared 1500 ms must win, or the extra would flash past.
        Assert.Single(program.Extras);
        Assert.Single(program.Extras[0].Frames);
        Assert.Equal(1500, program.Extras[0].FrameDurationsMs[0]);
        Assert.Equal(1500, program.Extras[0].FrameDurationMs);
    }

    [Fact]
    public void Rejects_a_source_character_with_five_extras()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"动作一","type":"png","file":"stretch.png","frameDurationMs":1500},
                         {"name":"动作二","type":"png","file":"stretch.png","frameDurationMs":1500},
                         {"name":"动作三","type":"png","file":"stretch.png","frameDurationMs":1500},
                         {"name":"动作四","type":"png","file":"stretch.png","frameDurationMs":1500},
                         {"name":"动作五","type":"png","file":"stretch.png","frameDurationMs":1500}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        Assert.Throws<FormatException>(() => library.PrepareDirectory(root, CancellationToken.None));
        Assert.Empty(library.List());
    }

    [Fact]
    public void Rejects_a_tampered_extra_frame_reference()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.gif"); File.WriteAllBytes(input, TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"input.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png","frameDurationMs":1500}]}}}
            """);
        var location = Path.Combine(root, "output");
        var library = new CharacterLibrary(location);
        var info = library.Commit(library.PrepareDirectory(root, CancellationToken.None), "多动作", new(.5, .1), .95);
        var manifest = Path.Combine(location, "library", info.Id, "character.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest)
            .Replace("frames/idle/extra-0/0000.png", "frames/idle/extra-1/0000.png"));

        Assert.Throws<FormatException>(() => library.Load(info.Id));
    }

    [Fact]
    public void Rejects_an_unknown_library_format_version()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.gif"); File.WriteAllBytes(input, TinyGif());
        var location = Path.Combine(root, "output");
        var library = new CharacterLibrary(location);
        var info = library.Commit(library.PrepareImage(input, CancellationToken.None), "test", new(.5, .1), .95);
        var manifest = Path.Combine(location, "library", info.Id, "character.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("libraryFormatVersion\":2", "libraryFormatVersion\":3"));

        Assert.Throws<FormatException>(() => library.Load(info.Id));
    }

    [Fact]
    public void Rejects_duplicate_extra_names_in_one_state()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "a.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"a.png","frameDurationMs":1000},
                         {"name":"伸懒腰","type":"png","file":"a.png","frameDurationMs":1000}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        Assert.Throws<FormatException>(() => library.PrepareDirectory(root, CancellationToken.None));
    }

    [Fact]
    public void Rejects_an_action_object_that_mixes_primary_and_type_forms()
    {
        var json = """
            {"characterFormatVersion":2,"name":"test","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{"type":"gif","file":"idle.gif","primary":{"type":"gif","file":"idle.gif"}}}}
            """;

        Assert.Throws<FormatException>(() => CharacterManifest.Parse(json));
    }

    internal static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
