namespace PetHelper;

public sealed class PetAnimationPlayback
{
    private readonly PetAnimationManifest manifest;
    private readonly Func<string, bool> isFrameAvailable;
    private readonly PetClipPlayback clipPlayback = new();
    private ResolvedClip? clip;

    public PetAnimationPlayback(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable)
    {
        this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        this.isFrameAvailable = isFrameAvailable ?? throw new ArgumentNullException(nameof(isFrameAvailable));
    }

    public PetAnimationKey Key => CurrentClip.Key;

    public string Frame => clipPlayback.Frame;

    public int IntervalMs => clipPlayback.FrameDurationMs;

    public PetStatusAnchor StatusAnchor => CurrentClip.StatusAnchor;

    public bool IsAnimating => clipPlayback.IsAnimating;

    public event EventHandler? Completed
    {
        add => clipPlayback.Completed += value;
        remove => clipPlayback.Completed -= value;
    }

    public void Apply(PetAnimationKey requested, bool reducedMotion)
    {
        clip = manifest.Resolve(requested, isFrameAvailable);
        clipPlayback.Start(clip, reducedMotion);
    }

    public void Advance()
    {
        clipPlayback.Advance();
    }

    private ResolvedClip CurrentClip => clip ??
        throw new InvalidOperationException("A pet animation must be applied before it can be played.");
}
