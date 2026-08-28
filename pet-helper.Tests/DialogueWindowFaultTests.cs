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
