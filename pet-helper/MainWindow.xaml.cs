using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetHelper;

public partial class MainWindow : Window
{
    private const double StatusBubbleGap = 4d;

    /// <summary>Set to 0 to suspend clip playback during physics flights (diagnostic A/B); playback keeps running by default.</summary>
    private const string PhysicsKeepAnimationEnvironmentVariable = "DSH_PNG_PET_PHYSICS_KEEP_ANIMATION";
    /// <summary>When set to 1, physics advances on fixed 1/60s steps (diagnostic A/B).</summary>
    private const string PhysicsPacedEnvironmentVariable = "DSH_PNG_PET_PHYSICS_PACED";
    /// <summary>When set to 1, 1 ms Windows timer resolution is requested for the duration of a flight (diagnostic A/B).</summary>
    private const string PhysicsTimerResolutionEnvironmentVariable = "DSH_PNG_PET_PHYSICS_TIMER_RES";
    /// <summary>Set to 0 to drive flight moves from WPF render events instead of the pacing timer (diagnostic A/B); pacing is the default.</summary>
    private const string PhysicsPacerEnvironmentVariable = "DSH_PNG_PET_PHYSICS_PACER";
    /// <summary>When set to 1, the pet drops itself through the 2×2 diagnostic matrix on startup and logs each flight.</summary>
    private const string PhysicsSelfTestEnvironmentVariable = "DSH_PNG_PET_SELFTEST";
    private static readonly TimeSpan PhysicsSelfTestLegDelay = TimeSpan.FromMilliseconds(1200);
    /// <summary>Fixed simulation step used when frame pacing is enabled.</summary>
    private const double PhysicsFixedStepSeconds = 1d / 60d;
    /// <summary>How much pacing debt a stalled pipeline may accumulate before it is dropped.</summary>
    private const int PhysicsMaxPacingDebtSteps = 2;

    /// <summary>
    /// How far the pet window may protrude past a screen edge so the character body visually
    /// hugs the edge. Derived from the 1254×1254 placeholder PNG (opaque x[274..1074],
    /// y[6..1224]) inside a 220×220 window with the image bottom-aligned (margin 20) and
    /// Uniform-scaled: body is ≈54px from the window's left, ≈21px from its top, ≈29px from
    /// its right, ≈5px from its bottom. Recompute when the character art changes.
    /// </summary>
    private static readonly PlacementPlanner.EdgeProtrusion PetProtrusion = new(54, 21, 29, 5);

    private readonly PetWindowStateStore stateStore = new();
    private readonly PetAnimationPlayer animationPlayer;
    private readonly IScreenLayout screenLayout;
    private PetDisplayState lastDisplayState = new("idle", string.Empty, 0);
    private bool reducedMotion;
    private bool restoringState = true;
    private bool hasSavedPlacement;
    private double defaultScale = 1d;
    private string defaultPetPlacement = DefaultLayout.Center;
    private DialogueWindow? dialogueWindow;
    private PeakValleyCardWindow? peakValleyCard;
    private readonly PetPointerGesture pointerGesture = new();
    private readonly DispatcherTimer singleClickTimer = new();
    private readonly DispatcherTimer randomChatTimer = new();
    private readonly DispatcherTimer randomChatExpiryTimer = new();
    private PetPhysicsState physicsState;
    private bool physicsEnabled;
    private volatile bool physicsRendering;
    private bool physicsAnimationPaused;
    private int physicsBouncePercent = 65;
    private long physicsLastTick;
    private readonly PhysicsFlightStats physicsFlightStats = new();
    private readonly DispatcherTimer physicsSelfTestTimer = new();
    private readonly bool physicsSelfTestMode =
        Environment.GetEnvironmentVariable(PhysicsSelfTestEnvironmentVariable) == "1";
    private bool keepAnimationDuringFlight =
        Environment.GetEnvironmentVariable(PhysicsKeepAnimationEnvironmentVariable) != "0";
    private readonly bool physicsPaced60Default =
        Environment.GetEnvironmentVariable(PhysicsPacedEnvironmentVariable) == "1";
    private readonly bool physicsTimerResolutionDefault =
        Environment.GetEnvironmentVariable(PhysicsTimerResolutionEnvironmentVariable) == "1";
    private readonly bool physicsPacerDefault =
        Environment.GetEnvironmentVariable(PhysicsPacerEnvironmentVariable) != "0";
    private bool physicsPaced60Enabled;
    private bool physicsTimerResolutionEnabled;
    private bool physicsPacerEnabled;
    private double physicsStepTargetSeconds;
    private double physicsPacingDebtSeconds;
    private System.Threading.Timer? physicsPacerTimer;
    private double physicsDisplayRefreshHz;
    private int physicsSelfTestLegIndex;
    private long physicsFlightStartTick;
    private long physicsLastMoveTick;
    /// <summary>The 2×2 diagnostic matrix: (clip running?, 1 ms timer resolution?, pacing timer?).</summary>
    private static readonly (bool KeepAnimation, bool TimerResolution, bool Pacer)[] PhysicsSelfTestLegs =
    {
        (KeepAnimation: true, TimerResolution: false, Pacer: false),
        (KeepAnimation: true, TimerResolution: true, Pacer: false),
        (KeepAnimation: true, TimerResolution: true, Pacer: true),
        (KeepAnimation: false, TimerResolution: true, Pacer: true),
    };
    private Point lastDragSamplePosition;
    private long lastDragSampleTick;
    private Vector dragReleaseVelocity;
    private bool randomChatEnabled;
    private int randomChatMinIntervalMinutes = 8;
    private int randomChatMaxIntervalMinutes = 24;
    private IReadOnlyList<RandomChatPrompt> randomChatPrompts = RandomChatPromptCatalog.Load();
    private long nextRandomChatInvitationId;
    private long? displayedRandomChatInvitationId;
    private string? displayedRandomChatTopic;

    private bool dragging;
    private bool combinedDrag;
    private Rect dragStartPetRect;
    private Rect dragStartDialogueRect;
    private Rect? lastMovedDialogueRect;

    public event EventHandler? HiddenToTray;

    /// <summary>Raised when the user opens the target-selection card (menu or dropped folder).</summary>
    public event EventHandler<string?>? TargetSelectionRequested;

    public event EventHandler<RandomChatRequestedEventArgs>? RandomChatRequested;

    /// <summary>Raised when a safe question or Web-routed approval bubble opens the already-running Harness.</summary>
    public event EventHandler? HarnessOpenRequested;

    public MainWindow(IScreenLayout screenLayout)
    {
        InitializeComponent();
        this.screenLayout = screenLayout;
        physicsPaced60Enabled = physicsPaced60Default;
        physicsTimerResolutionEnabled = physicsTimerResolutionDefault;
        physicsPacerEnabled = physicsPacerDefault;
        animationPlayer = new PetAnimationPlayer(PetImage);
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion: false);
        singleClickTimer.Tick += (_, _) =>
        {
            singleClickTimer.Stop();
            ShowPeakValleyCard();
        };
        randomChatTimer.Tick += (_, _) =>
        {
            randomChatTimer.Stop();
            ShowRandomChatInvitation();
        };
        randomChatExpiryTimer.Tick += (_, _) =>
        {
            randomChatExpiryTimer.Stop();
            DismissRandomChatInvitation();
            ScheduleRandomChatInvitation();
        };
        physicsSelfTestTimer.Tick += (_, _) =>
        {
            physicsSelfTestTimer.Stop();
            DropPhysicsSelfTestLeg();
        };
        RestoreState();
        restoringState = false;
        Loaded += (_, _) => UpdateStateBubblePosition();
        if (physicsSelfTestMode)
        {
            Loaded += (_, _) => BeginPhysicsSelfTest();
        }
        Closed += (_, _) =>
        {
            StopPhysicsRendering();
            animationPlayer.Stop();
            singleClickTimer.Stop();
            randomChatTimer.Stop();
            randomChatExpiryTimer.Stop();
            physicsSelfTestTimer.Stop();
            peakValleyCard?.CloseCard();
        };
    }

    public void AttachDialogueWindow(DialogueWindow window)
    {
        dialogueWindow = window;
        window.DialogueClosed += (_, _) =>
        {
            ScheduleRandomChatInvitation();
            ResumePhysicsIfEligible();
        };
    }

    public void AttachPeakValleyCard(PeakValleyCardWindow window)
    {
        peakValleyCard = window;
        window.RandomChatClicked += (_, _) => OpenDisplayedRandomChat();
    }

    public void ToggleDialogueWindow()
    {
        if (dialogueWindow is null) return;
        if (dialogueWindow.IsVisible)
        {
            dialogueWindow.CloseToHidden();
            ResumePhysicsIfEligible();
        }
        else
        {
            PausePhysics();
            DismissRandomChatInvitation();
            dialogueWindow.ShowDialogue(CurrentRect());
        }
    }

    public void ApplyDisplayState(PetDisplayState state)
    {
        lastDisplayState = state;
        StateLabel.Text = state.Label;
        StateBubble.Visibility = state.State == "idle" ? Visibility.Collapsed : Visibility.Visible;
        StateBubbleCanvas.IsHitTestVisible = StateBubbleCanOpenHarness(state.State);
        StateBubble.Cursor = StateBubbleCanOpenHarness(state.State) ? Cursors.Hand : Cursors.Arrow;
        StateBubble.Background = state.State switch
        {
            "waiting" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 142, 74, 29)),
            "question" => new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 105, 67, 150)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 43, 75, 95)),
        };
        animationPlayer.Apply(state.AnimationKey, reducedMotion);
        UpdateStateBubblePosition();
        ScheduleRandomChatInvitation();
    }

    public void ApplyConfig(ConfigMessage config)
    {
        defaultScale = config.Scale;
        defaultPetPlacement = config.PetPlacement;
        reducedMotion = config.ReducedMotion;
        physicsBouncePercent = config.PhysicsBouncePercent;
        randomChatEnabled = config.RandomChatEnabled && config.RandomChatBrowseOnOpen && config.RandomChatConfigured;
        randomChatMinIntervalMinutes = config.RandomChatMinIntervalMinutes;
        randomChatMaxIntervalMinutes = config.RandomChatMaxIntervalMinutes;
        randomChatPrompts = BuildRandomChatPrompts(config.RandomChatCustomPrompts);
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion);
        ApplyState(PetWindowState.Normalize(Left, Top, config.Scale));
        if (!hasSavedPlacement)
        {
            ApplyDefaultPlacement(config.PetPlacement);
            hasSavedPlacement = true;
        }
        CancelPendingPeakValleyCard();
        UpdateStateBubblePosition();
        if (!randomChatEnabled)
        {
            DismissRandomChatInvitation();
        }
        else if (displayedRandomChatInvitationId is null)
        {
            peakValleyCard?.Dismiss();
        }
        ScheduleRandomChatInvitation();
        ConfigurePhysics(config.PhysicsEnabled && !config.ReducedMotion);
        SaveState();
    }

    /** Opens the dialogue without toggling it closed when an approval needs an answer. */
    public void ShowDialogueForApproval()
    {
        if (dialogueWindow is null) return;
        PausePhysics();
        DismissRandomChatInvitation();
        dialogueWindow.ShowDialogue(CurrentRect());
    }

    public void ShowRandomChatDialogue(long invitationId)
    {
        if (displayedRandomChatInvitationId != invitationId) return;
        PausePhysics();
        DismissRandomChatInvitation();
        dialogueWindow?.ShowDialogue(CurrentRect());
    }

    public void ShowRandomChatError(long invitationId)
    {
        if (displayedRandomChatInvitationId != invitationId) return;
        peakValleyCard?.ShowRandomChatError(CurrentHeadRect(), screenLayout, CurrentHeadRect().Height);
        randomChatExpiryTimer.Stop();
        randomChatExpiryTimer.Interval = TimeSpan.FromSeconds(4);
        randomChatExpiryTimer.Start();
    }

    public void ShowRandomChatInvitationForTest()
    {
        ShowRandomChatInvitation(force: true);
    }

    /** Restarts local-only physics after the tray restores the visible pet. */
    public void ResumePhysicsAfterRestore() => ResumePhysicsIfEligible();

    public void SaveState()
    {
        // The standalone self-test relocates the pet for its drops; never persist those moves.
        if (!restoringState && !physicsSelfTestMode)
        {
            stateStore.Save(CurrentState());
        }
    }

    private void RestoreState()
    {
        var state = stateStore.Load();
        hasSavedPlacement = state.Left is not null && state.Top is not null;
        ApplyState(state);
    }

    private void ApplyDefaultPlacement(string placement)
    {
        var target = DefaultLayout.Place(placement, screenLayout.PrimaryWorkArea, new Size(Width, Height));
        Left = target.X;
        Top = target.Y;
    }

    private void ConfigurePhysics(bool enabled)
    {
        if (!enabled)
        {
            var position = CurrentPosition();
            physicsEnabled = false;
            StopPhysicsRendering();
            physicsState = PetPhysics.Rest(position);
            CommitPhysicsPosition();
            return;
        }

        if (!physicsEnabled)
        {
            physicsState = PetPhysics.StartFalling(CurrentPosition());
        }
        physicsEnabled = true;
        ResumePhysicsIfEligible();
    }

    private void PausePhysics()
    {
        StopPhysicsRendering();
    }

    private void ResumePhysicsIfEligible()
    {
        if (!physicsEnabled || reducedMotion || dragging || dialogueWindow is { IsVisible: true } || !IsVisible)
        {
            return;
        }
        physicsLastTick = Stopwatch.GetTimestamp();
        if (!physicsState.IsResting) StartPhysicsRendering();
    }

    /// <summary>
    /// Moves airborne window steps at the display refresh instead of relying on WPF render
    /// events. Diagnostic flights showed that render-event delivery is bursty (sub-millisecond
    /// clusters alternating with 30–47 ms gaps), which reads as dropped frames during fast
    /// bounces; a pacing timer at the measured display refresh delivers exactly one window move
    /// per display frame with zero gaps. The pacing timer is the default (disable with
    /// DSH_PNG_PET_PHYSICS_PACER=0). Clip playback keeps running during flights (override with
    /// DSH_PNG_PET_PHYSICS_KEEP_ANIMATION=0); only an explicit drag pauses it, since a dragged
    /// surface is not redrawn while it follows the cursor.
    /// </summary>
    private void StartPhysicsRendering()
    {
        if (physicsRendering) return;
        physicsRendering = true;
        physicsFlightStats.Reset();
        physicsFlightStartTick = Stopwatch.GetTimestamp();
        physicsLastMoveTick = 0;
        physicsPacingDebtSeconds = 0d;
        physicsDisplayRefreshHz = NativeTiming.ProbeDisplayRefreshHz(new WindowInteropHelper(this).Handle);
        physicsStepTargetSeconds = physicsPacerEnabled
            ? (physicsDisplayRefreshHz > 0d ? 1d / physicsDisplayRefreshHz : PhysicsFixedStepSeconds)
            : physicsPaced60Enabled
                ? PhysicsFixedStepSeconds
                : 0d;
        if (physicsPacerEnabled || physicsTimerResolutionEnabled)
        {
            NativeTiming.EnableHighResolutionTimer();
        }
        physicsAnimationPaused = !keepAnimationDuringFlight;
        if (physicsAnimationPaused)
        {
            animationPlayer.Pause();
        }
        else
        {
            // The drag that released this toss paused the player; flight is smoother when the
            // clip keeps the render pipeline producing frames at the display rate.
            animationPlayer.Resume();
        }
        if (physicsPacerEnabled)
        {
            StartPhysicsPacer();
        }
        else
        {
            CompositionTarget.Rendering += Physics_Rendering;
        }
    }

    private void StopPhysicsRendering()
    {
        if (!physicsRendering) return;
        physicsRendering = false;
        if (physicsPacerEnabled)
        {
            physicsPacerTimer?.Dispose();
            physicsPacerTimer = null;
        }
        else
        {
            CompositionTarget.Rendering -= Physics_Rendering;
        }
        if (physicsPacerEnabled || physicsTimerResolutionEnabled)
        {
            NativeTiming.DisableHighResolutionTimer();
        }
        WritePhysicsFlightDiagnostics();
        if (!physicsAnimationPaused) return;
        physicsAnimationPaused = false;
        animationPlayer.Resume();
    }

    /// <summary>
    /// Paces flight window moves from a thread-pool timer instead of WPF render events. The
    /// tick only queues UI-thread work; all WPF state stays on the dispatcher thread.
    /// </summary>
    private void StartPhysicsPacer()
    {
        var periodMs = Math.Max(1, (int)Math.Round(physicsStepTargetSeconds * 1000d / 2d));
        physicsPacerTimer?.Dispose();
        physicsPacerTimer = new System.Threading.Timer(
            static state => ((MainWindow)state!).QueuePhysicsPacerTick(),
            this,
            dueTime: periodMs,
            period: periodMs);
    }

    private void QueuePhysicsPacerTick()
    {
        if (!physicsRendering) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(AdvancePhysics));
        }
        catch
        {
            // A tick racing the dispatcher shutdown is harmless.
        }
    }

    private void Physics_Rendering(object? sender, EventArgs e) => AdvancePhysics();

    private void AdvancePhysics()
    {
        if (!physicsRendering || !physicsEnabled || reducedMotion || dragging
            || dialogueWindow is { IsVisible: true } || !IsVisible)
        {
            if (physicsRendering) PausePhysics();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - physicsLastTick) / (double)Stopwatch.Frequency;
        physicsLastTick = now;
        var executed = false;
        var lastExecutedTick = physicsLastMoveTick == 0 ? physicsFlightStartTick : physicsLastMoveTick;
        var intervalSinceMoveMs = (now - lastExecutedTick) / (double)Stopwatch.Frequency * 1000d;

        if (physicsStepTargetSeconds > 0d)
        {
            // Diagnostics show CompositionTarget.Rendering firing in sub-millisecond bursts far
            // above the display refresh when nothing else paces the pipeline. Moving the window
            // on every event floods the dispatcher with window messages and stalls the render
            // chain. Instead: bank the elapsed time and execute at most one fixed step per tick
            // (1/60s or one display frame), with a bounded debt so a deep stall never spawns a
            // teleporting catch-up.
            physicsPacingDebtSeconds = Math.Min(
                physicsPacingDebtSeconds + elapsed,
                physicsStepTargetSeconds * PhysicsMaxPacingDebtSteps);
            if (physicsPacingDebtSeconds < physicsStepTargetSeconds)
            {
                return;
            }
            physicsPacingDebtSeconds -= physicsStepTargetSeconds;
            AdvanceOnePhysicsStep(physicsStepTargetSeconds, intervalSinceMoveMs);
            executed = true;
        }
        else
        {
            AdvanceOnePhysicsStep(elapsed, intervalSinceMoveMs);
            executed = true;
        }

        if (executed && physicsState.IsResting)
        {
            physicsPacingDebtSeconds = 0d;
            CommitPhysicsPosition();
            PausePhysics();
            SaveState();
            ScheduleNextPhysicsSelfTestLeg();
        }
    }

    private void AdvanceOnePhysicsStep(double stepSeconds, double intervalSinceMoveMs)
    {
        var size = new Size(ActualWidth, ActualHeight);
        var rect = new Rect(physicsState.Position, size);
        physicsState = PetPhysics.Advance(
            physicsState,
            size,
            screenLayout.WorkAreaFor(rect),
            physicsBouncePercent,
            stepSeconds);
        // A transparent WPF window visibly stutters when Left and Top each issue a separate
        // window-position update. Move the HWND once per simulation tick instead.
        var move = Stopwatch.StartNew();
        WindowMover.Move(this, physicsState.Position.X, physicsState.Position.Y);
        move.Stop();
        physicsLastMoveTick = Stopwatch.GetTimestamp();
        physicsFlightStats.Add(intervalSinceMoveMs, move.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Standalone diagnostics (DSH_PNG_PET_SELFTEST=1): drops the pet from the top edge of its
    /// work area once per diagnostic leg — clip running or paused, 1 ms timer resolution on or
    /// off, window moves driven by WPF render events or by a pacing timer — and logs each
    /// flight, so the frame cadence of every path can be compared without needing a host session.
    /// </summary>
    private void BeginPhysicsSelfTest()
    {
        physicsEnabled = true;
        reducedMotion = false;
        physicsBouncePercent = 65;
        physicsState = PetPhysics.Rest(CurrentPosition());
        physicsSelfTestLegIndex = 0;
        DropPhysicsSelfTestLeg();
    }

    private void DropPhysicsSelfTestLeg()
    {
        if (physicsSelfTestLegIndex >= PhysicsSelfTestLegs.Length) return;
        var (keepAnimation, timerResolution, pacer) = PhysicsSelfTestLegs[physicsSelfTestLegIndex++];
        keepAnimationDuringFlight = keepAnimation;
        physicsTimerResolutionEnabled = timerResolution;
        physicsPacerEnabled = pacer;
        var size = new Size(ActualWidth, ActualHeight);
        var workArea = screenLayout.WorkAreaFor(new Rect(CurrentPosition(), size));
        var target = new Point(workArea.Left + (workArea.Width - size.Width) / 2d, workArea.Top);
        physicsState = PetPhysics.Rest(target);
        CommitPhysicsPosition();
        physicsState = PetPhysics.StartFalling(target);
        ResumePhysicsIfEligible();
    }

    private void ScheduleNextPhysicsSelfTestLeg()
    {
        if (!physicsSelfTestMode) return;
        physicsSelfTestTimer.Stop();
        if (physicsSelfTestLegIndex < PhysicsSelfTestLegs.Length)
        {
            physicsSelfTestTimer.Interval = PhysicsSelfTestLegDelay;
            physicsSelfTestTimer.Start();
            return;
        }
        // The matrix is finished; restore the environment-driven defaults so any manual toss
        // after it runs with the caller's intended configuration again.
        keepAnimationDuringFlight =
            Environment.GetEnvironmentVariable(PhysicsKeepAnimationEnvironmentVariable) != "0";
        physicsPaced60Enabled = physicsPaced60Default;
        physicsTimerResolutionEnabled = physicsTimerResolutionDefault;
        physicsPacerEnabled = physicsPacerDefault;
    }

    /// <summary>
    /// Appends one numeric summary per physics flight (toss/bounce). The flight frame cadence
    /// and the cost of the per-tick window move are the two numbers that tell whether the
    /// visible drop after a drag release comes from the render loop or from the move itself.
    /// </summary>
    private void WritePhysicsFlightDiagnostics()
    {
        if (physicsFlightStats.IsEmpty) return;
        var snapshot = physicsFlightStats.Snapshot();
        var durationMs = (Stopwatch.GetTimestamp() - physicsFlightStartTick) / (double)Stopwatch.Frequency * 1000d;
        var driver = physicsPacerEnabled
            ? "pacer"
            : physicsStepTargetSeconds > 0d
                ? "paced"
                : physicsTimerResolutionEnabled ? "timer" : "burst";
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "physics flight mode={0}+{1} refresh={2:F1} ticks={3} intervalMs={4:F2}/{5:F2}/{6:F2} slow={7} moveMs={8:F3}/{9:F3} durationMs={10:F0}",
            keepAnimationDuringFlight ? "hot" : "paused",
            driver,
            physicsDisplayRefreshHz,
            snapshot.TickCount,
            snapshot.IntervalMinMs,
            snapshot.IntervalAvgMs,
            snapshot.IntervalMaxMs,
            snapshot.SlowTickCount,
            snapshot.MoveAvgMs,
            snapshot.MoveMaxMs,
            durationMs);
        AppendPhysicsDiagnosticsLine("pet-helper-physics.log", line);
    }

    private static void AppendPhysicsDiagnosticsLine(string fileName, string line)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DshPngPet");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
            {
                File.WriteAllText(path, string.Empty);
            }
            File.AppendAllText(path, $"{DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never disturb pet rendering or lifecycle.
        }
    }

    private void CommitPhysicsPosition()
    {
        Left = physicsState.Position.X;
        Top = physicsState.Position.Y;
    }

    private Point CurrentPosition() =>
        dragging || !physicsEnabled || physicsState.IsResting
            ? new Point(Left, Top)
            : physicsState.Position;

    private void BeginDragPhysicsSampling()
    {
        PausePhysics();
        CommitPhysicsPosition();
        lastDragSamplePosition = CurrentPosition();
        lastDragSampleTick = Stopwatch.GetTimestamp();
        dragReleaseVelocity = new Vector();
    }

    private void RecordDragPhysicsSample()
    {
        if (!physicsEnabled) return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - lastDragSampleTick) / (double)Stopwatch.Frequency;
        var position = CurrentPosition();
        if (elapsed > 0.004d)
        {
            dragReleaseVelocity = new Vector(
                (position.X - lastDragSamplePosition.X) / elapsed,
                (position.Y - lastDragSamplePosition.Y) / elapsed);
            lastDragSamplePosition = position;
            lastDragSampleTick = now;
        }
    }

    private void LaunchAfterDrag()
    {
        if (!physicsEnabled || reducedMotion)
        {
            physicsState = PetPhysics.Rest(CurrentPosition());
            return;
        }
        physicsState = PetPhysics.Launch(CurrentPosition(), dragReleaseVelocity);
        ResumePhysicsIfEligible();
    }

    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;
        StateBubble.LayoutTransform = new ScaleTransform(state.Scale, state.Scale);
        UpdateStateBubblePosition();

        if (state.Left is { } left && state.Top is { } top)
        {
            if (!IsLoaded) WindowStartupLocation = WindowStartupLocation.Manual;
            var saved = new Rect(left, top, Width, Height);
            var workArea = PlacementPlanner.NearestWorkArea(saved, screenLayout.WorkAreas);
            var rect = PlacementPlanner.ClampIntoWorkAreaWithProtrusion(saved, workArea, PetProtrusion);
            Left = rect.X;
            Top = rect.Y;
            return;
        }

        if (!IsLoaded)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            var workArea = screenLayout.PrimaryWorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2d;
            Top = workArea.Top + (workArea.Height - Height) / 2d;
        }
    }

    private PetWindowState CurrentState()
    {
        var position = CurrentPosition();
        return PetWindowState.Normalize(position.X, position.Y, Width / PetWindowState.BaseSize);
    }

    private Rect CurrentRect()
    {
        var position = CurrentPosition();
        return new Rect(position.X, position.Y, ActualWidth, ActualHeight);
    }

    private Rect CurrentHeadRect()
    {
        var pet = CurrentRect();
        // The art has a large transparent shoulder/body area.  A third of the scaled pet
        // width produces a head-sized card height without coupling the card to a PNG frame.
        var height = Math.Clamp(pet.Width * 0.33d, 48d, 96d);
        return new Rect(
            pet.Left + (pet.Width - height) / 2d,
            pet.Top + Math.Max(0d, pet.Height * 0.08d),
            height,
            height);
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            pointerGesture.Cancel();
            if (PetLayout.IsMouseCaptured) PetLayout.ReleaseMouseCapture();
            CancelPendingPeakValleyCard();
            DismissRandomChatInvitation();
            PausePhysics();
            ToggleDialogueWindow();
            e.Handled = true;
            return;
        }

        pointerGesture.Begin(
            e.GetPosition(this),
            combinedDrag: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        PetLayout.CaptureMouse();
        PausePhysics();
        e.Handled = true;
    }

    private void Pet_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (pointerGesture.Move(
                e.GetPosition(this),
                SystemParameters.MinimumHorizontalDragDistance,
                SystemParameters.MinimumVerticalDragDistance) != PetPointerAction.StartDrag)
        {
            return;
        }

        var useCombinedDrag = pointerGesture.CombinedDrag;
        if (PetLayout.IsMouseCaptured) PetLayout.ReleaseMouseCapture();
        StartPetDrag(useCombinedDrag);
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var action = pointerGesture.Release();
        if (PetLayout.IsMouseCaptured) PetLayout.ReleaseMouseCapture();
        if (action == PetPointerAction.ShowPeakValleyCard)
        {
            SchedulePeakValleyCard();
        }
        ResumePhysicsIfEligible();
        e.Handled = true;
    }

    private void Pet_LostMouseCapture(object sender, MouseEventArgs e)
    {
        pointerGesture.Cancel();
        ResumePhysicsIfEligible();
    }

    private void ShowPeakValleyCard()
    {
        if (peakValleyCard is null || displayedRandomChatInvitationId is not null) return;
        var head = CurrentHeadRect();
        peakValleyCard.ShowPeriod(PeakValleySchedule.Current(), head, screenLayout, head.Height);
    }

    private void SchedulePeakValleyCard()
    {
        if (displayedRandomChatInvitationId is not null) return;
        singleClickTimer.Stop();
        singleClickTimer.Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime);
        singleClickTimer.Start();
    }

    private void CancelPendingPeakValleyCard() => singleClickTimer.Stop();

    private void StartPetDrag(bool useCombinedDrag)
    {
        CancelPendingPeakValleyCard();
        DismissRandomChatInvitation();
        combinedDrag = useCombinedDrag;
        dragStartPetRect = CurrentRect();
        lastMovedDialogueRect = null;
        if (combinedDrag && dialogueWindow is { IsVisible: true })
        {
            dragStartDialogueRect = new Rect(
                dialogueWindow.Left,
                dialogueWindow.Top,
                dialogueWindow.ActualWidth,
                dialogueWindow.ActualHeight);
        }

        dragging = true;
        BeginDragPhysicsSampling();
        animationPlayer.Pause();
        try
        {
            // Native OS-level window move (WM_NCLBUTTONDOWN / HTCAPTION): the window surface
            // is not redrawn while it follows the cursor, so the pet drags smoothly. It is
            // modal, so clamping happens after the drag ends (see Window_LocationChanged and
            // the finally block below).
            DragMove();
        }
        finally
        {
            pointerGesture.Cancel();
            dragging = false;
            ClampPetIntoProtrusion();
            if (combinedDrag && dialogueWindow is { IsVisible: true })
            {
                // Re-sync the dialogue's WPF state after the Win32 moves that followed the
                // pet during the drag, then persist it.
                if (lastMovedDialogueRect is { } rect)
                {
                    dialogueWindow.ApplyCombinedPosition(rect.X, rect.Y);
                }
                dialogueWindow.SaveState();
            }
            SaveState();
            LaunchAfterDrag();
            if (!physicsRendering) animationPlayer.Resume();
        }
    }

    /// <summary>
    /// Fired while DragMove() is running (and on every other position change). During a
    /// combined (Ctrl) drag the dialogue follows the pet's actual movement and is clamped
    /// fully on screen. The pet itself is never repositioned here: fighting the OS move loop
    /// would make it jitter.
    /// </summary>
    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (dragging) RecordDragPhysicsSample();
        if (!dragging || !combinedDrag || dialogueWindow is not { IsVisible: true }) return;

        var appliedDeltaX = Left - dragStartPetRect.X;
        var appliedDeltaY = Top - dragStartPetRect.Y;
        var dialogueTarget = new Rect(
            dragStartDialogueRect.X + appliedDeltaX,
            dragStartDialogueRect.Y + appliedDeltaY,
            dragStartDialogueRect.Width,
            dragStartDialogueRect.Height);
        var dialogueClamped = PlacementPlanner.ClampIntoWorkArea(
            dialogueTarget,
            screenLayout.WorkAreaFor(dialogueTarget));
        lastMovedDialogueRect = dialogueClamped;
        WindowMover.Move(dialogueWindow, dialogueClamped.X, dialogueClamped.Y);
    }

    /// <summary>
    /// After a native drag the pet may have been left off-screen; pull it back to its edge
    /// protrusion limit (the "dock at the screen edge" position).
    /// </summary>
    private void ClampPetIntoProtrusion()
    {
        var current = CurrentRect();
        var clamped = PlacementPlanner.ClampIntoWorkAreaWithProtrusion(
            current,
            screenLayout.WorkAreaFor(current),
            PetProtrusion);
        if (clamped.X != Left || clamped.Y != Top)
        {
            Left = clamped.X;
            Top = clamped.Y;
        }
    }

    private void PetLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        animationPlayer.RefreshPresentation();
        UpdateStateBubblePosition();
    }

    private void StateBubble_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateStateBubblePosition();

    private void StateBubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!StateBubbleCanOpenHarness(lastDisplayState.State)) return;
        e.Handled = true;
        HarnessOpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool StateBubbleCanOpenHarness(string state) =>
        state is "question" or "waiting";

    private void UpdateStateBubblePosition()
    {
        if (PetImage.ActualWidth <= 0d || PetImage.ActualHeight <= 0d ||
            StateBubble.ActualWidth <= 0d || StateBubble.ActualHeight <= 0d)
        {
            return;
        }

        var anchor = animationPlayer.StatusAnchor;
        var point = PetImage.TranslatePoint(
            new Point(PetImage.ActualWidth * anchor.X, PetImage.ActualHeight * anchor.Y),
            StateBubbleCanvas);
        var left = point.X - StateBubble.ActualWidth / 2d;
        var top = point.Y - StateBubble.ActualHeight - StatusBubbleGap;

        Canvas.SetLeft(StateBubble, Math.Clamp(left, 0d, Math.Max(0d, StateBubbleCanvas.ActualWidth - StateBubble.ActualWidth)));
        Canvas.SetTop(StateBubble, Math.Clamp(top, 0d, Math.Max(0d, StateBubbleCanvas.ActualHeight - StateBubble.ActualHeight)));
    }

    private void ScheduleRandomChatInvitation()
    {
        randomChatTimer.Stop();
        if (!randomChatEnabled || displayedRandomChatInvitationId is not null || dragging
            || lastDisplayState.State != "idle" || dialogueWindow is { IsVisible: true })
        {
            return;
        }
        randomChatTimer.Interval = RandomChatDelayFor(randomChatMinIntervalMinutes, randomChatMaxIntervalMinutes, Random.Shared.Next());
        randomChatTimer.Start();
    }

    internal static TimeSpan RandomChatDelayFor(int minimumMinutes, int maximumMinutes, int randomValue)
    {
        var minimum = Math.Clamp(minimumMinutes, 5, 1440);
        var maximum = Math.Clamp(maximumMinutes, minimum, 1440);
        var offset = Math.Abs((long)randomValue) % (maximum - minimum + 1L);
        return TimeSpan.FromMinutes(minimum + offset);
    }

    internal static IReadOnlyList<RandomChatPrompt> BuildRandomChatPrompts(ImmutableArray<string> customPrompts)
    {
        var prompts = RandomChatPromptCatalog.Load().ToList();
        prompts.AddRange(customPrompts.Select((text) => new RandomChatPrompt("discovery", text, "点击展开聊聊")));
        return prompts;
    }

    private void ShowRandomChatInvitation(bool force = false)
    {
        if ((!randomChatEnabled && !force) || lastDisplayState.State != "idle" || dialogueWindow is { IsVisible: true })
        {
            if (!force) ScheduleRandomChatInvitation();
            return;
        }
        nextRandomChatInvitationId++;
        displayedRandomChatInvitationId = nextRandomChatInvitationId;
        var prompt = randomChatPrompts[Random.Shared.Next(randomChatPrompts.Count)];
        displayedRandomChatTopic = prompt.Topic;
        var head = CurrentHeadRect();
        peakValleyCard?.ShowRandomChatInvitation(prompt.Text, prompt.Cta, head, screenLayout, head.Height);
        randomChatExpiryTimer.Stop();
        randomChatExpiryTimer.Interval = TimeSpan.FromSeconds(30);
        randomChatExpiryTimer.Start();
    }

    private void DismissRandomChatInvitation()
    {
        randomChatTimer.Stop();
        randomChatExpiryTimer.Stop();
        displayedRandomChatInvitationId = null;
        displayedRandomChatTopic = null;
        peakValleyCard?.Dismiss();
    }

    private void OpenDisplayedRandomChat()
    {
        if (displayedRandomChatInvitationId is not { } invitationId || displayedRandomChatTopic is not { } topic)
        {
            return;
        }
        randomChatTimer.Stop();
        randomChatExpiryTimer.Stop();
        RandomChatRequested?.Invoke(this, new RandomChatRequestedEventArgs(invitationId, topic));
    }

    private void ScaleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string scaleText }
            || !double.TryParse(scaleText, CultureInfo.InvariantCulture, out var scale))
        {
            return;
        }

        ApplyState(PetWindowState.Normalize(Left, Top, scale));
        SaveState();
    }

    private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyState(PetWindowState.Normalize(null, null, defaultScale));
        ApplyDefaultPlacement(defaultPetPlacement);
        hasSavedPlacement = true;
        physicsState = physicsEnabled ? PetPhysics.StartFalling(CurrentPosition()) : PetPhysics.Rest(CurrentPosition());
        ResumePhysicsIfEligible();
        SaveState();
    }

    private void HideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingPeakValleyCard();
        DismissRandomChatInvitation();
        PausePhysics();
        SaveState();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void SelectTargetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TargetSelectionRequested?.Invoke(this, null);
    }

    private void Pet_DragEnter(object sender, DragEventArgs e) => ApplyDropEffect(e);

    private void Pet_DragOver(object sender, DragEventArgs e) => ApplyDropEffect(e);

    private static void ApplyDropEffect(DragEventArgs e)
    {
        e.Effects = FirstDroppedDirectory(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Pet_Drop(object sender, DragEventArgs e)
    {
        var directory = FirstDroppedDirectory(e);
        if (directory is null) return;
        TargetSelectionRequested?.Invoke(this, directory);
    }

    private static string? FirstDroppedDirectory(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return null;
        }
        var first = paths.FirstOrDefault();
        return first is not null && Directory.Exists(first) ? first : null;
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class RandomChatRequestedEventArgs(long invitationId, string topic) : EventArgs
{
    public long InvitationId { get; } = invitationId;
    public string Topic { get; } = topic;
}
