using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PetHelper;

public partial class DialogueWindow : Window
{
    private readonly DialogueWindowStateStore stateStore = new();
    private readonly ConversationState conversationState = new(previewEnabled: false, previewMaxChars: 480);
    private long nextRequestId;
    private long historyRequestId;
    private bool restoringState = true;

    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;
    public event EventHandler<HistoryRequestedEventArgs>? HistoryRequested;
    public event EventHandler? HiddenToTray;

    public DialogueWindow()
    {
        InitializeComponent();
        RestoreState();
        restoringState = false;
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

    public void FocusInput()
    {
        Show();
        Activate();
        InputTextBox.Focus();
    }

    public void SaveState()
    {
        if (restoringState) return;
        stateStore.Save(new DialogueWindowState(Left, Top, Width, Height));
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

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !IsInteractiveTarget(e.OriginalSource as DependencyObject))
        {
            DragMove();
        }
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
            if (target is TextBox or Button or ScrollViewer) return true;
            target = VisualTreeHelper.GetParent(target);
        }
        return false;
    }

    private void DialogueWindow_LocationChanged(object? sender, EventArgs e) => SaveState();
}
