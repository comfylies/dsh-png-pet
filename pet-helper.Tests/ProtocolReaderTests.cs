using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ProtocolReaderTests
{
    [Fact]
    public void Parses_v5_shutdown_command()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"shutdown\"}");

        Assert.Equal(new ShutdownMessage(), message);
    }

    [Fact]
    public void Parses_a_composite_active_state()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"working\"],\"label\":\"思考中/工作中\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal("active", state.State);
        Assert.Equal(new[] { "thinking", "working" }, state.Activities);
        Assert.Equal("思考中/工作中", state.Label);
        Assert.Equal(4, state.Sequence);
    }

    [Fact]
    public void Parsed_activities_are_immutable()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        var activities = Assert.IsAssignableFrom<IList<string>>(state.Activities);
        Assert.Throws<NotSupportedException>(() => activities.Add("working"));
        Assert.Equal(new[] { "thinking" }, state.Activities);
    }

    [Fact]
    public void Parses_the_largest_safe_sequence()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":9007199254740991}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal(9_007_199_254_740_991, state.Sequence);
    }

    [Fact]
    public void Parses_a_supported_config()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"config\",\"scale\":1.25,\"reducedMotion\":true}");

        Assert.Equal(new ConfigMessage(1.25d, true), message);
    }

    [Fact]
    public void Rejects_a_free_form_state_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"working\"],\"label\":\"secret\",\"sequence\":4}"));
    }

    [Fact]
    public void Rejects_a_bad_disconnected_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"disconnected\",\"activities\":[],\"label\":\"secret\",\"sequence\":4}"));
    }

    [Theory]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4}")]
    [InlineData("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"thinking\"],\"label\":\"思考中/思考中\",\"sequence\":4}")]
    [InlineData("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"working\",\"thinking\"],\"label\":\"思考中/工作中\",\"sequence\":4}")]
    [InlineData("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"工作中…\",\"sequence\":4}")]
    [InlineData("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4,\"extra\":true}")]
    [InlineData("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":9007199254740992}")]
    public void Rejects_an_invalid_v5_state(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("{\"version\":1,\"kind\":\"shutdown\"}")]
    [InlineData("{\"version\":5,\"kind\":\"shutdown\",\"extra\":true}")]
    [InlineData("{\"version\":5,\"kind\":\"unknown\"}")]
    public void Rejects_an_incompatible_command(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Fact]
    public void Parses_v5_reply_preview()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"reply-preview\",\"requestId\":7,\"text\":\"abcdef\",\"completed\":false}");

        Assert.Equal(new ReplyPreviewMessage(7, "abcdef", false), message);
    }

    [Theory]
    [InlineData("{\"version\":5,\"kind\":\"conversation-config\",\"previewEnabled\":true,\"previewMaxChars\":80,\"defaultSessionId\":null}", true, 80)]
    [InlineData("{\"version\":5,\"kind\":\"conversation-config\",\"previewEnabled\":false,\"previewMaxChars\":2000,\"defaultSessionId\":null}", false, 2000)]
    public void Parses_v5_conversation_config_at_both_bounds(string line, bool previewEnabled, int previewMaxChars)
    {
        Assert.Equal(new ConversationConfigMessage(previewEnabled, previewMaxChars, null), ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("sent")]
    [InlineData("rejected")]
    [InlineData("no-default-session")]
    [InlineData("session-unavailable")]
    public void Parses_v5_input_status(string status)
    {
        var message = ProtocolReader.Parse($"{{\"version\":5,\"kind\":\"input-status\",\"requestId\":7,\"status\":\"{status}\"}}");

        Assert.Equal(new InputStatusMessage(7, status), message);
    }

    [Fact]
    public void Parses_v5_clear_preview()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"clear-preview\",\"requestId\":7,\"reason\":\"next-input\"}");

        Assert.Equal(new ClearPreviewMessage(7, "next-input"), message);
    }

    [Theory]
    [InlineData("{\"version\":5,\"kind\":\"conversation-config\",\"previewEnabled\":true,\"previewMaxChars\":79,\"defaultSessionId\":null}")]
    [InlineData("{\"version\":5,\"kind\":\"input-status\",\"requestId\":0,\"status\":\"sent\"}")]
    [InlineData("{\"version\":5,\"kind\":\"reply-preview\",\"requestId\":1,\"text\":\"\",\"completed\":false}")]
    [InlineData("{\"version\":5,\"kind\":\"clear-preview\",\"requestId\":1,\"reason\":\"unknown\"}")]
    public void Rejects_invalid_v5_dialogue_messages(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("{\"version\":5,\"kind\":\"conversation-config\",\"previewEnabled\":true,\"previewMaxChars\":80,\"extra\":true}")]
    [InlineData("{\"version\":5,\"kind\":\"input-status\",\"requestId\":1,\"status\":\"queued\",\"extra\":true}")]
    [InlineData("{\"version\":5,\"kind\":\"clear-preview\",\"requestId\":1,\"reason\":\"closed\",\"extra\":true}")]
    [InlineData("{\"version\":5,\"kind\":\"reply-preview\",\"requestId\":1,\"text\":\"safe\",\"completed\":false,\"extra\":true}")]
    public void Rejects_unknown_fields_on_every_v5_dialogue_kind(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Fact]
    public void Parses_reply_message()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"reply\",\"requestId\":7,\"text\":\"final\",\"completed\":true}");

        var reply = Assert.IsType<ReplyMessage>(message);
        Assert.Equal(7, reply.RequestId);
        Assert.Equal("final", reply.Text);
        Assert.True(reply.Completed);
    }

    [Fact]
    public void Parses_conversation_history_message()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"conversation-history\",\"requestId\":8,\"available\":true,\"messages\":[{\"role\":\"user\",\"text\":\"hi\"},{\"role\":\"assistant\",\"text\":\"hello\"}]}");

        var history = Assert.IsType<HistoryMessage>(message);
        Assert.True(history.Available);
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("user", history.Messages[0].Role);
        Assert.Equal("hello", history.Messages[1].Text);
    }

    [Fact]
    public void Rejects_over_limit_reply_and_history_entries()
    {
        var longReply = "{\"version\":5,\"kind\":\"reply\",\"requestId\":1,\"text\":\"" + new string('a', 8001) + "\",\"completed\":true}";
        Assert.Null(ProtocolReader.Parse(longReply));

        var tooMany = string.Join(",", Enumerable.Range(0, 21).Select(i => $"{{\"role\":\"user\",\"text\":\"m{i}\"}}"));
        var history = $"{{\"version\":5,\"kind\":\"conversation-history\",\"requestId\":1,\"available\":true,\"messages\":[{tooMany}]}}";
        Assert.Null(ProtocolReader.Parse(history));
    }
}
