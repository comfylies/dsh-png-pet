using PetHelper;
using System.IO;
using System.Text;
using System.Windows.Media;
using Xunit;
using WpfImage = System.Windows.Controls.Image;

namespace PetHelper.Tests;

public sealed class PetAnimationPlayerTests
{
    [Fact]
    public void Starting_a_large_clip_decodes_only_its_visible_first_frame()
    {
        RunOnSta(() =>
        {
            var image = new WpfImage();
            var player = new PetAnimationPlayer(image);

            player.Apply(PetAnimationKey.Responding, reducedMotion: false);

            Assert.NotNull(image.Source);
            Assert.Equal(1, player.CachedFrameCount);
            Assert.True(player.IsTimerRunning);
            player.Stop();
        });
    }

    [Fact]
    public void Uses_the_static_placeholder_without_animation_when_the_manifest_is_missing()
    {
        AssertStaticFallback(static () => null);
    }

    [Fact]
    public void Uses_the_static_placeholder_without_animation_when_the_manifest_is_malformed()
    {
        AssertStaticFallback(static () => new MemoryStream(Encoding.UTF8.GetBytes("{")));
    }

    [Fact]
    public void Uses_the_static_placeholder_without_animation_when_the_manifest_reader_throws()
    {
        RunOnSta(() =>
        {
            var image = new WpfImage();
            var staticPlaceholder = new DrawingImage();
            var player = new PetAnimationPlayer(
                image,
                static () => new MemoryStream(Encoding.UTF8.GetBytes("{}")),
                static _ => new ThrowingManifestReader(),
                () => staticPlaceholder);

            Assert.Same(staticPlaceholder, image.Source);
            Assert.False(player.IsTimerRunning);
            player.Stop();
        });
    }

    [Fact]
    public void Uses_the_static_placeholder_without_animation_when_the_idle_frame_is_unavailable()
    {
        RunOnSta(() =>
        {
            var image = new WpfImage();
            var staticPlaceholder = new DrawingImage();
            var player = new PetAnimationPlayer(
                image,
                static () => new MemoryStream(Encoding.UTF8.GetBytes("""
                    { "idle": { "frames": ["Animations/missing/001.png"] } }
                    """)),
                () => staticPlaceholder);

            player.Apply(PetAnimationKey.Idle, reducedMotion: false);

            Assert.Same(staticPlaceholder, image.Source);
            Assert.False(player.IsTimerRunning);
            player.Stop();
        });
    }

    [Fact]
    public void Pause_and_resume_are_safe_without_active_playback()
    {
        RunOnSta(() =>
        {
            var image = new WpfImage();
            var player = new PetAnimationPlayer(
                image,
                static () => new MemoryStream(Encoding.UTF8.GetBytes("""
                    { "idle": { "frames": ["Animations/missing/001.png", "Animations/missing/002.png"], "intervalMs": 1000 } }
                    """)),
                static () => null);

            // Frames are unavailable in the test environment, so playback stays static; both
            // calls must be safe no-ops and must not start the timer.
            player.Apply(PetAnimationKey.Idle, reducedMotion: false);
            Assert.False(player.IsTimerRunning);

            player.Pause();
            Assert.False(player.IsTimerRunning);
            player.Resume();
            Assert.False(player.IsTimerRunning);

            player.Stop();
        });
    }

    private static void AssertStaticFallback(Func<Stream?> manifestStreamReader)
    {
        RunOnSta(() =>
        {
            var image = new WpfImage();
            var staticPlaceholder = new DrawingImage();
            var staticPlaceholderLoads = 0;
            var player = new PetAnimationPlayer(
                image,
                manifestStreamReader,
                () =>
                {
                    staticPlaceholderLoads++;
                    return staticPlaceholder;
                });

            Assert.Same(staticPlaceholder, image.Source);
            Assert.Equal(1, staticPlaceholderLoads);
            player.Apply(PetAnimationKey.Working, reducedMotion: false);
            Assert.False(player.IsTimerRunning);
            player.Stop();
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private sealed class ThrowingManifestReader : TextReader
    {
        public override string ReadToEnd() =>
            throw new IOException("The injected manifest reader failed.");
    }
}
