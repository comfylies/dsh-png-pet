using System.Collections.Immutable;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ConversationStateTests
{
    [Fact]
    public void Keeps_the_latest_configured_preview_characters()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 4);

        state.Apply(new ReplyPreviewMessage(7, "abcdef", false));

        Assert.Equal("cdef", state.PreviewText);
        Assert.Equal(7, state.RequestId);
    }

    [Fact]
    public void Clears_preview_when_disabled()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new ReplyPreviewMessage(1, "visible", false));

        state.Apply(new ConversationConfigMessage(false, 80, null));

        Assert.Equal(string.Empty, state.PreviewText);
    }

    [Fact]
    public void Uses_only_fixed_input_status_text()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(3, "session-unavailable"));

        Assert.Equal("请在 DSH 设置中选择会话", state.StatusText);
        Assert.Equal(3, state.RequestId);
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
        Assert.Equal("current", state.PreviewText);
    }

    [Fact]
    public void Starting_the_next_input_immediately_clears_local_preview()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new InputStatusMessage(1, "sent"));
        state.Apply(new ReplyPreviewMessage(1, "visible", false));

        state.BeginInput(2);

        Assert.Equal(2, state.RequestId);
        Assert.Equal(string.Empty, state.PreviewText);
    }

    [Fact]
    public void Clears_preview_for_the_current_request()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 80);
        state.Apply(new InputStatusMessage(2, "sent"));
        state.Apply(new ReplyPreviewMessage(2, "visible", false));

        state.Apply(new ClearPreviewMessage(2, "next-input"));

        Assert.Equal(string.Empty, state.PreviewText);
    }

    [Fact]
    public void Reenabling_preview_preserves_only_the_shrunk_latest_buffer()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 2000);
        var initialPreview = new string('a', 100) + "tail";
        state.Apply(new ReplyPreviewMessage(3, initialPreview, false));

        state.Apply(new ConversationConfigMessage(false, 80, null));
        state.Apply(new ConversationConfigMessage(true, 80, null));
        state.Apply(new ReplyPreviewMessage(3, initialPreview, false));

        Assert.Equal("tail", state.PreviewText[^4..]);
        Assert.Equal(80, state.PreviewText.Length);
    }

    [Fact]
    public void Shrinks_an_existing_enabled_preview_immediately_after_config_change()
    {
        var state = new ConversationState(previewEnabled: true, previewMaxChars: 2000);
        state.Apply(new ReplyPreviewMessage(3, new string('a', 100) + "tail", false));

        state.Apply(new ConversationConfigMessage(true, 80, null));

        Assert.Equal(80, state.PreviewText.Length);
        Assert.Equal("tail", state.PreviewText[^4..]);
    }

    [Theory]
    [InlineData("queued", "已排队")]
    [InlineData("sent", "已发送")]
    [InlineData("rejected", "未能发送")]
    [InlineData("no-default-session", "请在 DSH 设置中选择会话")]
    public void Maps_fixed_input_status_text(string status, string expectedText)
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(1, status));

        Assert.Equal(expectedText, state.StatusText);
    }

    [Fact]
    public void Marks_the_reply_pending_after_sending_and_resolves_on_reply()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);

        state.Apply(new InputStatusMessage(4, "sent"));
        Assert.True(state.ReplyPending);
        Assert.Equal(string.Empty, state.ReplyText);

        state.Apply(new ReplyMessage(4, "最终回复", true));
        Assert.False(state.ReplyPending);
        Assert.Equal("最终回复", state.ReplyText);
    }

    [Fact]
    public void Clears_reply_pending_on_rejection()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        state.Apply(new InputStatusMessage(4, "sent"));

        state.Apply(new InputStatusMessage(4, "rejected"));

        Assert.False(state.ReplyPending);
        Assert.Equal(string.Empty, state.ReplyText);
    }

    [Fact]
    public void Stores_history_messages_and_availability()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        var messages = ImmutableArray.Create(new HistoryItem("user", "hi"), new HistoryItem("assistant", "hello"));

        state.Apply(new HistoryMessage(9, true, messages));

        Assert.True(state.HistoryAvailable);
        Assert.Equal(2, state.HistoryMessages.Length);
    }

    [Fact]
    public void Clears_reply_and_history_when_the_default_session_changes()
    {
        var state = new ConversationState(previewEnabled: false, previewMaxChars: 80);
        state.Apply(new ConversationConfigMessage(false, 80, "s-1"));
        state.Apply(new InputStatusMessage(4, "sent"));
        state.Apply(new ReplyMessage(4, "回复", true));
        state.Apply(new HistoryMessage(9, true, ImmutableArray.Create(new HistoryItem("user", "hi"))));

        state.Apply(new ConversationConfigMessage(false, 80, "s-2"));

        Assert.Equal(string.Empty, state.ReplyText);
        Assert.False(state.ReplyPending);
        Assert.Equal(0, state.HistoryMessages.Length);
    }

}
