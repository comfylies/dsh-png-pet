using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetHelper;

public partial class MainWindow : Window
{
    private readonly PetWindowStateStore stateStore = new();
    private bool restoringState = true;

    public event EventHandler? HiddenToTray;

    public MainWindow()
    {
        InitializeComponent();
        RestoreState();
        restoringState = false;
    }

    private void RestoreState() => ApplyState(stateStore.Load());

    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;

        if (state.Left is { } left && state.Top is { } top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (IsLoaded)
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
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
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
