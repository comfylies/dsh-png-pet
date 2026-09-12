namespace PetHelper;

/// <summary>
/// Selects local state programs and source-to-target transitions.  It has no WPF or protocol
/// dependency; callers only provide the already-normalized animation key and controlled frame
/// availability predicate.
/// </summary>
public sealed class PetStateAnimationCoordinator
{
    // A single-frame primary loop produces no frame advance of its own, so the coordinator keeps
    // reporting a timer interval to measure the extras cooldown against.
    private const int StaticPrimaryHeartbeatMs = 1000;

    private readonly Func<PetAnimationKey, Func<string, bool>, ResolvedStateProgram> resolveProgram;
    private readonly Func<string, bool> isFrameAvailable;
    private readonly Func<int, int> nextExtraIndex;
    private readonly PetClipPlayback clipPlayback = new();
    private ResolvedStateProgram? currentProgram;
    private ResolvedTransition? currentTransition;
    private ResolvedTransition? transitionAfterEnter;
    private ResolvedClip? currentClip;
    private ResolvedClip? currentExtra;
    private PetAnimationKey requested;
    private AnimationPhase phase;
    private int clipIndex;
    private int extraElapsedMs;
    private bool reducedMotion;
    private bool clipCompleted;
    private bool finished;

    public event EventHandler? Completed;

    public PetStateAnimationCoordinator(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable)
        : this(manifest.ResolveProgram, isFrameAvailable, null) { }

    // Mirrors the public constructor for callers that must supply a deterministic extra selector.  The
    // overload exists so those callers pass a manifest instead of hand-binding the resolver delegate.
    internal PetStateAnimationCoordinator(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable,
        Func<int, int>? nextExtraIndex)
        : this(manifest.ResolveProgram, isFrameAvailable, nextExtraIndex) { }

    internal PetStateAnimationCoordinator(
        Func<PetAnimationKey, Func<string, bool>, ResolvedStateProgram> resolveProgram,
        Func<string, bool> isFrameAvailable,
        Func<int, int>? nextExtraIndex = null)
    {
        this.resolveProgram = resolveProgram;
        this.isFrameAvailable = isFrameAvailable ?? throw new ArgumentNullException(nameof(isFrameAvailable));
        this.nextExtraIndex = nextExtraIndex ?? (count => Random.Shared.Next(count));
        clipPlayback.Completed += (_, _) =>
        {
            clipCompleted = true;
            Completed?.Invoke(this, EventArgs.Empty);
        };
    }

    public string Frame => clipPlayback.Frame;
    public int IntervalMs => StaticPrimaryNeedsHeartbeat ? StaticPrimaryHeartbeatMs : clipPlayback.FrameDurationMs;
    public PetStatusAnchor StatusAnchor => currentClip?.StatusAnchor ?? PetStatusAnchor.Default;
    public PetRenderTransform RenderTransform => currentClip?.RenderTransform ?? PetRenderTransform.Identity;
    public bool IsAnimating => !reducedMotion && !finished &&
        (clipCompleted || clipPlayback.IsAnimating || StaticPrimaryNeedsHeartbeat);

    private bool StaticPrimaryNeedsHeartbeat => currentExtra is null &&
        phase == AnimationPhase.Looping &&
        currentProgram is { } program &&
        !program.Extras.IsEmpty &&
        program.Loop[0].Frames.Length == 1;

    public void Apply(PetAnimationKey nextRequested, bool reducedMotion)
    {
        if (currentProgram is null)
        {
            requested = nextRequested;
            this.reducedMotion = reducedMotion;
            StartTarget(nextRequested, useEnter: !reducedMotion);
            return;
        }

        if (reducedMotion != this.reducedMotion)
        {
            requested = nextRequested;
            this.reducedMotion = reducedMotion;
            StartTarget(nextRequested, useEnter: false);
            return;
        }

        if (nextRequested == requested) return;
        requested = nextRequested;
        if (reducedMotion)
        {
            StartTarget(nextRequested, useEnter: false);
            return;
        }

        var route = RouteFor(nextRequested);
        if (phase == AnimationPhase.Transitioning)
        {
            if (currentTransition is not null && currentTransition.Targets.Contains(nextRequested)) return;
            if (route is not null)
            {
                StartTransition(route);
            }
            else
            {
                StartTarget(nextRequested, useEnter: true);
            }
            return;
        }

        if (phase == AnimationPhase.Entering && route is not null)
        {
            transitionAfterEnter = route;
            return;
        }

        if (route is not null)
        {
            StartTransition(route);
            return;
        }

        StartTarget(nextRequested, useEnter: true);
    }

    public void Advance()
    {
        if (!IsAnimating) return;
        var elapsedMs = IntervalMs;
        clipPlayback.Advance();
        if (clipCompleted)
        {
            clipCompleted = false;
            if (currentExtra is not null)
            {
                currentExtra = null;
                StartLoop();
                return;
            }
            MoveToNextClip();
            return;
        }
        if (currentExtra is null && phase == AnimationPhase.Looping)
        {
            extraElapsedMs += elapsedMs;
            if (!reducedMotion && CurrentProgram.Extras.Length > 0 && extraElapsedMs >= CurrentProgram.ExtrasCooldownMs)
                StartExtra();
        }
    }

    private void StartExtra()
    {
        var extras = CurrentProgram.Extras;
        currentExtra = extras[nextExtraIndex(extras.Length)];
        phase = AnimationPhase.Looping;
        clipIndex = 0;
        StartClip(currentExtra);
    }

    private void StartTarget(PetAnimationKey target, bool useEnter)
    {
        currentProgram = resolveProgram(target, isFrameAvailable);
        currentTransition = null;
        transitionAfterEnter = null;
        currentExtra = null;
        extraElapsedMs = 0;
        requested = target;
        if (!useEnter || reducedMotion || currentProgram.Enter.IsEmpty)
        {
            StartLoop();
            return;
        }

        phase = AnimationPhase.Entering;
        clipIndex = 0;
        StartClip(currentProgram.Enter[clipIndex]);
    }

    private void StartLoop()
    {
        phase = AnimationPhase.Looping;
        currentExtra = null;
        extraElapsedMs = 0;
        clipIndex = 0;
        StartClip(CurrentProgram.Loop[clipIndex]);
    }

    private void StartTransition(ResolvedTransition route)
    {
        currentTransition = route;
        transitionAfterEnter = null;
        currentExtra = null;
        extraElapsedMs = 0;
        phase = AnimationPhase.Transitioning;
        clipIndex = 0;
        StartClip(route.Clips[clipIndex]);
    }

    private void MoveToNextClip()
    {
        switch (phase)
        {
            case AnimationPhase.Entering:
                if (++clipIndex < CurrentProgram.Enter.Length)
                {
                    StartClip(CurrentProgram.Enter[clipIndex]);
                }
                else if (transitionAfterEnter is { } route)
                {
                    StartTransition(route);
                }
                else
                {
                    StartLoop();
                }
                break;
            case AnimationPhase.Looping:
                if (!CurrentProgram.LoopRepeats)
                {
                    finished = true;
                    return;
                }
                clipIndex = (clipIndex + 1) % CurrentProgram.Loop.Length;
                StartClip(CurrentProgram.Loop[clipIndex]);
                break;
            case AnimationPhase.Transitioning:
                if (++clipIndex < currentTransition!.Clips.Length)
                {
                    StartClip(currentTransition.Clips[clipIndex]);
                }
                else
                {
                    StartTarget(requested, useEnter: true);
                }
                break;
        }
    }

    private void StartClip(ResolvedClip clip)
    {
        currentClip = clip;
        clipCompleted = false;
        finished = false;
        clipPlayback.Start(clip, reducedMotion, restart: true);
    }

    private ResolvedTransition? RouteFor(PetAnimationKey target) => currentProgram?
        .Transitions.FirstOrDefault(transition => transition.Targets.Contains(target));

    private ResolvedStateProgram CurrentProgram => currentProgram ??
        throw new InvalidOperationException("A state program must be applied before playback.");

    private enum AnimationPhase { Entering, Looping, Transitioning }
}
