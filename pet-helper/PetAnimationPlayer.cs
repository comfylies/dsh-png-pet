using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfImage = System.Windows.Controls.Image;

namespace PetHelper;

public sealed class PetAnimationPlayer
{
    private const string ManifestResourceName = "PetHelper.Assets.pet-animations.json";
    private const string ManifestUnavailableMessage = "The pet animation manifest is unavailable.";

    private readonly WpfImage image;
    private readonly PetAnimationPlayback playback;
    private readonly DispatcherTimer timer;
    private readonly Dictionary<string, BitmapImage> imagesByFrame = new(StringComparer.Ordinal);
    private readonly HashSet<string> unavailableFrames = new(StringComparer.Ordinal);

    public PetAnimationPlayer(WpfImage image)
    {
        this.image = image ?? throw new ArgumentNullException(nameof(image));
        playback = new PetAnimationPlayback(LoadManifest(typeof(PetAnimationPlayer).Assembly), IsFrameAvailable);
        timer = new DispatcherTimer();
        timer.Tick += Timer_Tick;
    }

    public void Apply(PetAnimationKey key, bool reducedMotion)
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

    public void Stop()
    {
        timer.Stop();
        timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        playback.Advance();
        UpdateImage();
    }

    private void UpdateImage()
    {
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
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri($"pack://application:,,,/Assets/{frame}", UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            imagesByFrame.Add(frame, bitmap);
            return true;
        }
        catch
        {
            unavailableFrames.Add(frame);
            return false;
        }
    }

    private static PetAnimationManifest LoadManifest(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            using var stream = assembly.GetManifestResourceStream(ManifestResourceName)
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
