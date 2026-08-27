namespace PetHelper;

public sealed class PetAnimationPlayback
{
    private readonly PetAnimationManifest manifest;
    private readonly Func<string, bool> isFrameAvailable;
    private ResolvedAnimation? animation;
    private int frameIndex;

    public PetAnimationPlayback(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable)
    {
        this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        this.isFrameAvailable = isFrameAvailable ?? throw new ArgumentNullException(nameof(isFrameAvailable));
    }

    public PetAnimationKey Key => CurrentAnimation.Key;

    public string Frame => CurrentAnimation.Frames[frameIndex];

    public int IntervalMs => CurrentAnimation.IntervalMs;

    public PetStatusAnchor StatusAnchor => CurrentAnimation.StatusAnchor;

    public bool IsAnimating { get; private set; }

    public void Apply(PetAnimationKey requested, bool reducedMotion)
    {
        var resolved = manifest.Resolve(requested, isFrameAvailable);
        var effectiveKeyChanged = animation is null || animation.Key != resolved.Key;

        if (effectiveKeyChanged)
        {
            frameIndex = 0;
        }

        animation = resolved;

        if (reducedMotion)
        {
            frameIndex = 0;
            IsAnimating = false;
            return;
        }

        IsAnimating = resolved.Frames.Length > 1;
    }

    public void Advance()
    {
        if (!IsAnimating)
        {
            return;
        }

        frameIndex = (frameIndex + 1) % CurrentAnimation.Frames.Length;
    }

    private ResolvedAnimation CurrentAnimation => animation ??
        throw new InvalidOperationException("A pet animation must be applied before it can be played.");
}
