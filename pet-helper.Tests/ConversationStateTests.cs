using System.Collections.Immutable;
using System.Linq;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ConversationStateTests
{
    [Fact]
    public void Buffers_the_latest_configured_streaming_text()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 4);

        state.Apply(new ReplyPreviewMessage(7, "abcdef", false));

        Assert.Equal("cdef", state.PendingStreamText);
        Assert.Equal(7, state.RequestId);
        Assert.True(state.HasActiveTurn);
        Assert.True(state.HasStreamingMessage);
        var assistant = Assert.Single(state.Messages.Where(message => message.Role == "assistant"));
        Assert.True(assistant.Streaming);
    }

    [Fact]
    public void Completed_preview_commits_the_text_and_stops_streaming()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new ReplyPreviewMessage(3, "部分文本", false));
        state.Apply(new ReplyPreviewMessage(3, "最终文本", true));

        Assert.False(state.HasStreamingMessage);
        Assert.Null(state.PendingStreamText);
        var assistant = Assert.Single(state.Messages.Where(message => message.Role == "assistant"));
        Assert.False(assistant.Streaming);
        Assert.Equal("最终文本", assistant.Text);
        Assert.Equal(MessageEndState.None, assistant.End);
    }

    [Fact]
    public void A_terminal_stop_never_leaves_the_dialogue_blank()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(4, "stopped"));

        Assert.False(state.HasActiveTurn);
        Assert.Equal("已停止", state.StatusText);
        var assistant = Assert.Single(state.Messages.Where(message => message.Role == "assistant"));
        Assert.Equal(MessageEndState.Stopped, assistant.End);
        Assert.Equal(string.Empty, assistant.Text);
    }

    [Fact]
    public void Terminal_endings_mark_the_streaming_message_in_place()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new ReplyPreviewMessage(5, "部分", false));
        var assistant = Assert.Single(state.Messages.Where(message => message.Role == "assistant"));

        state.Apply(new InputStatusMessage(5, "interrupted"));

        Assert.False(assistant.Streaming);
        Assert.Equal(MessageEndState.Interrupted, assistant.End);
        // The streamed text stays buffered for the UI throttle to flush onto the message.
        Assert.Equal("部分", state.PendingStreamText);
    }

    [Fact]
    public void Ignores_stale_reply_preview_and_clear_messages()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new InputStatusMessage(2, "sent"));
        state.Apply(new ReplyPreviewMessage(2, "current", false));

        state.Apply(new ReplyPreviewMessage(1, "stale", false));
        state.Apply(new ClearPreviewMessage(1, "next-input"));

        Assert.Equal(2, state.RequestId);
        Assert.Equal("current", state.PendingStreamText);
    }

    [Fact]
    public void Starting_the_next_input_immediately_echoes_the_user_message()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new InputStatusMessage(1, "sent"));
        state.Apply(new ReplyPreviewMessage(1, "visible", false));

        state.BeginInput(2, "新问题", ImmutableArray<DialogueImage>.Empty, ImmutableArray<DialogueFile>.Empty);

        Assert.Equal(2, state.RequestId);
        Assert.Equal("正在发送…", state.StatusText);
        var user = Assert.Single(state.Messages.Where(message => message.Role == "user"));
        Assert.Equal("新问题", user.Text);
    }

    [Fact]
    public void Begin_input_carries_image_thumbnails_and_file_cards()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        var images = ImmutableArray.Create(new DialogueImage("shot.png", null, null, "AAAA"));
        var files = ImmutableArray.Create(new DialogueFile("notes.txt", "C:\\docs\\notes.txt"));

        state.BeginInput(3, string.Empty, images, files);

        var user = Assert.Single(state.Messages.Where(message => message.Role == "user"));
        Assert.Equal(string.Empty, user.Text);
        Assert.Single(user.Images);
        Assert.Single(user.Files);
    }

    [Fact]
    public void Clear_preview_stops_streaming_and_keeps_the_partial_text()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new ReplyPreviewMessage(2, "visible", false));

        state.Apply(new ClearPreviewMessage(2, "next-input"));

        Assert.False(state.HasActiveTurn);
        var assistant = Assert.Single(state.Messages.Where(message => message.Role == "assistant"));
        Assert.False(assistant.Streaming);
        Assert.Equal("visible", state.PendingStreamText);
    }

    [Fact]
    public void Maps_fixed_input_status_text()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(1, "queued"));
        Assert.Equal("已排队", state.StatusText);
        state.Apply(new InputStatusMessage(2, "sent"));
        Assert.Equal("已发送", state.StatusText);
        state.Apply(new InputStatusMessage(3, "rejected"));
        Assert.Equal("未能发送", state.StatusText);
        state.Apply(new InputStatusMessage(4, "no-default-session"));
        Assert.Equal("请在 DSH 设置中选择会话", state.StatusText);
        state.Apply(new InputStatusMessage(5, "session-unavailable"));
        Assert.Equal("会话不可用", state.StatusText);
        state.Apply(new InputStatusMessage(6, "stopped"));
        Assert.Equal("已停止", state.StatusText);
        state.Apply(new InputStatusMessage(7, "interrupted"));
        Assert.Equal("已中断", state.StatusText);
        state.Apply(new InputStatusMessage(8, "failed"));
        Assert.Equal("生成失败", state.StatusText);
    }

    [Fact]
    public void Marks_the_reply_pending_after_sending_and_resolves_on_reply()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(4, "sent"));
        Assert.True(state.HasActiveTurn);

        state.Apply(new ReplyMessage(4, "最终回复", true));
        Assert.False(state.HasActiveTurn);
        var assistant = Assert.Single(state.Messages.Where(message => message.Role == "assistant"));
        Assert.Equal("最终回复", assistant.Text);
        Assert.False(assistant.Streaming);
    }

    [Fact]
    public void Stores_history_messages_with_text_and_image_placeholders()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        var blocks = ImmutableArray.Create<HistoryBlock>(
            new HistoryImageBlock("chart.png", 640, 480),
            new HistoryTextBlock("hello"));
        var messages = ImmutableArray.Create(
            new HistoryItem("user", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("hi"))),
            new HistoryItem("assistant", blocks));

        state.Apply(new HistoryMessage(9, true, messages));

        Assert.True(state.HistoryAvailable);
        Assert.Equal(2, state.Messages.Length);
        Assert.Equal("hi", state.Messages[0].Text);
        Assert.Equal("hello", state.Messages[1].Text);
        var image = Assert.Single(state.Messages[1].Images);
        Assert.Equal("chart.png", image.Name);
        Assert.Equal(640, image.Width);
        Assert.Null(image.DataBase64);
    }

    [Fact]
    public void Clears_messages_when_the_default_session_changes()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        state.Apply(new ConversationConfigMessage(false, 80, "s-1", null));
        state.Apply(new ReplyMessage(4, "回复", true));
        state.Apply(new HistoryMessage(9, true, ImmutableArray.Create(new HistoryItem("user", ImmutableArray.Create<HistoryBlock>(new HistoryTextBlock("hi"))))));

        state.Apply(new ConversationConfigMessage(false, 80, "s-2", null));

        Assert.Empty(state.Messages);
        Assert.False(state.HasActiveTurn);
        Assert.Equal(0, state.RequestId);
    }
}
