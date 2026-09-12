using System.IO;
using System.Windows.Controls;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterPlayerTests
{
    [Fact]
    public void External_frames_play_and_a_missing_uncached_frame_falls_back_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-character-player-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            PetAnimationPlayer? player = null;
            try
            {
                Directory.CreateDirectory(root);
                var input = Path.Combine(root, "input.gif");
                File.WriteAllBytes(input, GifFrameImporterTests.Gif(1));
                var library = new CharacterLibrary(Path.Combine(root, "library-root"));
                using var draft = library.PrepareImage(input, CancellationToken.None);
                var info = library.Commit(draft, "test", new(.5, .1), .95);
                var source = library.Load(info.Id);
                var firstFrame = source.ResolveProgram(PetAnimationKey.Idle, _ => true).Loop[0].Frames[0];
                Assert.NotNull(PetAnimationPlayer.LoadExternalBitmap(source.ReadFrame(firstFrame)));
                Assert.Equal(new[] {40, 250, 100}, source.ResolveProgram(PetAnimationKey.Idle, _ => true).Loop[0].FrameDurationsMs);
                var image = new Image();
                player = new PetAnimationPlayer(image, source);
                var failures = 0;
                player.AssetFailed += (_, _) => failures++;
                player.Apply(PetAnimationKey.Working, false);
                Assert.True(player.IsTimerRunning);
                Assert.NotNull(image.Source);
                var first = image.Source;
                player.AdvanceFrame();
                Assert.NotSame(first, image.Source);
                File.Delete(Path.Combine(root, "library-root", "library", info.Id, "frames", "idle", "0002.png"));
                player.AdvanceFrame();
                Assert.True(player.HasFailed);
                Assert.False(player.IsTimerRunning);
                Assert.NotNull(image.Source);
                player.AdvanceFrame();
                Assert.Equal(1, failures);
                player.Stop();
                Assert.Equal(0, player.CachedFrameCount);
            }
            catch (Exception exception) { failure = exception; }
            finally { player?.Stop(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
