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

        // Always start collapsed. The configured default remains marked inside its workspace.
        ShowTargetTree();
    }

    private void ShowLoading()
    {
        LoadingView.Visibility = Visibility.Visible;
        ErrorView.Visibility = Visibility.Collapsed;
        TargetTreeView.Visibility = Visibility.Collapsed;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Visible;
        TargetTreeView.Visibility = Visibility.Collapsed;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        ErrorLabel.Text = message;
    }

    private void ShowTargetTree()
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        TargetTreeView.Visibility = Visibility.Visible;
        CreateView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        CardTitle.Text = "选择会话";

        WorkspaceTree.ItemsSource = workspaces.Select(workspace =>
        {
            var sessions = sessionsByWorkspace.TryGetValue(workspace.Id, out var listed) ? listed : [];
            return new WorkspaceRow(
                workspace,
                sessions.Select(session => new SessionRow(session, workspace.Id, IsCurrent(session.Id))).ToImmutableArray());
        }).ToArray();

        UngroupedExpander.IsExpanded = false;
        UngroupedExpander.Visibility = ungrouped.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UngroupedSessionList.ItemsSource = ungrouped
            .Select(session => new SessionRow(session, null, IsCurrent(session.Id)))
            .ToImmutableArray();
    }

    private bool IsCurrent(string sessionId) => sessionId == defaultSessionId;

    private void ShowCreateView()
    {
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        TargetTreeView.Visibility = Visibility.Collapsed;
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

    private void NestedSessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: SessionRow row } list) return;
        list.SelectedItem = null;
        AnswerSession(row.Session.Id, row.WorkspaceId, newBlank: false);
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e) =>
        AnswerSession(null, (sender as FrameworkElement)?.Tag as string, newBlank: true);

    private void CreateWorkspaceButton_Click(object sender, RoutedEventArgs e) => ShowCreateView();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (CreateView.Visibility == Visibility.Visible)
        {
            ShowTargetTree();
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
            if (target is Button or ToggleButton or Expander or TextBox or ListBox or ListBoxItem or ScrollViewer or ScrollBar) return true;
            target = target is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(target)
                : LogicalTreeHelper.GetParent(target);
        }
        return false;
    }

    private sealed class WorkspaceRow(TargetWorkspaceInfo workspace, ImmutableArray<SessionRow> sessions)
    {
        public TargetWorkspaceInfo Workspace { get; } = workspace;

        public ImmutableArray<SessionRow> Sessions { get; } = sessions;

        public int SessionCount => Sessions.Length;

        public bool IsExpanded { get; set; }
    }

    private sealed record SessionRow(TargetSessionInfo Session, string? WorkspaceId, bool IsCurrent)
    {
        public string DisplayTitle =>
            Session.Title.Length > 0
                ? (IsCurrent ? "✓ " : "") + Session.Title
                : Session.Blank ? "空白会话" : "未命名会话";
    }
}
