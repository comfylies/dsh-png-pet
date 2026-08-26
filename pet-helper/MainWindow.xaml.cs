using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PetHelper;

public partial class MainWindow : Window
{
    private readonly PetWindowStateStore stateStore = new();
    private readonly PetAnimationPlayer animationPlayer;
    private PetDisplayState lastDisplayState = new("idle", string.Empty, 0);
    private bool reducedMotion;
    private bool restoringState = true;
    private bool dragging;
    private DialogueWindow? dialogueWindow;

    public event EventHandler? HiddenToTray;

    public MainWindow()
    {
        InitializeComponent();
        animationPlayer = new PetAnimationPlayer(PetImage);
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion: false);
        RestoreState();
        restoringState = false;
        Closed += (_, _) => animationPlayer.Stop();
    }

    public void AttachDialogueWindow(DialogueWindow window)
    {
        dialogueWindow = window;
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
            dialogueWindow.FocusInput();
        }
    }

    public void ApplyDisplayState(PetDisplayState state)
    {
        lastDisplayState = state;
        StateLabel.Text = state.Label;
        StateBubble.Visibility = state.State == "idle" ? Visibility.Collapsed : Visibility.Visible;
        StateBubble.Background = state.State == "waiting"
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 142, 74, 29))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 43, 75, 95));
        animationPlayer.Apply(state.AnimationKey, reducedMotion);
    }

    public void ApplyConfig(ConfigMessage config)
    {
        reducedMotion = config.ReducedMotion;
        animationPlayer.Apply(lastDisplayState.AnimationKey, reducedMotion);
        ApplyState(PetWindowState.Normalize(Left, Top, config.Scale));
        SaveState();
    }

    private void RestoreState() => ApplyState(stateStore.Load());

    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;
        StateBubble.LayoutTransform = new ScaleTransform(state.Scale, state.Scale);

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
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleDialogueWindow();
            return;
        }
        if (e.ClickCount == 1)
        {
            dragging = true;
            try
            {
                DragMove();
            }
            finally
            {
                dragging = false;
            }
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        SaveState();
        if (dragging && dialogueWindow is { IsVisible: true })
        {
            dialogueWindow.Left = Left + Width + 8;
            dialogueWindow.Top = Top;
        }
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
        SaveState();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();
}
