using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetHelper;

internal sealed class CharacterImportBudget
{
    private long pixels;
    private long inputBytes;
    private int frames;
    internal int Width { get; private set; }
    internal int Height { get; private set; }

    internal void Add(int width, int height, int count, int bytes)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || count is < 1 or > 240) throw CharacterManifest.Invalid();
        if (Width != 0 && (Width != width || Height != height)) throw CharacterManifest.Invalid();
        Width = width; Height = height;
        frames += count;
        pixels += (long)width * height * count;
        inputBytes += bytes;
        if (frames > 1024 || pixels > 256_000_000 || inputBytes > 100L * 1024 * 1024) throw CharacterManifest.Invalid();
    }
}

internal static class GifFrameImporter
{
    internal static bool IsGif(byte[] bytes) => bytes.Length >= 6 &&
        (bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8));

    internal static void Decode(byte[] bytes, bool gif, int pngDelay, CharacterImportBudget budget,
        CancellationToken cancellation, Action<BitmapSource, int> accept)
    {
        cancellation.ThrowIfCancellationRequested();
        if (gif)
        {
            var layout = InspectGif(bytes);
            budget.Add(layout.Width, layout.Height, layout.Frames.Count, bytes.Length);
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            if (decoder.Frames.Count != layout.Frames.Count) throw CharacterManifest.Invalid();
            var canvas = new byte[checked(layout.Width * layout.Height * 4)];
            if (!layout.Frames[0].Transparent) Fill(canvas, layout.Width, 0, 0, layout.Width, layout.Height, layout.Background);
            for (var index = 0; index < layout.Frames.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var info = layout.Frames[index];
                var previous = info.Disposal == 3 ? (byte[])canvas.Clone() : null;
                var frame = decoder.Frames[index];
                if (frame.PixelWidth != info.Width || frame.PixelHeight != info.Height) throw CharacterManifest.Invalid();
                var rgba = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[info.Width * info.Height * 4];
                rgba.CopyPixels(pixels, info.Width * 4, 0);
                for (var y = 0; y < info.Height; y++)
                for (var x = 0; x < info.Width; x++)
                {
                    var from = (y * info.Width + x) * 4;
                    if (pixels[from + 3] == 0) continue;
                    Buffer.BlockCopy(pixels, from, canvas, ((y + info.Top) * layout.Width + x + info.Left) * 4, 4);
                }
                accept(Normalize(canvas, layout.Width, layout.Height), info.Delay);
                if (info.Disposal == 2)
                    Fill(canvas, layout.Width, info.Left, info.Top, info.Width, info.Height,
                        info.Transparent ? new byte[4] : layout.Background);
                else if (previous is not null) canvas = previous;
            }
        }
        else
        {
            if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
                !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw CharacterManifest.Invalid();
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            budget.Add(width, height, 1, bytes.Length);
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != width || frame.PixelHeight != height) throw CharacterManifest.Invalid();
            var rgba = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[width * height * 4];
            rgba.CopyPixels(pixels, width * 4, 0);
            cancellation.ThrowIfCancellationRequested();
            accept(Normalize(pixels, width, height), pngDelay);
        }
    }

    private static BitmapSource Normalize(byte[] pixels, int width, int height)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var side = Math.Max(width, height);
        var factor = Math.Min(1d, 512d / side);
        BitmapSource scaled = factor < 1 ? new TransformedBitmap(source, new ScaleTransform(factor, factor)) : source;
        var outputSide = Math.Min(side, 512);
        var target = new byte[outputSide * outputSide * 4];
        var small = new byte[scaled.PixelWidth * scaled.PixelHeight * 4];
        scaled.CopyPixels(small, scaled.PixelWidth * 4, 0);
        var left = (outputSide - scaled.PixelWidth) / 2;
        var top = outputSide - scaled.PixelHeight;
        for (var y = 0; y < scaled.PixelHeight; y++)
            Buffer.BlockCopy(small, y * scaled.PixelWidth * 4, target, ((y + top) * outputSide + left) * 4, scaled.PixelWidth * 4);
        var result = BitmapSource.Create(outputSide, outputSide, 96, 96, PixelFormats.Bgra32, null, target, outputSide * 4);
        result.Freeze();
        return result;
    }

    private static void Fill(byte[] pixels, int strideWidth, int left, int top, int width, int height, byte[] color)
    {
        for (var y = top; y < top + height; y++)
        for (var x = left; x < left + width; x++)
            Buffer.BlockCopy(color, 0, pixels, (y * strideWidth + x) * 4, 4);
    }

    private sealed record FrameInfo(int Left, int Top, int Width, int Height, int Delay, int Disposal, bool Transparent);
    private sealed record GifLayout(int Width, int Height, byte[] Background, List<FrameInfo> Frames);

    // Inspect the bounded byte snapshot before asking the native decoder to allocate frames.
    private static GifLayout InspectGif(byte[] bytes)
    {
        if (!IsGif(bytes) || bytes.Length < 14) throw CharacterManifest.Invalid();
        var offset = 6;
        int Byte() { if (offset >= bytes.Length) throw CharacterManifest.Invalid(); return bytes[offset++]; }
        int Word() { var lo = Byte(); return lo | Byte() << 8; }
        void Skip(int count) { if (count < 0 || offset > bytes.Length - count) throw CharacterManifest.Invalid(); offset += count; }
        void Blocks() { int size; while ((size = Byte()) != 0) Skip(size); }
        var width = Word(); var height = Word();
        if (width is < 1 or > 2048 || height is < 1 or > 2048) throw CharacterManifest.Invalid();
        var packed = Byte(); var backgroundIndex = Byte(); Byte();
        var background = new byte[4];
        if ((packed & 128) != 0)
        {
            var count = 1 << ((packed & 7) + 1);
            if (backgroundIndex >= count) throw CharacterManifest.Invalid();
            var palette = offset;
            Skip(count * 3);
            background = [bytes[palette + backgroundIndex * 3 + 2], bytes[palette + backgroundIndex * 3 + 1],
                bytes[palette + backgroundIndex * 3], 255];
        }
        var frames = new List<FrameInfo>();
        var delay = 100; var disposal = 0; var transparent = false;
        while (true)
        {
            var marker = Byte();
            if (marker == 0x3b) break;
            if (marker == 0x21)
            {
                var label = Byte();
                if (label == 0xf9)
                {
                    if (Byte() != 4) throw CharacterManifest.Invalid();
                    var control = Byte();
                    disposal = (control >> 2) & 7;
                    if (disposal > 3 || (control & 0xe2) != 0) throw CharacterManifest.Invalid();
                    transparent = (control & 1) != 0;
                    var rawDelay = Word();
                    if (rawDelay > 1000) throw CharacterManifest.Invalid();
                    delay = rawDelay == 0 ? 100 : Math.Max(20, rawDelay * 10);
                    Byte();
                    if (Byte() != 0) throw CharacterManifest.Invalid();
                }
                else if (label is 0xfe or 0xff) Blocks();
                else throw CharacterManifest.Invalid();
            }
            else if (marker == 0x2c)
            {
                var left = Word(); var top = Word(); var fw = Word(); var fh = Word();
                if (fw < 1 || fh < 1 || left + fw > width || top + fh > height || frames.Count >= 240)
                    throw CharacterManifest.Invalid();
                var flags = Byte();
                if ((flags & 128) != 0) Skip(3 * (1 << ((flags & 7) + 1)));
                var codeSize = Byte();
                if (codeSize is < 2 or > 8) throw CharacterManifest.Invalid();
                Blocks();
                frames.Add(new(left, top, fw, fh, delay, disposal, transparent));
                delay = 100; disposal = 0; transparent = false;
            }
            else throw CharacterManifest.Invalid();
        }
        if (frames.Count == 0 || offset != bytes.Length) throw CharacterManifest.Invalid();
        return new(width, height, background, frames);
    }
}
