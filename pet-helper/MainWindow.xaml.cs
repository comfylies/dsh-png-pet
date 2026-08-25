using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PetHelper;

public partial class MainWindow : Window
{
    private readonly PetWindowStateStore stateStore = new();
    private readonly ConversationState conversationState = new(previewEnabled: false, previewMaxChars: 480);
    private bool restoringState = true;
    private long nextRequestId;

    public event EventHandler? HiddenToTray;
    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;

    public MainWindow()
    {
        InitializeComponent();
        RestoreState();
        restoringState = false;
    }

    public void ApplyDisplayState(PetDisplayState state)
    {
        StateLabel.Text = state.Label;
        StateBubble.Visibility = state.State == "idle" ? Visibility.Collapsed : Visibility.Visible;
        StateBubble.Background = state.State == "waiting"
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 142, 74, 29))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 43, 75, 95));
    }

    public void ApplyConfig(ConfigMessage config)
    {
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
    }

    private void RestoreState() => ApplyState(stateStore.Load());

    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;

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

    private void ClearAndHideInput()
    {
        InputTextBox.Clear();
        conversationState.ClearLocalInput();
        PreviewText.Text = string.Empty;
        PreviewBubble.Visibility = Visibility.Collapsed;
        InputBubble.Visibility = Visibility.Collapsed;
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
