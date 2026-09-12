using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class GifFrameImporterTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void Composes_partial_transparent_frames_and_applies_disposal(int disposal, bool retainRed)
    {
        var frames = new List<BitmapSource>();
        var delays = new List<int>();
        GifFrameImporter.Decode(Gif(disposal), true, 100, new(), CancellationToken.None,
            (frame, delay) => { frames.Add(frame); delays.Add(delay); });
        Assert.Equal(new[] { 40, 250, 100 }, delays);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(frames[0], 0, 1));
        Assert.Equal(retainRed ? new byte[] { 0, 0, 255, 255 } : new byte[4], Pixel(frames[1], 0, 1));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(frames[1], 1, 1));
        // Third frame is transparent; disposal=2 on the blue frame clears that region only.
        Assert.Equal(new byte[4], Pixel(frames[2], 1, 1));
    }

    [Fact]
    public void Rejects_excessive_canvas_and_reserved_disposal_before_decoding()
    {
        var bytes = Gif(1);
        bytes[6] = 0xff; bytes[7] = 0x7f;
        Assert.Throws<FormatException>(() => GifFrameImporter.Decode(bytes, true, 100, new(), CancellationToken.None, (_, _) => { }));
        Assert.Throws<FormatException>(() => GifFrameImporter.Decode(Gif(4), true, 100, new(), CancellationToken.None, (_, _) => { }));
    }

    [Fact]
    public void Stops_at_cancellation_between_frames()
    {
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        Assert.Throws<OperationCanceledException>(() => GifFrameImporter.Decode(Gif(1), true, 100, new(), cancellation.Token,
            (_, _) => { count++; cancellation.Cancel(); }));
        Assert.Equal(1, count);
    }

    private static byte[] Pixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels.AsSpan((y * bitmap.PixelWidth + x) * 4, 4).ToArray();
    }

    internal static byte[] Gif(int disposal)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write("GIF89a"u8); writer.Write((ushort)2); writer.Write((ushort)1);
        writer.Write(new byte[] { 0x81, 0, 0, 0,0,0, 255,0,0, 0,0,255, 255,255,255 });
        void Frame(int left, int pixel, int delay, int method)
        {
            writer.Write(new byte[] { 0x21, 0xf9, 4, (byte)((method << 2) | 1) });
            writer.Write((ushort)delay); writer.Write(new byte[] { 0, 0, 0x2c });
            writer.Write((ushort)left); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write(new byte[] { 0, 2, 2, (byte)(4 | pixel << 3 | 5 << 6), 1, 0 });
        }
        Frame(0, 1, 4, disposal); Frame(1, 2, 25, 2); Frame(0, 0, 0, 1);
        writer.Write((byte)0x3b);
        return output.ToArray();
    }
}
