using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PetHelper;

public partial class DialogueWindow : Window
{
    private static readonly Size MinSize = new(DialogueWindowState.MinWidth, DialogueWindowState.MinHeight);
    private static readonly Size MaxSize = new(DialogueWindowState.MaxWidth, DialogueWindowState.MaxHeight);

    private readonly IScreenLayout screenLayout;
    private readonly DialogueWindowStateStore stateStore = new();
    private readonly ConversationState conversationState = new(previewEnabled: false, previewMaxChars: 480);
    private long nextRequestId;
    private long historyRequestId;
    private bool restoringState = true;

    private bool inSystemDrag;
    private bool resizingWindow;
    private ResizeEdge activeResizeEdge;
    private System.Windows.Point dragStartCursor;
    private Rect dragStartRect;
    private Rect lastAppliedRect;

    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;
    public event EventHandler<HistoryRequestedEventArgs>? HistoryRequested;
    public event EventHandler? HiddenToTray;

    public DialogueWindow(IScreenLayout screenLayout)
    {
        InitializeComponent();
        this.screenLayout = screenLayout;
        RestoreState();
        restoringState = false;
    }

    /// <summary>
    /// Shows the dialogue at its saved position (corrected into a visible work area), or —
    /// on first run — smartly placed beside the pet. The pet's rect is only used for that
    /// first-run placement; afterwards the dialogue is fully independent.
    /// </summary>
    public void ShowDialogue(Rect petRect)
    {
        var saved = stateStore.Load();
        var size = new Size(saved.Width, saved.Height);
        Rect target;
        if (saved.Left is { } left && saved.Top is { } top)
        {
            target = PlacementPlanner.CorrectRestoredPosition(
                new Rect(left, top, size.Width, size.Height),
                screenLayout.WorkAreas,
                MinSize);
        }
        else
        {
            target = PlacementPlanner.PlaceBeside(
                petRect,
                screenLayout.WorkAreaFor(petRect),
                size,
                PlacementPlanner.PetGap);
        }

        Left = target.X;
        Top = target.Y;
        Width = target.Width;
        Height = target.Height;
        Show();
        Activate();
        InputTextBox.Focus();
    }

    /// <summary>Applies a position computed by the pet's combined (Ctrl) drag, clamped on screen.</summary>
    public void ApplyCombinedPosition(double left, double top)
    {
        var target = new Rect(left, top, ActualWidth, ActualHeight);
        var clamped = PlacementPlanner.ClampIntoWorkArea(target, screenLayout.WorkAreaFor(target));
        Left = clamped.X;
        Top = clamped.Y;
    }

    public void ApplyConversationMessage(ProtocolMessage message)
    {
        conversationState.Apply(message);
        ConversationStatusLabel.Text = conversationState.StatusText;
        ReplyTextBlock.Text = conversationState.ReplyPending && conversationState.ReplyText.Length == 0
            ? "正在生成回复…"
            : conversationState.ReplyText;
        if (message is HistoryMessage)
        {
            RenderHistory(conversationState);
        }
    }

    public void CloseToHidden()
    {
        SaveState();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    public void SaveState()
    {
        if (restoringState) return;
        stateStore.Save(new DialogueWindowState(Left, Top, ActualWidth, ActualHeight));
    }

    private void RestoreState()
    {
        var state = stateStore.Load();
        Width = state.Width;
        Height = state.Height;
        if (state.Left is { } left && state.Top is { } top)
        {
            Left = left;
            Top = top;
        }
    }

    private void SubmitInput()
    {
        var text = InputTextBox.Text.Trim();
        if (text.Length is 0 or > 2000) return;

        nextRequestId++;
        conversationState.BeginInput(nextRequestId);
        InputTextBox.Clear();
        ConversationStatusLabel.Text = "正在发送…";
        InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(nextRequestId, text));
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            SubmitInput();
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SubmitInput();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseToHidden();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveTarget(e.OriginalSource as DependencyObject)) return;

        var point = e.GetPosition(this);
        var edge = WindowResizeMath.HitTest(point, new Rect(0, 0, ActualWidth, ActualHeight));
        if (edge != ResizeEdge.None)
        {
            resizingWindow = true;
            activeResizeEdge = edge;
            dragStartCursor = Mouse.GetPosition(null);
            dragStartRect = CurrentRect();
            lastAppliedRect = dragStartRect;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Native OS-level window move (WM_NCLBUTTONDOWN / HTCAPTION): the window surface is
        // not redrawn while it follows the cursor, so dragging is smooth. It is modal, so
        // on-screen clamping happens after the drag ends.
        inSystemDrag = true;
        try
        {
            DragMove();
        }
        finally
        {
            inSystemDrag = false;
            ClampDialogueOnScreen();
            SaveState();
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (resizingWindow)
        {
            var current = Mouse.GetPosition(null);
            var delta = current - dragStartCursor;
            var probe = new Rect(
                dragStartRect.X + delta.X,
                dragStartRect.Y + delta.Y,
                dragStartRect.Width,
                dragStartRect.Height);
            var rect = WindowResizeMath.ResizeFrom(
                dragStartRect,
                delta,
                activeResizeEdge,
                MinSize,
                MaxSize,
                screenLayout.WorkAreaFor(probe),
                PlacementPlanner.ScreenMargin);
            lastAppliedRect = rect;
            WindowMover.MoveAndResize(this, rect);
            e.Handled = true;
            return;
        }

        if (!inSystemDrag)
        {
            UpdateHoverCursor(e);
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!resizingWindow) return;

        resizingWindow = false;
        activeResizeEdge = ResizeEdge.None;
        ReleaseMouseCapture();
        // Re-sync WPF's property state after the Win32 resize performed during the drag.
        ApplyRect(lastAppliedRect);
        SaveState();
    }

    /// <summary>Pulls the dialogue fully back on screen after a native drag.</summary>
    private void ClampDialogueOnScreen()
    {
        var current = CurrentRect();
        var clamped = PlacementPlanner.ClampIntoWorkArea(current, screenLayout.WorkAreaFor(current));
        if (clamped.X != Left || clamped.Y != Top)
        {
            Left = clamped.X;
            Top = clamped.Y;
        }
    }

    private void UpdateHoverCursor(MouseEventArgs e)
    {
        if (IsInteractiveTarget(e.OriginalSource as DependencyObject))
        {
            Cursor = Cursors.Arrow;
            return;
        }

        var point = e.GetPosition(this);
        var edge = WindowResizeMath.HitTest(point, new Rect(0, 0, ActualWidth, ActualHeight));
        Cursor = WindowResizeMath.ResizeCursor(edge);
    }

    private Rect CurrentRect() => new(Left, Top, ActualWidth, ActualHeight);

    private void ApplyRect(Rect rect)
    {
        Left = rect.X;
        Top = rect.Y;
        Width = rect.Width;
        Height = rect.Height;
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        historyRequestId++;
        HistoryRequested?.Invoke(this, new HistoryRequestedEventArgs(historyRequestId));
        HistoryPanel.Visibility = Visibility.Visible;
        HistoryStatus.Text = "加载中…";
        HistoryStatus.Visibility = Visibility.Visible;
        HistoryScroll.Visibility = Visibility.Collapsed;
        HistoryList.Children.Clear();
    }

    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPanel.Visibility = Visibility.Collapsed;
    }

    private void RenderHistory(ConversationState state)
    {
        HistoryList.Children.Clear();
        HistoryScroll.Visibility = Visibility.Collapsed;
        HistoryStatus.Visibility = Visibility.Visible;
        if (!state.HistoryAvailable)
        {
            HistoryStatus.Text = "会话不可用";
            return;
        }
        if (state.HistoryMessages.Length == 0)
        {
            HistoryStatus.Text = "暂无对话历史";
            return;
        }

        HistoryStatus.Visibility = Visibility.Collapsed;
        HistoryScroll.Visibility = Visibility.Visible;
        foreach (var item in state.HistoryMessages)
        {
            var isUser = item.Role == "user";
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 2, 0, 2),
                MaxWidth = 190,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, isUser ? (byte)46 : (byte)64, isUser ? (byte)92 : (byte)68, isUser ? (byte)122 : (byte)84)),
            };
            bubble.Child = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
            };
            HistoryList.Children.Add(bubble);
        }
    }

    private static bool IsInteractiveTarget(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is TextBox or Button or ScrollViewer or ScrollBar) return true;
            target = VisualTreeHelper.GetParent(target);
        }
        return false;
    }
}
