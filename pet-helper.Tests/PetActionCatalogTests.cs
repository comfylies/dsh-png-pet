using System.IO;
using System.Windows.Controls;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetActionCatalogTests
{
    [Fact]
    public void The_manifest_catalog_lists_the_primary_loop_before_the_extras()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, PetExtrasPlaybackTests.IdleState);

        var catalog = manifest.ResolveActions(PetAnimationKey.Idle, _ => true);

        Assert.Equal(new[] { "呼吸", "伸懒腰", "打哈欠" }, catalog.Select(choice => choice.Label));
        Assert.Equal(new[] { false, true, true }, catalog.Select(choice => choice.IsExtra));
    }

    [Fact]
    public void The_external_catalog_lists_the_primary_and_the_named_extras()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-action-catalog-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Image? image = null;
            PetAnimationPlayer? player = null;
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(Path.Combine(root, "idle.gif"), CharacterLibraryTests.TinyGif());
                File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());
                File.WriteAllText(Path.Combine(root, "character.json"), """
                    {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
                     "actions":{"idle":{
                       "primary":{"type":"gif","file":"idle.gif"},
                       "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png","frameDurationMs":1500}]}}}
                    """);
                var library = new CharacterLibrary(Path.Combine(root, "library-root"));
                using var draft = library.PrepareDirectory(root, CancellationToken.None);
                var info = library.Commit(draft, "多动作", new(.5, .1), .95);
                image = new Image();
                player = new PetAnimationPlayer(image, library.Load(info.Id), preview: true);

                var catalog = player.ActionCatalog(PetAnimationKey.Idle);
                Assert.Equal(new[] { "默认动作", "伸懒腰" }, catalog.Select(choice => choice.Label));
                Assert.Equal(new[] { false, true }, catalog.Select(choice => choice.IsExtra));

                player.PreviewAction(PetAnimationKey.Idle, 1, reducedMotion: false);
                Assert.True(player.IsTimerRunning);
                Assert.Equal("frames/idle/extra-0/0000.png", catalog[1].Clip.Frames[0]);

                // A one-frame preview restarts instead of finishing, so the window keeps showing it.
                player.AdvanceFrame();
                Assert.True(player.IsTimerRunning);
                Assert.NotNull(image.Source);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                player?.Stop();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
