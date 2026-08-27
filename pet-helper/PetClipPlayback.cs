namespace PetHelper;

public sealed class PetClipPlayback
{
    private ResolvedClip? clip;
    private int frameIndex;
    private bool reducedMotion;
    private bool completed;

    public event EventHandler? Completed;

    public string Frame => CurrentClip.Frames[frameIndex];
    public int FrameDurationMs => CurrentClip.FrameDurationMs;
    public bool IsAnimating => !reducedMotion && CurrentClip.Frames.Length > 1 &&
        (CurrentClip.Playback == PetClipPlaybackMode.Loop || !completed);

    public void Start(ResolvedClip nextClip, bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(nextClip);
        var identityChanged = clip is null || !string.Equals(clip.Id, nextClip.Id, StringComparison.Ordinal);
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

        if (!this.reducedMotion && CurrentClip.Playback == PetClipPlaybackMode.Once &&
            CurrentClip.Frames.Length == 1 && !completed)
        {
            Complete();
        }
    }

    public void Advance()
    {
        if (!IsAnimating) return;

        if (CurrentClip.Playback == PetClipPlaybackMode.Loop)
        {
            frameIndex = (frameIndex + 1) % CurrentClip.Frames.Length;
            return;
        }

        frameIndex++;
        if (frameIndex == CurrentClip.Frames.Length - 1)
        {
            Complete();
        }
    }

    private ResolvedClip CurrentClip => clip ??
        throw new InvalidOperationException("A pet clip must be started before it can be played.");

    private void Complete()
    {
        completed = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
