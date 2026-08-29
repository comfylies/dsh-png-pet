using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
    private DialogueWindow? dialogueWindow;
    private PeakValleyCardWindow? peakValleyCard;
    private readonly PetPointerGesture pointerGesture = new();

    private bool dragging;
    private bool combinedDrag;
    private Rect dragStartPetRect;
    private Rect dragStartDialogueRect;
    private Rect? lastMovedDialogueRect;

    public event EventHandler? HiddenToTray;

    /// <summary>Raised when the user opens the target-selection card (menu or dropped folder).</summary>
    public event EventHandler<string?>? TargetSelectionRequested;

    public MainWindow(IScreenLayout screenLayout)
    {
        InitializeComponent();
        this.screenLayout = screenLayout;
        animationPlayer = new PetAnimationPlayer(PetImage);
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion: false);
        RestoreState();
        restoringState = false;
        Loaded += (_, _) => UpdateStateBubblePosition();
        Closed += (_, _) =>
        {
            animationPlayer.Stop();
            peakValleyCard?.CloseCard();
        };
    }

    public void AttachDialogueWindow(DialogueWindow window)
    {
        dialogueWindow = window;
    }

    public void AttachPeakValleyCard(PeakValleyCardWindow window)
    {
        peakValleyCard = window;
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
    }

    public void ApplyConfig(ConfigMessage config)
    {
        reducedMotion = config.ReducedMotion;
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion);
        ApplyState(PetWindowState.Normalize(Left, Top, config.Scale));
        peakValleyCard?.Dismiss();
        UpdateStateBubblePosition();
        SaveState();
    }

    public void SaveState()
    {
        if (!restoringState)
        {
            stateStore.Save(CurrentState());
        }
    }

    private void RestoreState() => ApplyState(stateStore.Load());

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
            if (IsMouseCaptured) ReleaseMouseCapture();
            peakValleyCard?.Dismiss();
            ToggleDialogueWindow();
            e.Handled = true;
            return;
        }

        pointerGesture.Begin(
            e.GetPosition(this),
            combinedDrag: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        CaptureMouse();
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
        if (IsMouseCaptured) ReleaseMouseCapture();
        StartPetDrag(useCombinedDrag);
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var action = pointerGesture.Release();
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (action == PetPointerAction.ShowPeakValleyCard)
        {
            ShowPeakValleyCard();
        }
        e.Handled = true;
    }

    private void Pet_LostMouseCapture(object sender, MouseEventArgs e) => pointerGesture.Cancel();

    private void ShowPeakValleyCard()
    {
        if (peakValleyCard is null) return;
        var head = CurrentHeadRect();
        peakValleyCard.ShowPeriod(PeakValleySchedule.Current(), head, screenLayout, head.Height);
    }

    private void StartPetDrag(bool useCombinedDrag)
    {
        peakValleyCard?.Dismiss();
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
        ApplyState(CurrentState().Reset());
        SaveState();
    }

    private void HideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        peakValleyCard?.Dismiss();
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
