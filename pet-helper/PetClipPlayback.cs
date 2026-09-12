namespace PetHelper;

public sealed class PetClipPlayback
{
    private ResolvedClip? clip;
    private int frameIndex;
    private bool reducedMotion;
    private bool completed;

    public event EventHandler? Completed;

    public string Frame => CurrentClip.Frames[frameIndex];
    public int FrameDurationMs => CurrentClip.FrameDurationsMs.IsDefaultOrEmpty
        ? CurrentClip.FrameDurationMs : CurrentClip.FrameDurationsMs[frameIndex];
    public bool IsAnimating => !reducedMotion && (CurrentClip.Playback == PetClipPlaybackMode.Once
        ? !completed : CurrentClip.Frames.Length > 1);

    public void Start(ResolvedClip nextClip, bool reducedMotion, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(nextClip);
        if (nextClip.Frames.IsDefaultOrEmpty || (!nextClip.FrameDurationsMs.IsDefault &&
            (nextClip.FrameDurationsMs.Length != nextClip.Frames.Length ||
             nextClip.FrameDurationsMs.Any(duration => duration is < 16 or > 10000))))
            throw new ArgumentException("Invalid clip timing.");
        var identityChanged = restart || clip is null || !string.Equals(clip.Id, nextClip.Id, StringComparison.Ordinal);
        if (identityChanged)
        {
            clip = nextClip;
            frameIndex = 0;
            completed = false;
        }

        if (reducedMotion && !this.reducedMotion)
        {
            frameIndex = 0;
        }
        this.reducedMotion = reducedMotion;

    }

    public void Advance()
    {
        if (!IsAnimating) return;

        if (CurrentClip.Playback == PetClipPlaybackMode.Loop)
        {
            frameIndex = (frameIndex + 1) % CurrentClip.Frames.Length;
            return;
        }

        if (frameIndex == CurrentClip.Frames.Length - 1)
        {
            Complete();
        }
        else frameIndex++;
    }

    private ResolvedClip CurrentClip => clip ??
        throw new InvalidOperationException("A pet clip must be started before it can be played.");

    private void Complete()
    {
        completed = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
