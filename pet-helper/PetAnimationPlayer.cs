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

    private readonly WpfImage image;
    private readonly Func<ImageSource?> staticPlaceholderLoader;
    private PetAnimationPlayback? playback;
    private readonly DispatcherTimer timer;
    private readonly Dictionary<string, BitmapImage> imagesByFrame = new(StringComparer.Ordinal);
    private readonly HashSet<string> unavailableFrames = new(StringComparer.Ordinal);

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
    {
        this.image = image ?? throw new ArgumentNullException(nameof(image));
        this.staticPlaceholderLoader = staticPlaceholderLoader
            ?? throw new ArgumentNullException(nameof(staticPlaceholderLoader));
        timer = new DispatcherTimer();
        timer.Tick += Timer_Tick;

        try
        {
            playback = new PetAnimationPlayback(
                LoadManifest(manifestStreamReader ?? throw new ArgumentNullException(nameof(manifestStreamReader))),
                IsFrameAvailable);
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
    }

    internal bool IsTimerRunning => timer.IsEnabled;

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (playback is null)
        {
            timer.Stop();
            return;
        }

        playback.Advance();
        UpdateImage();
    }

    private void UpdateImage()
    {
        if (playback is null)
        {
            return;
        }

        image.Source = imagesByFrame.TryGetValue(playback.Frame, out var bitmap)
            ? bitmap
            : null;
    }

    private bool IsFrameAvailable(string frame)
    {
        if (imagesByFrame.ContainsKey(frame))
        {
            return true;
        }

        if (unavailableFrames.Contains(frame))
        {
            return false;
        }

        try
        {
            var bitmap = LoadBitmap(new Uri($"pack://application:,,,/Assets/{frame}", UriKind.Absolute));
            imagesByFrame.Add(frame, bitmap);
            return true;
        }
        catch
        {
            unavailableFrames.Add(frame);
            return false;
        }
    }

    private void ActivateStaticFallback()
    {
        timer.Stop();
        playback = null;
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
            return LoadBitmap(new Uri("pack://application:,,,/Assets/placeholder-a.png", UriKind.Absolute));
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
        bitmap.UriSource = uri;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static PetAnimationManifest LoadManifest(Func<Stream?> manifestStreamReader)
    {
        ArgumentNullException.ThrowIfNull(manifestStreamReader);

        try
        {
            using var stream = manifestStreamReader()
                ?? throw new InvalidOperationException(ManifestUnavailableMessage);
            using var reader = new StreamReader(stream);
            return PetAnimationManifest.Parse(reader.ReadToEnd());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(ManifestUnavailableMessage);
        }
    }
}
