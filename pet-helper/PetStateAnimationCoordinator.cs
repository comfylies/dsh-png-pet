namespace PetHelper;

/// <summary>
/// Selects local state programs and source-to-target transitions.  It has no WPF or protocol
/// dependency; callers only provide the already-normalized animation key and controlled frame
/// availability predicate.
/// </summary>
public sealed class PetStateAnimationCoordinator
{
    private readonly PetAnimationManifest manifest;
    private readonly Func<string, bool> isFrameAvailable;
    private readonly PetClipPlayback clipPlayback = new();
    private ResolvedStateProgram? currentProgram;
    private ResolvedTransition? currentTransition;
    private ResolvedTransition? transitionAfterEnter;
    private ResolvedClip? currentClip;
    private PetAnimationKey requested;
    private AnimationPhase phase;
    private int clipIndex;
    private bool reducedMotion;
    private bool clipCompleted;
    private bool finished;

    public event EventHandler? Completed;

    public PetStateAnimationCoordinator(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable)
    {
        this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        this.isFrameAvailable = isFrameAvailable ?? throw new ArgumentNullException(nameof(isFrameAvailable));
        clipPlayback.Completed += (_, _) =>
        {
            clipCompleted = true;
            Completed?.Invoke(this, EventArgs.Empty);
        };
    }

    public string Frame => clipPlayback.Frame;
    public int IntervalMs => clipPlayback.FrameDurationMs;
    public PetStatusAnchor StatusAnchor => currentClip?.StatusAnchor ?? PetStatusAnchor.Default;
    public PetRenderTransform RenderTransform => currentClip?.RenderTransform ?? PetRenderTransform.Identity;
    public bool IsAnimating => !reducedMotion && !finished && (clipCompleted || clipPlayback.IsAnimating);

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
        if (clipCompleted)
        {
            clipCompleted = false;
            MoveToNextClip();
            return;
        }
        clipPlayback.Advance();
    }

    private void StartTarget(PetAnimationKey target, bool useEnter)
    {
        currentProgram = manifest.ResolveProgram(target, isFrameAvailable);
        currentTransition = null;
        transitionAfterEnter = null;
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
        clipIndex = 0;
        StartClip(CurrentProgram.Loop[clipIndex]);
    }

    private void StartTransition(ResolvedTransition route)
    {
        currentTransition = route;
        transitionAfterEnter = null;
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
