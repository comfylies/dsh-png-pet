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
    public void Uses_the_static_placeholder_without_animation_when_the_manifest_is_missing()
    {
        AssertStaticFallback(static () => null);
    }

    [Fact]
    public void Uses_the_static_placeholder_without_animation_when_the_manifest_is_malformed()
    {
        AssertStaticFallback(static () => new MemoryStream(Encoding.UTF8.GetBytes("{")));
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
}
