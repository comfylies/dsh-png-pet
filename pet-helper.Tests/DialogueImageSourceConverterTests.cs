using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class DialogueImageSourceConverterTests
{
    [Fact]
    public void Downsamples_live_image_thumbnails_to_the_bounded_decode_size()
    {
        RunOnSta(() =>
        {
            var source = new WriteableBitmap(1024, 4, 96, 96, PixelFormats.Bgra32, null);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);

            var converter = new DialogueImageSourceConverter();
            var image = Assert.IsType<BitmapImage>(converter.Convert(
                new DialogueImage("wide.png", null, null, Convert.ToBase64String(stream.ToArray())),
                typeof(ImageSource),
                null!,
                System.Globalization.CultureInfo.InvariantCulture));

            Assert.True(image.PixelWidth <= 512);
            Assert.Equal(512, image.PixelWidth);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
