using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfImage = System.Windows.Controls.Image;

namespace PetHelper;

public sealed class PetAnimationPlayer
{
    private const string ManifestResourceName = "PetHelper.Assets.pet-animations.json";
    private const string ManifestUnavailableMessage = "The pet animation manifest is unavailable.";
    // Hold the entire 49-frame responding loop: a smaller LRU would evict every frame
    // before its next lap. At 512px square RGBA this caps decoded pixels near 49 MiB,
    // before WPF compositor copies, without retaining every state the pet has visited.
    private const int MaximumCachedFrames = 49;
    private const int AnimationDecodePixelWidth = 512;

    private WpfImage image;
    private readonly CharacterAssetSource? characterSource;
    private readonly Func<ImageSource?> staticPlaceholderLoader;
    private PetStateAnimationCoordinator? playback;
    private readonly DispatcherTimer timer;
    private readonly BoundedLruCache<string, BitmapImage> imagesByFrame;
    private readonly HashSet<string> availableFrames = new(StringComparer.Ordinal);
    private readonly HashSet<string> unavailableFrames = new(StringComparer.Ordinal);

    public event EventHandler? Completed;
    internal event EventHandler? AssetFailed;
    internal bool HasFailed { get; private set; }

    public PetAnimationPlayer(WpfImage image)
        : this(
            image,
            () => typeof(PetAnimationPlayer).Assembly.GetManifestResourceStream(ManifestResourceName),
            LoadStaticPlaceholder)
    {
    }

    internal PetAnimationPlayer(
        WpfImage image,
        Func<Stream?> manifestStreamReader,
        Func<ImageSource?> staticPlaceholderLoader)
        : this(image, manifestStreamReader, static stream => new StreamReader(stream), staticPlaceholderLoader)
    {
    }

    internal PetAnimationPlayer(
        WpfImage image,
        Func<Stream?> manifestStreamReader,
        Func<Stream, TextReader> manifestReaderFactory,
        Func<ImageSource?> staticPlaceholderLoader,
        CharacterAssetSource? characterSource = null,
        int cacheFrames = MaximumCachedFrames)
    {
        this.image = image ?? throw new ArgumentNullException(nameof(image));
        this.characterSource = characterSource;
        imagesByFrame = new(cacheFrames, cacheFrames * 1024L * 1024,
            bitmap => (long)bitmap.PixelWidth * bitmap.PixelHeight * Math.Max(4, (bitmap.Format.BitsPerPixel + 7) / 8));
        this.staticPlaceholderLoader = staticPlaceholderLoader
            ?? throw new ArgumentNullException(nameof(staticPlaceholderLoader));
        // State updates from a streaming reply arrive at normal dispatcher priority.  Render
        // priority keeps the fixed-rate frame timer from being starved by that input burst.
        timer = new DispatcherTimer(DispatcherPriority.Render);
        timer.Tick += Timer_Tick;

        try
        {
            playback = characterSource is null
                ? new PetStateAnimationCoordinator(LoadManifest(manifestStreamReader, manifestReaderFactory), IsFrameAvailable)
                : new PetStateAnimationCoordinator(characterSource.ResolveProgram, IsFrameAvailable);
            playback.Completed += Playback_Completed;
        }
        catch (InvalidOperationException)
        {
            ActivateStaticFallback();
        }
    }

    internal PetAnimationPlayer(WpfImage image, CharacterAssetSource? characterSource, bool preview = false)
        : this(image, () => typeof(PetAnimationPlayer).Assembly.GetManifestResourceStream(ManifestResourceName),
            static stream => new StreamReader(stream), LoadStaticPlaceholder, characterSource, preview ? 8 : MaximumCachedFrames) { }

    internal void AttachImage(WpfImage target)
    {
        var current = image.Source;
        image.Source = null;
        image = target;
        image.Source = current;
        ApplyRenderTransform();
    }

    public void Apply(PetAnimationKey key, bool reducedMotion)
    {
        if (playback is null)
        {
            timer.Stop();
            return;
        }

        try
        {
            playback.Apply(key, reducedMotion);
            UpdateImage();

            if (playback is { IsAnimating: true })
            {
                timer.Interval = TimeSpan.FromMilliseconds(playback.IntervalMs);
                timer.Start();
                return;
            }

            timer.Stop();
        }
        catch (Exception)
        {
            ActivateStaticFallback();
        }
    }

    public void Stop()
    {
        timer.Stop();
        timer.Tick -= Timer_Tick;
        if (playback is not null) playback.Completed -= Playback_Completed;
        playback = null;
        imagesByFrame.Clear();
        availableFrames.Clear();
        unavailableFrames.Clear();
    }

    /// <summary>
    /// Suspends frame playback without losing the current animation state, so dragging the
    /// pet does not compete with per-frame redraws of the transparent window.
    /// </summary>
    public void Pause()
    {
        timer.Stop();
    }

    /// <summary>Resumes frame playback if the current animation is still animating.</summary>
    public void Resume()
    {
        if (playback is not { IsAnimating: true }) return;
        timer.Interval = TimeSpan.FromMilliseconds(playback.IntervalMs);
        timer.Start();
    }

    internal bool IsTimerRunning => timer.IsEnabled;

    internal int CachedFrameCount => imagesByFrame.Count;

    public PetStatusAnchor StatusAnchor => playback?.StatusAnchor ?? PetStatusAnchor.Default;

    public PetRenderTransform RenderTransform => playback?.RenderTransform ?? PetRenderTransform.Identity;

    /// <summary>Reapplies normalized translation after the WPF image receives a new layout size.</summary>
    public void RefreshPresentation() => ApplyRenderTransform();

    private void Timer_Tick(object? sender, EventArgs e) => AdvanceFrame();

    internal void AdvanceFrame()
    {
        if (playback is null)
        {
            timer.Stop();
            return;
        }

        try
        {
            playback.Advance();
            UpdateImage();
            if (playback is null || !playback.IsAnimating) { timer.Stop(); return; }
            timer.Interval = TimeSpan.FromMilliseconds(playback.IntervalMs);
        }
        catch { ActivateStaticFallback(); }
    }

    private void Playback_Completed(object? sender, EventArgs e) => Completed?.Invoke(this, EventArgs.Empty);

    private void UpdateImage()
    {
        if (playback is null)
        {
            return;
        }

        var next = TryLoadFrame(playback.Frame);
        if (next is null) { ActivateStaticFallback(); return; }
        image.Source = next;
        ApplyRenderTransform();
    }

    private void ApplyRenderTransform()
    {
        var transform = playback?.RenderTransform ?? PetRenderTransform.Identity;
        var width = image.ActualWidth;
        var height = image.ActualHeight;
        var offsetX = width * (transform.Origin.X * (1d - transform.Scale) + transform.Offset.X);
        var offsetY = height * (transform.Origin.Y * (1d - transform.Scale) + transform.Offset.Y);
        image.RenderTransform = new MatrixTransform(new Matrix(
            transform.Scale, 0d, 0d, transform.Scale, offsetX, offsetY));
    }

    private bool IsFrameAvailable(string frame)
    {
        if (imagesByFrame.TryGetValue(frame, out _) || availableFrames.Contains(frame))
        {
            return true;
        }

        if (unavailableFrames.Contains(frame))
        {
            return false;
        }

        try
        {
            if (characterSource is not null) return characterSource.HasFrame(frame);
            // Resolve() checks every frame in a clip.  Decoding all 32/49 720px PNGs here
            // blocks the UI for the whole state transition.  Opening the controlled resource
            // is enough to establish availability; only the currently displayed frame is
            // decoded below.
            var resource = System.Windows.Application.GetResourceStream(FrameUri(frame));
            if (resource?.Stream is null) throw new IOException();
            using (resource.Stream) { }
            availableFrames.Add(frame);
            return true;
        }
        catch
        {
            unavailableFrames.Add(frame);
            return false;
        }
    }

    private ImageSource? TryLoadFrame(string frame)
    {
        if (imagesByFrame.TryGetValue(frame, out var bitmap)) return bitmap;
        try
        {
            bitmap = characterSource is null ? LoadBitmap(FrameUri(frame)) : LoadExternalBitmap(characterSource.ReadFrame(frame));
            imagesByFrame.AddOrUpdate(frame, bitmap);
            availableFrames.Add(frame);
            return bitmap;
        }
        catch
        {
            unavailableFrames.Add(frame);
            return null;
        }
    }

    private static Uri FrameUri(string frame) =>
        new($"pack://application:,,,/pet-helper;component/Assets/{frame}", UriKind.Absolute);

    private void ActivateStaticFallback()
    {
        timer.Stop();
        playback = null;
        ApplyRenderTransform();
        image.Source = TryLoadStaticPlaceholder(staticPlaceholderLoader);
        if (!HasFailed)
        {
            HasFailed = true;
            AssetFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static BitmapImage LoadExternalBitmap(byte[] bytes, int decodeWidth = 512)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw CharacterManifest.Invalid();
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is < 1 or > 512 || width != height) throw CharacterManifest.Invalid();
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        // Stream-backed bitmaps have no URI cache entry. IgnoreImageCache asks WPF to
        // remove a null URI and fails on .NET 10; OnLoad already detaches the stream.
        bitmap.DecodePixelWidth = Math.Min(width, decodeWidth);
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? TryLoadStaticPlaceholder(Func<ImageSource?> staticPlaceholderLoader)
    {
        try
        {
            return staticPlaceholderLoader();
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? LoadStaticPlaceholder()
    {
        try
        {
            return LoadBitmap(FrameUri("placeholder-a.png"));
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage LoadBitmap(Uri uri)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        // The LRU cache is the sole owner of decoded animation frames; do not let WPF retain
        // an unbounded second cache keyed by the embedded pack URI after a frame is evicted.
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.DecodePixelWidth = AnimationDecodePixelWidth;
        bitmap.UriSource = uri;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static PetAnimationManifest LoadManifest(
        Func<Stream?> manifestStreamReader,
        Func<Stream, TextReader> manifestReaderFactory)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(manifestStreamReader);
            ArgumentNullException.ThrowIfNull(manifestReaderFactory);
            using var stream = manifestStreamReader()
                ?? throw new InvalidOperationException(ManifestUnavailableMessage);
            using var reader = manifestReaderFactory(stream)
                ?? throw new InvalidOperationException(ManifestUnavailableMessage);
            return PetAnimationManifest.Parse(reader.ReadToEnd(), ReadEmbeddedActionManifest);
        }
        catch (Exception)
        {
            throw new InvalidOperationException(ManifestUnavailableMessage);
        }
    }

    private static string ReadEmbeddedActionManifest(string identifier)
    {
        var resourceName = $"PetHelper.Assets.{identifier.Replace('/', '.').Replace('-', '_')}";
        using var stream = typeof(PetAnimationPlayer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(ManifestUnavailableMessage);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
