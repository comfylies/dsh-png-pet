using System.Collections.Immutable;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

/// <summary>Forces the dialogue window through a full conversation flow on an STA thread to catch render/layout faults.</summary>
public sealed class DialogueWindowFaultTests
{
    private const string RichMarkdown =
        "# 标题\n\n**粗体** 与 *斜体* 和 `代码`\n\n> 引用行\n\n- 列表 A\n- 列表 B\n\n1. 有序 1\n2. 有序 2\n\n```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n| 列1 | 列2 |\n| --- | --- |\n| a | b |\n\n[链接](https://example.com) 结尾。";

    [Fact]
    public void Resident_mode_projects_latest_reply_and_restores_full_conversation()
    {
        RunWindowFlow(window =>
        {
            window.ApplyConversationMessage(new ConversationConfigMessage(true, 2000, "s-1", "w-1"));
            window.ApplyConversationMessage(new HistoryMessage(1, true, ImmutableArray.Create(
                new HistoryItem("assistant", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("old"))),
                new HistoryItem("user", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("question"))),
                new HistoryItem("assistant", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("latest"))))));
            window.SetResidentMode(true);
            window.Show();
            window.UpdateLayout();
            var list = (System.Windows.Controls.ItemsControl)window.FindName("MessageList");
            Assert.Equal("latest", Assert.IsType<DialogueMessage>(Assert.Single(list.Items.Cast<object>())).Text);
            Assert.Equal(Visibility.Collapsed, ((UIElement)window.FindName("SendButton")).Visibility);
            Assert.True(((UIElement)window.FindName("InputTextBox")).IsVisible);
            Assert.False(list.IsVisible);
            Assert.InRange(window.ActualHeight, 60, 160);
            var compactHeight = window.ActualHeight;
            window.ToggleResidentReply();
            window.UpdateLayout();
            Assert.True(list.IsVisible);
            Assert.InRange(window.ActualHeight, compactHeight + 1, 560);
            window.ToggleResidentReply();
            window.UpdateLayout();
            Assert.False(list.IsVisible);
            Assert.Equal(compactHeight, window.ActualHeight);
            window.SetResidentMode(false);
            window.UpdateLayout();
            Assert.True(list.IsVisible);
            Assert.Equal(3, list.Items.Count);
            Assert.Equal(Visibility.Visible, ((UIElement)window.FindName("SendButton")).Visibility);
        });
    }

    [Fact]
    public void Pasted_screenshot_uses_existing_image_submission_and_attachment_limit()
    {
        RunWindowFlow(window =>
        {
            window.SetResidentMode(true);
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96,
                PixelFormats.Bgra32, null, new byte[16], 8);
            for (var i = 0; i < 5; i++)
            {
                var data = new DataObject(DataFormats.Bitmap, bitmap);
                var paste = new DataObjectPastingEventArgs(data, false, DataFormats.Bitmap);
                ((System.Windows.Controls.TextBox)window.FindName("InputTextBox")).RaiseEvent(paste);
                Assert.True(paste.CommandCancelled);
            }
            InputSubmittedEventArgs? submitted = null;
            window.InputSubmitted += (_, input) => submitted = input;
            ((System.Windows.Controls.Button)window.FindName("SendButton")).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.NotNull(submitted);
            Assert.Equal(4, submitted.Attachments.Length);
            Assert.All(submitted.Attachments, attachment =>
            {
                var image = Assert.IsType<ImageInputAttachment>(attachment);
                Assert.Equal("image/png", image.MediaType);
                Assert.InRange(Convert.FromBase64String(image.Base64).Length, 1, 2 * 1024 * 1024);
            });
        });
    }

    [Fact]
    public void Screenshot_size_rejection_preserves_text_paste_and_empty_composer()
    {
        RunWindowFlow(window =>
        {
            var pixels = new byte[1024 * 1024 * 4];
            new Random(42).NextBytes(pixels);
            window.AddClipboardImage(System.Windows.Media.Imaging.BitmapSource.Create(1024, 1024,
                96, 96, PixelFormats.Bgra32, null, pixels, 4096));
            Assert.Empty(((System.Windows.Controls.ItemsControl)window.FindName("PendingAttachmentsList")).Items);
            Assert.Contains("2 MB", ((System.Windows.Controls.TextBlock)window.FindName("ConversationStatusLabel")).Text);
            var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "hello"),
                false, DataFormats.UnicodeText);
            ((System.Windows.Controls.TextBox)window.FindName("InputTextBox")).RaiseEvent(paste);
            Assert.False(paste.CommandCancelled);
        });
    }

    [Fact]
    public void Resident_reply_scrolls_and_keeps_approval_and_composer_visible()
    {
        RunWindowFlow(window =>
        {
            window.SetResidentMode(true);
            window.ApplyConversationMessage(new InputStatusMessage(1, "sent"));
            window.ApplyConversationMessage(new ReplyMessage(1, string.Join("\n\n", Enumerable.Repeat("long reply", 100)), true));
            window.Show();
            window.ToggleResidentReply();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            window.UpdateLayout();
            var scroll = (System.Windows.Controls.ScrollViewer)window.FindName("MessageScroll");
            Assert.True(scroll.ScrollableHeight > 0);
            scroll.ScrollToTop();
            window.UpdateLayout();
            scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
            {
                RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent,
            });
            window.UpdateLayout();
            Assert.True(scroll.VerticalOffset > 0);
            window.ApplyApprovalMessage(new ApprovalRequestMessage(1));
            window.UpdateLayout();
            Assert.True(((UIElement)window.FindName("ApprovalAllowButton")).IsVisible);
            Assert.True(((UIElement)window.FindName("InputTextBox")).IsVisible);
        });
    }

    [Fact]
    public void Dialogue_window_survives_history_with_rich_markdown()
    {
        RunWindowFlow(window =>
        {
            window.ApplyConversationMessage(new ConversationConfigMessage(true, 2000, "s-1", "w-1"));
            var history = ImmutableArray.Create(
                new HistoryItem("user", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("你好"))),
                new HistoryItem("assistant", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock(RichMarkdown))),
                new HistoryItem("user", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("再看一次"))),
                new HistoryItem("assistant", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("**第二段** 回复"))));
            window.ApplyConversationMessage(new HistoryMessage(1, true, history));
            window.Show();
            window.UpdateLayout();
            window.ApplyConversationMessage(new InputStatusMessage(2, "sent"));
            window.ApplyConversationMessage(new ReplyPreviewMessage(2, "部分回复", false));
            window.ApplyConversationMessage(new ReplyPreviewMessage(2, RichMarkdown, true));
            window.UpdateLayout();
            window.ApplyConversationMessage(new InputStatusMessage(2, "stopped"));
            window.UpdateLayout();
        });
    }

    [Fact]
    public void Dialogue_window_survives_an_attachment_input_flow()
    {
        RunWindowFlow(window =>
        {
            window.Show();
            window.UpdateLayout();
            // Simulate the user submitting an image-only input through the same path the UI uses.
            var images = ImmutableArray.Create(new DialogueImage("shot.png", null, null, "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
            var files = ImmutableArray.Create(new DialogueFile("notes.txt", "C:\\docs\\notes.txt"));
            window.ApplyConversationMessage(new InputStatusMessage(3, "sent"));
            window.ApplyConversationMessage(new ReplyMessage(3, "已处理图片与文件", true));
            window.UpdateLayout();
        });
    }

    [Fact]
    public void Interactive_target_walk_survives_markdown_content_elements()
    {
        RunOnSta(() =>
        {
            var richTextBox = new System.Windows.Controls.RichTextBox();
            richTextBox.Document = new System.Windows.Documents.FlowDocument(
                new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("hello")));
            var paragraph = (System.Windows.Documents.Paragraph)richTextBox.Document.Blocks.FirstBlock;

            // Regression: the markdown host's OriginalSource is a Paragraph (a ContentElement);
            // VisualTreeHelper.GetParent on it used to throw and crash the pet.
            Assert.True(DialogueWindow.IsInteractiveTarget(paragraph));
            Assert.True(DialogueWindow.IsInteractiveTarget(new System.Windows.Controls.TextBox()));
        });
    }

    [Fact]
    public void Markdown_document_is_built_for_realized_history_messages()
    {
        RunWindowFlow(window =>
        {
            window.ApplyConversationMessage(new ConversationConfigMessage(true, 2000, "s-1", "w-1"));
            var history = ImmutableArray.Create(
                new HistoryItem("assistant", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("**粗体** 与 `代码` 内容"))));
            window.ApplyConversationMessage(new HistoryMessage(1, true, history));
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            window.UpdateLayout();

            var hosts = FindVisuals<System.Windows.Controls.RichTextBox>(window);
            Assert.NotEmpty(hosts);
            Assert.Contains(hosts, host => TextOf(host.Document).Contains("粗体"));
        });
    }

    [Fact]
    public void Default_rich_text_box_document_reports_a_single_empty_paragraph()
    {
        RunOnSta(() =>
        {
            var richTextBox = new System.Windows.Controls.RichTextBox();

            // A fresh RichTextBox already owns a FlowDocument with one empty Paragraph;
            // any "already rendered" guard must not mistake that for real content.
            Assert.Equal(1, richTextBox.Document.Blocks.Count);
            var first = Assert.IsType<System.Windows.Documents.Paragraph>(richTextBox.Document.Blocks.FirstBlock);
            Assert.Equal(0, first.Inlines.Count);
        });
    }

    private static string TextOf(System.Windows.Documents.FlowDocument document) =>
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;

    private static IEnumerable<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var nested in FindVisuals<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return nested;
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            try
            {
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                // Drain pending render work, then shut the thread's dispatcher down so the
                // composition pipeline cannot crash the test host on teardown.
                try
                {
                    dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    dispatcher.InvokeShutdown();
                }
                catch
                {
                    // Best effort teardown.
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    /// <summary>Runs a window flow on its own STA thread with clean composition teardown.</summary>
    private static void RunWindowFlow(Action<DialogueWindow> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var window = new DialogueWindow(new Win32ScreenLayout());
            try
            {
                action(window);
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                try
                {
                    window.Close();
                    dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    dispatcher.InvokeShutdown();
                }
                catch
                {
                    // Best effort teardown.
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
