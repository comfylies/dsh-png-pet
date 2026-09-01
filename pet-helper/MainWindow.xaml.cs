using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetHelper;

public partial class MainWindow : Window
{
    private const double StatusBubbleGap = 4d;

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

    public MainWindow(IScreenLayout screenLayout)
    {
        InitializeComponent();
        this.screenLayout = screenLayout;
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
        RestoreState();
        restoringState = false;
        Loaded += (_, _) => UpdateStateBubblePosition();
        Closed += (_, _) =>
        {
            animationPlayer.Stop();
            singleClickTimer.Stop();
            randomChatTimer.Stop();
            randomChatExpiryTimer.Stop();
            peakValleyCard?.CloseCard();
        };
    }

    public void AttachDialogueWindow(DialogueWindow window)
    {
        dialogueWindow = window;
        window.DialogueClosed += (_, _) => ScheduleRandomChatInvitation();
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
        }
        else
        {
            DismissRandomChatInvitation();
            dialogueWindow.ShowDialogue(CurrentRect());
        }
    }

    public void ApplyDisplayState(PetDisplayState state)
    {
        lastDisplayState = state;
        StateLabel.Text = state.Label;
        StateBubble.Visibility = state.State == "idle" ? Visibility.Collapsed : Visibility.Visible;
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
        SaveState();
    }

    public void ShowRandomChatDialogue(long invitationId)
    {
        if (displayedRandomChatInvitationId != invitationId) return;
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

    public void SaveState()
    {
        if (!restoringState)
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

    private PetWindowState CurrentState() =>
        PetWindowState.Normalize(Left, Top, Width / PetWindowState.BaseSize);

    private Rect CurrentRect() => new(Left, Top, ActualWidth, ActualHeight);

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
            ToggleDialogueWindow();
            e.Handled = true;
            return;
        }

        pointerGesture.Begin(
            e.GetPosition(this),
            combinedDrag: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        PetLayout.CaptureMouse();
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
        e.Handled = true;
    }

    private void Pet_LostMouseCapture(object sender, MouseEventArgs e) => pointerGesture.Cancel();

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
            animationPlayer.Resume();
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

    private void PetLayout_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateStateBubblePosition();

    private void StateBubble_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateStateBubblePosition();

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
        SaveState();
    }

    private void HideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingPeakValleyCard();
        DismissRandomChatInvitation();
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
