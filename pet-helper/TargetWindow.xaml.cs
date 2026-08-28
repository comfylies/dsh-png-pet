using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PetHelper;

public sealed class TargetOpenEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}

public sealed class TargetAnswerEventArgs(
    long requestId,
    string? sessionId,
    string? workspaceId,
    bool newBlank,
    string? path,
    bool newWorkspace) : EventArgs
{
    public long RequestId { get; } = requestId;
    public string? SessionId { get; } = sessionId;
    public string? WorkspaceId { get; } = workspaceId;
    public bool NewBlank { get; } = newBlank;
    public string? Path { get; } = path;
    public bool NewWorkspace { get; } = newWorkspace;
}

/// <summary>
/// Target-selection card: workspace list (level 1) → session list (level 2) with a
/// create-workspace flow (level 3). Selecting a session answers immediately; closing
/// (×, timeout, or the card losing its request) never persists anything.
/// </summary>
public partial class TargetWindow : Window
{
    private const int TimeoutSeconds = 60;

    private readonly DispatcherTimer timeoutTimer;
    private long nextOpenRequestId;
    private long currentRequestId;
    private bool dataReady;
    private string? pendingWorkspacePath;
    private string? selectedWorkspaceId;
    private bool viewingUngrouped;
    private ImmutableArray<TargetWorkspaceInfo> workspaces = [];
    private ImmutableDictionary<string, ImmutableArray<TargetSessionInfo>> sessionsByWorkspace =
        ImmutableDictionary<string, ImmutableArray<TargetSessionInfo>>.Empty;
    private ImmutableArray<TargetSessionInfo> ungrouped = [];
    private string? defaultWorkspaceId;
    private string? defaultSessionId;

    public event EventHandler<TargetOpenEventArgs>? TargetOpenRequested;
    public event EventHandler<TargetAnswerEventArgs>? TargetAnswered;

    public TargetWindow()
    {
        InitializeComponent();
        timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TimeoutSeconds) };
        timeoutTimer.Tick += (_, _) =>
        {
            timeoutTimer.Stop();
            CloseCard();
        };
    }

    /// <summary>Shows the card, refreshes its data, and starts the idle timeout.</summary>
    public void ShowCard(string? droppedDirectory = null)
    {
        pendingWorkspacePath = droppedDirectory;
        ShowLoading();
        Show();
        Activate();
        nextOpenRequestId++;
        TargetOpenRequested?.Invoke(this, new TargetOpenEventArgs(nextOpenRequestId));
        RestartTimeout();
    }

    public void ApplyTargetRequest(TargetRequestMessage message)
    {
        currentRequestId = message.RequestId;
        RestartTimeout();

        if (message.Error is { } error)
        {
            dataReady = false;
            ShowError(error);
            return;
        }

        workspaces = message.Workspaces;
        sessionsByWorkspace = message.SessionsByWorkspace;
        ungrouped = message.Ungrouped;
        defaultWorkspaceId = message.DefaultWorkspaceId;
        defaultSessionId = message.DefaultSessionId;
        dataReady = true;

        // A directory was dropped (or otherwise submitted): register it now; the follow-up
        // target-request lands us directly in that workspace's session list.
        if (pendingWorkspacePath is { } path)
        {
            pendingWorkspacePath = null;
            AnswerNewWorkspace(path);
            return;
        }

        // Default landing: the configured workspace's sessions when it still exists.
        if (defaultWorkspaceId is { } workspaceId && workspaces.Any((workspace) => workspace.Id == workspaceId))
        {
            ShowLevelTwo(workspaceId, viewingUngrouped: false);
            return;
        }

        ShowLevelOne();
    }

    private void ShowLoading()
    {
        LoadingView.Visibility = Visibility.Visible;
        ErrorView.Visibility = Visibility.Collapsed;
        LevelOneView.Visibility = Visibility.Collapsed;
        LevelTwoView.Visibility = Visibility.Collapsed;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Visible;
        LevelOneView.Visibility = Visibility.Collapsed;
        LevelTwoView.Visibility = Visibility.Collapsed;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        ErrorLabel.Text = message;
    }

    private void ShowLevelOne()
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        LevelOneView.Visibility = Visibility.Visible;
        LevelTwoView.Visibility = Visibility.Collapsed;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        CardTitle.Text = "目标选择";

        UngroupedButton.Visibility = ungrouped.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceList.SelectionChanged -= WorkspaceList_SelectionChanged;
        WorkspaceList.Items.Clear();
        foreach (var workspace in workspaces)
        {
            WorkspaceList.Items.Add(new WorkspaceRow(workspace));
        }
        WorkspaceList.SelectionChanged += WorkspaceList_SelectionChanged;
    }

    private void ShowLevelTwo(string workspaceId, bool viewingUngrouped)
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        LevelOneView.Visibility = Visibility.Collapsed;
        LevelTwoView.Visibility = Visibility.Visible;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;

        this.viewingUngrouped = viewingUngrouped;
        selectedWorkspaceId = viewingUngrouped ? null : workspaceId;

        var sessions = viewingUngrouped
            ? ungrouped
            : sessionsByWorkspace.TryGetValue(workspaceId, out var listed) ? listed : [];

        if (viewingUngrouped)
        {
            CardTitle.Text = "未分组会话";
            SessionBucketLabel.Text = "未分组会话";
        }
        else
        {
            var workspace = workspaces.FirstOrDefault((entry) => entry.Id == workspaceId);
            CardTitle.Text = workspace?.Title ?? "会话";
            SessionBucketLabel.Text = $"“{workspace?.Title ?? "未命名"}”下的会话";
        }

        SessionList.SelectionChanged -= SessionList_SelectionChanged;
        SessionList.Items.Clear();
        foreach (var session in sessions)
        {
            var row = new SessionRow(session, IsCurrent(session.Id));
            SessionList.Items.Add(row);
            if (row.IsCurrent) SessionList.SelectedItem = row;
        }
        SessionList.SelectionChanged += SessionList_SelectionChanged;
    }

    private bool IsCurrent(string sessionId) => sessionId == defaultSessionId;

    private void ShowCreateView()
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        LevelOneView.Visibility = Visibility.Collapsed;
        LevelTwoView.Visibility = Visibility.Collapsed;
        CreateView.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        CardTitle.Text = "新建工作区";
    }

    private void AnswerSession(string? sessionId, string? workspaceId, bool newBlank)
    {
        if (!dataReady) return;
        timeoutTimer.Stop();
        TargetAnswered?.Invoke(this, new TargetAnswerEventArgs(currentRequestId, sessionId, workspaceId, newBlank, null, false));
        Hide();
    }

    private void AnswerNewWorkspace(string path)
    {
        if (!dataReady) return;
        timeoutTimer.Stop();
        TargetAnswered?.Invoke(this, new TargetAnswerEventArgs(currentRequestId, null, null, false, path, true));
        // Stay visible: the follow-up target-request lands in the new workspace's sessions.
        ShowLoading();
    }

    private void RestartTimeout()
    {
        timeoutTimer.Stop();
        timeoutTimer.Start();
    }

    private void CloseCard()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceList.SelectedItem is not WorkspaceRow row) return;
        WorkspaceList.SelectedItem = null;
        ShowLevelTwo(row.Workspace.Id, viewingUngrouped: false);
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row) return;
        SessionList.SelectedItem = null;
        AnswerSession(row.Session.Id, selectedWorkspaceId, newBlank: false);
    }

    private void UngroupedButton_Click(object sender, RoutedEventArgs e) => ShowLevelTwo(string.Empty, viewingUngrouped: true);

    private void NewSessionButton_Click(object sender, RoutedEventArgs e) => AnswerSession(null, selectedWorkspaceId, newBlank: true);

    private void CreateWorkspaceButton_Click(object sender, RoutedEventArgs e) => ShowCreateView();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (CreateView.Visibility == Visibility.Visible)
        {
            ShowLevelOne();
        }
        else if (LevelTwoView.Visibility == Visibility.Visible)
        {
            ShowLevelOne();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseCard();

    private void RetryButton_Click(object sender, RoutedEventArgs e) => ShowCard(null);

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择工作区目录",
            Multiselect = false,
        };
        if (workspaces.Length > 0)
        {
            dialog.InitialDirectory = workspaces[0].Path;
        }

        // The system picker only ever returns an existing directory.
        if (dialog.ShowDialog(this) == true)
        {
            SubmitWorkspacePath(dialog.FolderName);
        }
    }

    private void SubmitWorkspacePath(string path)
    {
        pendingWorkspacePath = path;
        AnswerNewWorkspace(path);
    }

    private void TargetWindow_DragEnter(object sender, DragEventArgs e) => ApplyDragEffect(e);

    private void TargetWindow_DragOver(object sender, DragEventArgs e) => ApplyDragEffect(e);

    private static void ApplyDragEffect(DragEventArgs e)
    {
        e.Effects = HasDroppedDirectory(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TargetWindow_Drop(object sender, DragEventArgs e)
    {
        var directory = FirstDroppedDirectory(e);
        // DragOver already refused non-directory drops (Effects.None), so this is a folder.
        if (directory is null) return;

        if (!IsVisible)
        {
            ShowCard(directory);
            return;
        }

        SubmitWorkspacePath(directory);
    }

    private static bool HasDroppedDirectory(DragEventArgs e) => FirstDroppedDirectory(e) is not null;

    private static string? FirstDroppedDirectory(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return null;
        }
        var first = paths.FirstOrDefault();
        return first is not null && Directory.Exists(first) ? first : null;
    }

    private void TargetWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveTarget(e.OriginalSource as DependencyObject)) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove requires a pressed left button; ignore stray events.
        }
    }

    private static bool IsInteractiveTarget(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is Button or TextBox or ListBox or ListBoxItem or ScrollViewer or ScrollBar) return true;
            target = target is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(target)
                : LogicalTreeHelper.GetParent(target);
        }
        return false;
    }

    private sealed record WorkspaceRow(TargetWorkspaceInfo Workspace)
    {
        public override string ToString() => Workspace.Title;
    }

    private sealed record SessionRow(TargetSessionInfo Session, bool IsCurrent)
    {
        public override string ToString() =>
            Session.Title.Length > 0
                ? (IsCurrent ? "✓ " : "") + Session.Title
                : Session.Blank ? "空白会话" : "未命名会话";
    }
}
