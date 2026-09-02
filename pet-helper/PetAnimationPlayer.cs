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
    // At 512px RGBA, 48 frames are about 48 MiB of decoded pixels before WPF compositor copies.
    // Keeping this bounded prevents a long-running pet from retaining every state it has visited.
    private const int MaximumCachedFrames = 48;
    private const int AnimationDecodePixelWidth = 512;

    private readonly WpfImage image;
    private readonly Func<ImageSource?> staticPlaceholderLoader;
    private PetStateAnimationCoordinator? playback;
    private readonly DispatcherTimer timer;
    private readonly BoundedLruCache<string, BitmapImage> imagesByFrame = new(MaximumCachedFrames);
    private readonly HashSet<string> availableFrames = new(StringComparer.Ordinal);
    private readonly HashSet<string> unavailableFrames = new(StringComparer.Ordinal);

    public event EventHandler? Completed;

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
        Func<ImageSource?> staticPlaceholderLoader)
    {
        this.image = image ?? throw new ArgumentNullException(nameof(image));
        this.staticPlaceholderLoader = staticPlaceholderLoader
            ?? throw new ArgumentNullException(nameof(staticPlaceholderLoader));
        // State updates from a streaming reply arrive at normal dispatcher priority.  Render
        // priority keeps the fixed-rate frame timer from being starved by that input burst.
        timer = new DispatcherTimer(DispatcherPriority.Render);
        timer.Tick += Timer_Tick;

        try
        {
            playback = new PetStateAnimationCoordinator(
                LoadManifest(manifestStreamReader, manifestReaderFactory),
                IsFrameAvailable);
            playback.Completed += Playback_Completed;
        }
        catch (InvalidOperationException)
        {
            ActivateStaticFallback();
        }
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

            if (playback.IsAnimating)
            {
                timer.Interval = TimeSpan.FromMilliseconds(playback.IntervalMs);
                timer.Start();
                return;
            }

            timer.Stop();
        }
        catch (InvalidOperationException)
        {
            ActivateStaticFallback();
        }
    }

    public void Stop()
    {
        timer.Stop();
        timer.Tick -= Timer_Tick;
        if (playback is not null) playback.Completed -= Playback_Completed;
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

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (playback is null)
        {
            timer.Stop();
            return;
        }

        playback.Advance();
        UpdateImage();
        if (!playback.IsAnimating)
        {
            timer.Stop();
            return;
        }
        timer.Interval = TimeSpan.FromMilliseconds(playback.IntervalMs);
    }

    private void Playback_Completed(object? sender, EventArgs e) => Completed?.Invoke(this, EventArgs.Empty);

    private void UpdateImage()
    {
        if (playback is null)
        {
            return;
        }

        image.Source = TryLoadFrame(playback.Frame);
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
            bitmap = LoadBitmap(FrameUri(frame));
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
