using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PetHelper;

public partial class MainWindow : Window
{
    private static readonly Thickness InputBubbleMargin = new(8d, 4d, 8d, 0d);
    private static readonly Thickness StateBubbleMargin = new(10d, 106d, 10d, 0d);
    private static readonly Thickness HistoryPanelMargin = new(8d, 8d, 8d, 8d);
    private readonly PetWindowStateStore stateStore = new();
    private readonly ConversationState conversationState = new(previewEnabled: false, previewMaxChars: 480);
    private readonly PetAnimationPlayer animationPlayer;
    private PetDisplayState lastDisplayState = new("idle", string.Empty, 0);
    private bool reducedMotion;
    private bool restoringState = true;
    private long nextRequestId;
    private long historyRequestId;

    public event EventHandler? HiddenToTray;
    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;
    public event EventHandler<HistoryRequestedEventArgs>? HistoryRequested;

    public MainWindow()
    {
        InitializeComponent();
        animationPlayer = new PetAnimationPlayer(PetImage);
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion: false);
        RestoreState();
        restoringState = false;
        Closed += (_, _) => animationPlayer.Stop();
    }

    public void ApplyDisplayState(PetDisplayState state)
    {
        lastDisplayState = state;
        StateLabel.Text = state.Label;
        StateBubble.Background = state.State == "waiting"
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 142, 74, 29))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 43, 75, 95));
        UpdateStateBubbleVisibility();
        animationPlayer.Apply(state.AnimationKey, reducedMotion);
    }

    public void ApplyConfig(ConfigMessage config)
    {
        reducedMotion = config.ReducedMotion;
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion);
        ApplyState(PetWindowState.Normalize(Left, Top, config.Scale));
        SaveState();
    }

    public void ApplyConversationMessage(ProtocolMessage message)
    {
        conversationState.Apply(message);
        ConversationStatusLabel.Text = conversationState.StatusText;
        PreviewText.Text = conversationState.PreviewText;
        PreviewBubble.Visibility = conversationState.PreviewText.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (conversationState.PreviewText.Length != 0)
        {
            InputBubble.Visibility = Visibility.Collapsed;
        }
        ReplyTextBlock.Text = conversationState.ReplyPending && conversationState.ReplyText.Length == 0
            ? "正在生成回复…"
            : conversationState.ReplyText;
        ReplyBubble.Visibility = conversationState.ReplyPending || conversationState.ReplyText.Length != 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (message is HistoryMessage)
        {
            RenderHistory(conversationState);
        }
        UpdateStateBubbleVisibility();
    }

    private void RestoreState() => ApplyState(stateStore.Load());

    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;
        var scale = state.Scale;
        var bubbleScale = new ScaleTransform(scale, scale);
        DialogueStack.LayoutTransform = bubbleScale;
        PreviewBubble.LayoutTransform = bubbleScale;
        StateBubble.LayoutTransform = bubbleScale;
        HistoryPanel.LayoutTransform = bubbleScale;
        DialogueStack.Margin = ScaleMargin(InputBubbleMargin, scale);
        PreviewBubble.Margin = ScaleMargin(InputBubbleMargin, scale);
        StateBubble.Margin = ScaleMargin(StateBubbleMargin, scale);
        HistoryPanel.Margin = ScaleMargin(HistoryPanelMargin, scale);

        if (state.Left is { } left && state.Top is { } top)
        {
            if (!IsLoaded) WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            return;
        }

        if (!IsLoaded)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2d;
            Top = workArea.Top + (workArea.Height - Height) / 2d;
        }
    }

    private static Thickness ScaleMargin(Thickness margin, double scale) =>
        new(margin.Left * scale, margin.Top * scale, margin.Right * scale, margin.Bottom * scale);

    /// <summary>The state bubble hides while an input or preview bubble is open so scaled layouts never overlap.</summary>
    private void UpdateStateBubbleVisibility() =>
        StateBubble.Visibility = lastDisplayState.State == "idle"
            || InputBubble.Visibility == Visibility.Visible
            || ReplyBubble.Visibility == Visibility.Visible
            || PreviewBubble.Visibility == Visibility.Visible
            || HistoryPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private PetWindowState CurrentState() =>
        PetWindowState.Normalize(Left, Top, Width / PetWindowState.BaseSize);

    private void SaveState()
    {
        if (!restoringState)
        {
            stateStore.Save(CurrentState());
        }
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !IsInteractiveTarget(e.OriginalSource as DependencyObject))
        {
            ShowInputBubble();
            DragMove();
        }
    }

    private void ShowInputBubble()
    {
        InputBubble.Visibility = Visibility.Visible;
        PreviewBubble.Visibility = Visibility.Collapsed;
        UpdateStateBubbleVisibility();
        InputTextBox.Focus();
    }

    private void SubmitInput()
    {
        var text = InputTextBox.Text.Trim();
        if (text.Length is 0 or > 2000)
        {
            return;
        }

        nextRequestId++;
        conversationState.BeginInput(nextRequestId);
        InputTextBox.Clear();
        PreviewText.Text = string.Empty;
        PreviewBubble.Visibility = Visibility.Collapsed;
        ConversationStatusLabel.Text = "正在发送…";
        InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(nextRequestId, text));
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearAndHideInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            SubmitInput();
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SubmitInput();

    private void CloseInputButton_Click(object sender, RoutedEventArgs e) => ClearAndHideInput();

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        historyRequestId++;
        HistoryRequested?.Invoke(this, new HistoryRequestedEventArgs(historyRequestId));
        HistoryPanel.Visibility = Visibility.Visible;
        HistoryStatus.Text = "加载中…";
        HistoryList.Children.Clear();
        HistoryStatus.Visibility = Visibility.Visible;
        HistoryScroll.Visibility = Visibility.Collapsed;
        UpdateStateBubbleVisibility();
    }

    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPanel.Visibility = Visibility.Collapsed;
        UpdateStateBubbleVisibility();
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
                MaxWidth = 150,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, isUser ? (byte)46 : (byte)64, isUser ? (byte)92 : (byte)68, isUser ? (byte)122 : (byte)84)),
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

    private void ClearAndHideInput()
    {
        InputTextBox.Clear();
        conversationState.ClearLocalInput();
        PreviewText.Text = string.Empty;
        PreviewBubble.Visibility = Visibility.Collapsed;
        InputBubble.Visibility = Visibility.Collapsed;
        UpdateStateBubbleVisibility();
    }

    private static bool IsInteractiveTarget(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is TextBox or Button or ScrollViewer)
            {
                return true;
            }

            target = VisualTreeHelper.GetParent(target);
        }

        return false;
    }

    private void Window_LocationChanged(object? sender, EventArgs e) => SaveState();

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
        SaveState();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class InputSubmittedEventArgs(long requestId, string text) : EventArgs
{
    public long RequestId { get; } = requestId;
    public string Text { get; } = text;
}

public sealed class HistoryRequestedEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}
