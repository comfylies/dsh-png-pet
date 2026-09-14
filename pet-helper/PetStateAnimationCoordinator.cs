namespace PetHelper;

/// <summary>
/// Selects local state programs and source-to-target transitions.  It has no WPF or protocol
/// dependency; callers only provide the already-normalized animation key and controlled frame
/// availability predicate.
/// </summary>
public sealed class PetStateAnimationCoordinator
{
    // A single-frame primary loop produces no frame advance of its own, so the coordinator keeps
    // reporting a timer interval to measure the extras cooldown against.  The cooldown is therefore
    // approximate for those primaries: the tick is a fixed one second no matter what frame duration
    // the single frame declares.  One second keeps a 5000 ms minimum cooldown at least five ticks
    // long and stays coarse enough not to wake the UI thread needlessly for a still image.
    private const int StaticPrimaryHeartbeatMs = 1000;

    private readonly Func<PetAnimationKey, Func<string, bool>, ResolvedStateProgram> resolveProgram;
    private readonly Func<string, bool> isFrameAvailable;
    private readonly Func<int, int> nextExtraIndex;
    private readonly bool useRandomExtraSelection;
    private readonly PetClipPlayback clipPlayback = new();
    private ResolvedStateProgram? currentProgram;
    private ResolvedTransition? currentTransition;
    private ResolvedTransition? transitionAfterEnter;
    private ResolvedClip? currentClip;
    private ResolvedClip? currentExtra;
    // Set only by Preview and cleared by every live entry point, so preview mode cannot survive into
    // live playback: a sticky flag would make a later live Apply restart its clip on every completion
    // and never reach the extras branch again.
    private ResolvedClip? previewClip;
    private PetAnimationKey requested;
    private AnimationPhase phase;
    private int clipIndex;
    private int extraElapsedMs;
    private int lastRandomExtraIndex = -1;
    private bool reducedMotion;
    private bool clipCompleted;
    private bool finished;

    public event EventHandler? Completed;

    public PetStateAnimationCoordinator(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable)
        : this(manifest.ResolveProgram, isFrameAvailable, null) { }

    // Exists for deterministic tests: production always uses the public two-argument constructor and
    // its random selector.  Mirrors that constructor so test callers pass a manifest rather than
    // hand-binding the resolver delegate.
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
        useRandomExtraSelection = nextExtraIndex is null;
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

    // Only a looping single-frame primary needs the heartbeat.  A single-frame one-shot primary
    // completes on its own first tick, so a heartbeat would keep restarting it, report animation
    // forever, and fire Completed on every tick without ever reaching the cooldown accumulation.
    private bool StaticPrimaryNeedsHeartbeat => currentExtra is null &&
        phase == AnimationPhase.Looping &&
        currentProgram is { } program &&
        !program.Extras.IsEmpty &&
        program.Loop[0].Frames.Length == 1 &&
        program.Loop[0].Playback == PetClipPlaybackMode.Loop;

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
            // Only a window preview restarts its own clip; live one-shot completion still belongs to
            // the extras branch below, which the preview guard here never shadows.
            if (previewClip is { } preview)
            {
                StartClip(preview);
                return;
            }
            if (currentExtra is not null)
            {
                currentExtra = null;
                StartLoop();
                return;
            }
            MoveToNextClip();
            return;
        }
        if (previewClip is null && currentExtra is null && phase == AnimationPhase.Looping)
        {
            extraElapsedMs += elapsedMs;
            if (!reducedMotion && !CurrentProgram.Extras.IsEmpty && extraElapsedMs >= CurrentProgram.ExtrasCooldownMs)
                StartExtra();
        }
    }

    /// <summary>Plays one explicit clip on repeat for the character window preview.</summary>
    internal void Preview(ResolvedClip clip, bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(clip);
        previewClip = clip;
        currentProgram = new ResolvedStateProgram(clip.Key, [], [clip], [], false);
        currentTransition = null;
        transitionAfterEnter = null;
        requested = clip.Key;
        this.reducedMotion = reducedMotion;
        currentExtra = null;
        extraElapsedMs = 0;
        lastRandomExtraIndex = -1;
        finished = false;
        phase = AnimationPhase.Looping;
        clipIndex = 0;
        StartClip(clip);
    }

    private void StartExtra()
    {
        var extras = CurrentProgram.Extras;
        var index = nextExtraIndex(extras.Length);
        // A random idle action should not immediately repeat itself when alternatives exist.
        // The injected selector remains untouched for deterministic tests and previews.
        if (useRandomExtraSelection && extras.Length > 1 && index == lastRandomExtraIndex)
            index = (index + 1 + Random.Shared.Next(extras.Length - 1)) % extras.Length;
        if (useRandomExtraSelection) lastRandomExtraIndex = index;
        currentExtra = extras[index];
        phase = AnimationPhase.Looping;
        clipIndex = 0;
        StartClip(currentExtra);
    }

    private void StartTarget(PetAnimationKey target, bool useEnter)
    {
        previewClip = null;
        currentProgram = resolveProgram(target, isFrameAvailable);
        currentTransition = null;
        transitionAfterEnter = null;
        currentExtra = null;
        extraElapsedMs = 0;
        lastRandomExtraIndex = -1;
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
        previewClip = null;
        phase = AnimationPhase.Looping;
        currentExtra = null;
        extraElapsedMs = 0;
        clipIndex = 0;
        StartClip(CurrentProgram.Loop[clipIndex]);
    }

    private void StartTransition(ResolvedTransition route)
    {
        previewClip = null;
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
