using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PetHelper;

public partial class DialogueWindow : Window
{
    private static readonly Size MinSize = new(DialogueWindowState.MinWidth, DialogueWindowState.MinHeight);

    private const int MaxPendingAttachments = 4;
    private const int MaxImageBytes = 2 * 1024 * 1024;

    private readonly IScreenLayout screenLayout;
    private readonly DialogueWindowStateStore stateStore = new();
    private readonly ConversationState conversationState = new(previewEnabled: true, previewMaxChars: 2000);
    private readonly ObservableCollection<DialogueMessage> messages = new();
    private readonly ObservableCollection<PendingAttachment> pendingAttachments = new();
    private readonly DispatcherTimer streamFlushTimer;
    private long nextRequestId;
    private long historyRequestId;
    private bool restoringState = true;
    private string? lastDefaultSessionId;
    private string petStatusText = string.Empty;
    private bool atBottom = true;

    private bool inSystemDrag;

    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;
    public event EventHandler<HistoryRequestedEventArgs>? HistoryRequested;
    public event EventHandler<StopRequestedEventArgs>? StopRequested;
    public event EventHandler? HiddenToTray;

    public DialogueWindow(IScreenLayout screenLayout)
    {
        InitializeComponent();
        this.screenLayout = screenLayout;
        RestoreState();
        restoringState = false;
        UpdateMaxSize();
        MessageList.ItemsSource = messages;
        PendingAttachmentsList.ItemsSource = pendingAttachments;
        streamFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        streamFlushTimer.Tick += StreamFlushTick;
        streamFlushTimer.Start();
    }

    private void DialogueWindow_LocationChanged(object? sender, EventArgs e) => UpdateMaxSize();

    private void DialogueWindow_SizeChanged(object? sender, SizeChangedEventArgs e) => UpdateMaxSize();

    /// <summary>
    /// Drives the runtime maximum size from the work area the window currently sits in. A
    /// fixed small MaxWidth/MaxHeight fights Windows Aero Snap: snapping to a half screen
    /// asks for a width the window cannot take, so the OS believes the window is snapped
    /// while its size never matched — afterwards the window cannot be dragged or resized.
    /// Capping at (almost) the work area lets every snap complete; the window still cannot
    /// exceed the screen. Re-evaluated on every move/resize so snapping to another monitor
    /// or dragging across displays keeps a usable cap.
    /// </summary>
    private void UpdateMaxSize()
    {
        var max = DialogueWindowState.MaxSizeFor(screenLayout.WorkAreaFor(CurrentRect()));
        if (Math.Abs(MaxWidth - max.Width) < 0.5 && Math.Abs(MaxHeight - max.Height) < 0.5)
        {
            return;
        }
        MaxWidth = max.Width;
        MaxHeight = max.Height;
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
        if (lastDefaultSessionId is not null)
        {
            RequestHistory();
        }
        Dispatcher.BeginInvoke(MessageScroll.ScrollToEnd);
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
        var sessionChanged = message is ConversationConfigMessage config && config.DefaultSessionId != lastDefaultSessionId;
        conversationState.Apply(message);
        if (message is ConversationConfigMessage configMessage)
        {
            lastDefaultSessionId = configMessage.DefaultSessionId;
            if (sessionChanged)
            {
                messages.Clear();
                pendingAttachments.Clear();
                RefreshPendingAttachments();
                RequestHistory();
            }
        }

        SyncMessages();
        if (message is HistoryMessage or ReplyMessage or ReplyPreviewMessage { Completed: true } or InputStatusMessage)
        {
            EnsureAllMarkdownRendered();
        }
        UpdateStatusLabel();
        UpdateSendButton();
    }

    /// <summary>Pet bubble state, shown in the dialogue status line when no conversation is active (同源联动).</summary>
    public void ApplyPetState(StateMessage state)
    {
        petStatusText = state.State switch
        {
            "idle" => string.Empty,
            "waiting" => "等待你的操作",
            "success" => "已完成",
            "error" => "发生错误",
            "active" => state.Label,
            _ => string.Empty,
        };
        UpdateStatusLabel();
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

    private void RequestHistory()
    {
        if (lastDefaultSessionId is null) return;
        historyRequestId++;
        HistoryRequested?.Invoke(this, new HistoryRequestedEventArgs(historyRequestId));
    }

    private void SubmitInput()
    {
        var text = InputTextBox.Text.Trim();
        if (text.Length > 2000) return;
        if (text.Length == 0 && pendingAttachments.Count == 0) return;

        var attachments = pendingAttachments
            .Select(attachment => attachment.IsImage
                ? (InputAttachment)new ImageInputAttachment(attachment.MediaType!, attachment.Base64!, attachment.Name)
                : new FileInputAttachment(attachment.Path!, attachment.Name))
            .ToImmutableArray();

        nextRequestId++;
        var images = pendingAttachments.Where(attachment => attachment.IsImage)
            .Select(attachment => new DialogueImage(attachment.Name, null, null, attachment.Base64))
            .ToImmutableArray();
        var files = pendingAttachments.Where(attachment => !attachment.IsImage)
            .Select(attachment => new DialogueFile(attachment.Name, attachment.Path!))
            .ToImmutableArray();

        conversationState.BeginInput(nextRequestId, text, images, files);
        InputTextBox.Clear();
        pendingAttachments.Clear();
        RefreshPendingAttachments();
        SyncMessages();
        UpdateStatusLabel();
        UpdateSendButton();
        InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(nextRequestId, text, attachments));
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (conversationState.HasActiveTurn)
        {
            var requestId = conversationState.RequestId;
            if (requestId > 0)
            {
                StopRequested?.Invoke(this, new StopRequestedEventArgs(requestId));
            }
            return;
        }
        SubmitInput();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            SubmitInput();
            e.Handled = true;
        }
    }

    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        InputHint.Visibility = InputTextBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseToHidden();

    private void AttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图片或文件（可多选）",
            Multiselect = true,
            Filter = "图片或文件|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        AddAttachments(dialog.FileNames);
    }

    private void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (pendingAttachments.Count >= MaxPendingAttachments)
            {
                SetTemporaryStatus($"最多 {MaxPendingAttachments} 个附件");
                break;
            }
            var name = Path.GetFileName(path);
            if (IsImagePath(path))
            {
                if (!TryReadImage(path, out var base64, out var mediaType))
                {
                    SetTemporaryStatus("图片过大或无法读取，已跳过");
                    continue;
                }
                pendingAttachments.Add(new PendingAttachment { Name = name, Base64 = base64, MediaType = mediaType });
            }
            else
            {
                pendingAttachments.Add(new PendingAttachment { Name = name, Path = path });
            }
        }
        RefreshPendingAttachments();
    }

    private void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PendingAttachment attachment)
        {
            pendingAttachments.Remove(attachment);
            RefreshPendingAttachments();
        }
    }

    private void RefreshPendingAttachments()
    {
        PendingAttachmentsList.Visibility = pendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsImagePath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    private static bool TryReadImage(string path, out string base64, out string mediaType)
    {
        base64 = string.Empty;
        mediaType = string.Empty;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return false;
            mediaType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => string.Empty,
            };
            if (mediaType.Length == 0) return false;
            base64 = Convert.ToBase64String(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e) => ApplyFileDropEffect(e);

    private void Window_DragOver(object sender, DragEventArgs e) => ApplyFileDropEffect(e);

    private static void ApplyFileDropEffect(DragEventArgs e)
    {
        e.Effects = HasFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!HasFileDrop(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }
        AddAttachments(paths);
        e.Handled = true;
    }

    private static bool HasFileDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
        && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 };

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard contention is best effort.
        }
    }

    private void MarkdownHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox { DataContext: DialogueMessage { ShowMarkdown: true } message } host)
        {
            return;
        }
        RenderMarkdown(host, message);
    }

    private void EnsureAllMarkdownRendered()
    {
        foreach (var message in messages)
        {
            EnsureMarkdownRendered(message);
        }
    }

    private void EnsureMarkdownRendered(DialogueMessage message)
    {
        if (!message.ShowMarkdown) return;
        if (MessageList.ItemContainerGenerator.ContainerFromItem(message) is not ContentPresenter container)
        {
            return;
        }
        var host = FindDescendantRichTextBox(container);
        if (host is not null)
        {
            RenderMarkdown(host, message);
        }
    }

    private static RichTextBox? FindDescendantRichTextBox(DependencyObject root)
    {
        if (root is RichTextBox richTextBox) return richTextBox;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindDescendantRichTextBox(VisualTreeHelper.GetChild(root, index));
            if (found is not null) return found;
        }
        return null;
    }

    private static void RenderMarkdown(RichTextBox host, DialogueMessage message)
    {
        // A fresh RichTextBox already owns a FlowDocument with one empty Paragraph, so a
        // "has blocks" guard would wrongly skip every render. Mark the host once instead.
        if (ReferenceEquals(host.Tag, message)) return;
        try
        {
            host.Document = MarkdownRenderer.Render(message.Text, CopyText);
            host.Tag = message;
        }
        catch
        {
            // A malformed markdown must never break the dialogue.
        }
    }

    private void StreamFlushTick(object? sender, EventArgs e)
    {
        var pending = conversationState.PendingStreamText;
        if (pending is null) return;
        // The buffered stream belongs to the live assistant message; a terminal ending
        // (stopped/interrupted/failed) must still receive the partial text.
        var message = conversationState.Messages.LastOrDefault(candidate =>
            candidate.Role == "assistant" && (candidate.Streaming || candidate.End != MessageEndState.None));
        if (message is null) return;
        var becomesMarkdown = !message.ShowMarkdown;
        message.Text = pending;
        if (becomesMarkdown && message.ShowMarkdown)
        {
            EnsureMarkdownRendered(message);
        }
        if (atBottom)
        {
            MessageScroll.ScrollToEnd();
        }
    }

    private void MessageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0 && atBottom)
        {
            MessageScroll.ScrollToEnd();
        }
        atBottom = MessageScroll.VerticalOffset + MessageScroll.ViewportHeight >= MessageScroll.ExtentHeight - 4;
    }

    private void SyncMessages()
    {
        var source = conversationState.Messages;
        var index = 0;
        while (index < source.Length)
        {
            if (index < messages.Count)
            {
                if (!ReferenceEquals(messages[index], source[index]))
                {
                    messages[index] = source[index];
                }
            }
            else
            {
                messages.Add(source[index]);
            }
            index++;
        }
        while (messages.Count > source.Length)
        {
            messages.RemoveAt(messages.Count - 1);
        }
    }

    private void UpdateStatusLabel()
    {
        var text = conversationState.StatusText;
        if (text.Length == 0)
        {
            if (conversationState.HasStreamingMessage)
            {
                text = "生成中…";
            }
            else if (conversationState.HasActiveTurn)
            {
                text = "思考中…";
            }
            else
            {
                text = petStatusText;
            }
        }
        ConversationStatusLabel.Text = text;
    }

    private void UpdateSendButton()
    {
        if (conversationState.HasActiveTurn)
        {
            SendGlyph.Data = Geometry.Parse("M 4,4 L 12,4 L 12,12 L 4,12 Z");
            SendButton.ToolTip = "停止生成";
        }
        else
        {
            SendGlyph.Data = Geometry.Parse("M 8,1 L 1,9 L 5.5,9 L 5.5,15 L 10.5,15 L 10.5,9 L 15,9 Z");
            SendButton.ToolTip = "发送（Enter）";
        }
    }

    private void SetTemporaryStatus(string text)
    {
        ConversationStatusLabel.Text = text;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveTarget(e.OriginalSource as DependencyObject)) return;

        var point = e.GetPosition(this);
        var edge = WindowResizeMath.HitTest(point, new Rect(0, 0, ActualWidth, ActualHeight));
        if (edge != ResizeEdge.None)
        {
            inSystemDrag = true;
            try
            {
                WindowMover.BeginNativeResize(this, edge);
            }
            finally
            {
                inSystemDrag = false;
                ClampDialogueOnScreen();
                SaveState();
            }
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
        if (!inSystemDrag)
        {
            UpdateHoverCursor(e);
        }
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

    internal static bool IsInteractiveTarget(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is TextBox or Button or ScrollViewer or ScrollBar or RichTextBox or ListBox or ItemsControl)
            {
                return true;
            }
            target = ParentOf(target);
        }
        return false;
    }

    /// <summary>
    /// Walks the parent chain across both Visuals and ContentElements: the markdown host's
    /// OriginalSource can be a FlowDocument Paragraph/Run (a ContentElement), which
    /// VisualTreeHelper.GetParent rejects.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject target) =>
        target is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(target)
            : LogicalTreeHelper.GetParent(target);

    /// <summary>One pending attachment chip: an image (base64 kept for the thumbnail echo) or a file path.</summary>
    private sealed class PendingAttachment
    {
        public required string Name { get; init; }

        public string? Base64 { get; init; }

        public string? MediaType { get; init; }

        public string? Path { get; init; }

        public bool IsImage => Base64 is not null;
    }
}
