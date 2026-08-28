using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ProtocolReaderTests
{
    [Fact]
    public void Parses_v6_shutdown_command()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"shutdown\"}");

        Assert.Equal(new ShutdownMessage(), message);
    }

    [Fact]
    public void Parses_a_composite_active_state()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"working\"],\"label\":\"思考中/工作中\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal("active", state.State);
        Assert.Equal(new[] { "thinking", "working" }, state.Activities);
        Assert.Equal("思考中/工作中", state.Label);
        Assert.Equal(4, state.Sequence);
    }

    [Fact]
    public void Parses_an_outputting_active_state()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"responding\"],\"label\":\"输出中…\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal("active", state.State);
        Assert.Equal(new[] { "responding" }, state.Activities);
        Assert.Equal("输出中…", state.Label);
        Assert.Equal(4, state.Sequence);
    }

    [Fact]
    public void Parses_a_question_state()
    {
        var message = ProtocolReader.Parse("{\"version\":5,\"kind\":\"state\",\"state\":\"question\",\"activities\":[],\"label\":\"等你回答…\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal("question", state.State);
        Assert.Empty(state.Activities);
        Assert.Equal("等你回答…", state.Label);
        Assert.Equal(4, state.Sequence);
    }

    [Fact]
    public void Parsed_activities_are_immutable()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        var activities = Assert.IsAssignableFrom<IList<string>>(state.Activities);
        Assert.Throws<NotSupportedException>(() => activities.Add("working"));
        Assert.Equal(new[] { "thinking" }, state.Activities);
    }

    [Fact]
    public void Parses_the_largest_safe_sequence()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":9007199254740991}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal(9_007_199_254_740_991, state.Sequence);
    }

    [Fact]
    public void Parses_a_supported_config()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"config\",\"scale\":1.25,\"reducedMotion\":true}");

        Assert.Equal(new ConfigMessage(1.25d, true), message);
    }

    [Fact]
    public void Rejects_a_free_form_state_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"working\"],\"label\":\"secret\",\"sequence\":4}"));
    }

    [Fact]
    public void Rejects_a_bad_disconnected_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":6,\"kind\":\"state\",\"state\":\"disconnected\",\"activities\":[],\"label\":\"secret\",\"sequence\":4}"));
    }

    [Theory]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4}")]
    [InlineData("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"thinking\"],\"label\":\"思考中/思考中\",\"sequence\":4}")]
    [InlineData("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"working\",\"thinking\"],\"label\":\"思考中/工作中\",\"sequence\":4}")]
    [InlineData("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"responding\"],\"label\":\"输出中…\",\"sequence\":4}")]
    [InlineData("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"工作中…\",\"sequence\":4}")]
    [InlineData("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4,\"extra\":true}")]
    [InlineData("{\"version\":6,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":9007199254740992}")]
    public void Rejects_an_invalid_v6_state(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("{\"version\":1,\"kind\":\"shutdown\"}")]
    [InlineData("{\"version\":6,\"kind\":\"shutdown\",\"extra\":true}")]
    [InlineData("{\"version\":6,\"kind\":\"unknown\"}")]
    public void Rejects_an_incompatible_command(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Fact]
    public void Parses_v6_reply_preview()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"reply-preview\",\"requestId\":7,\"text\":\"abcdef\",\"completed\":false}");

        Assert.Equal(new ReplyPreviewMessage(7, "abcdef", false), message);
    }

    [Fact]
    public void Parses_an_extended_reply_preview_text()
    {
        var text = new string('a', 8000);
        var message = ProtocolReader.Parse($"{{\"version\":6,\"kind\":\"reply-preview\",\"requestId\":7,\"text\":\"{text}\",\"completed\":false}}");

        Assert.Equal(new ReplyPreviewMessage(7, text, false), message);
        Assert.Null(ProtocolReader.Parse($"{{\"version\":6,\"kind\":\"reply-preview\",\"requestId\":7,\"text\":\"{text}a\",\"completed\":false}}"));
    }

    [Theory]
    [InlineData("{\"version\":6,\"kind\":\"conversation-config\",\"previewEnabled\":true,\"previewMaxChars\":80,\"defaultSessionId\":null,\"defaultWorkspaceId\":null}", true, 80)]
    [InlineData("{\"version\":6,\"kind\":\"conversation-config\",\"previewEnabled\":false,\"previewMaxChars\":8000,\"defaultSessionId\":null,\"defaultWorkspaceId\":null}", false, 8000)]
    public void Parses_v6_conversation_config_at_both_bounds(string line, bool previewEnabled, int previewMaxChars)
    {
        Assert.Equal(new ConversationConfigMessage(previewEnabled, previewMaxChars, null, null), ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("sent")]
    [InlineData("rejected")]
    [InlineData("no-default-session")]
    [InlineData("session-unavailable")]
    [InlineData("stopped")]
    [InlineData("interrupted")]
    [InlineData("failed")]
    public void Parses_v6_input_status(string status)
    {
        var message = ProtocolReader.Parse($"{{\"version\":6,\"kind\":\"input-status\",\"requestId\":7,\"status\":\"{status}\"}}");

        Assert.Equal(new InputStatusMessage(7, status), message);
    }

    [Fact]
    public void Parses_v6_clear_preview()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"clear-preview\",\"requestId\":7,\"reason\":\"next-input\"}");

        Assert.Equal(new ClearPreviewMessage(7, "next-input"), message);
    }

    [Theory]
    [InlineData("{\"version\":6,\"kind\":\"conversation-config\",\"previewEnabled\":true,\"previewMaxChars\":79,\"defaultSessionId\":null}")]
    [InlineData("{\"version\":6,\"kind\":\"input-status\",\"requestId\":0,\"status\":\"sent\"}")]
    [InlineData("{\"version\":6,\"kind\":\"reply-preview\",\"requestId\":1,\"text\":\"\",\"completed\":false}")]
    [InlineData("{\"version\":6,\"kind\":\"clear-preview\",\"requestId\":1,\"reason\":\"unknown\"}")]
    [InlineData("{\"version\":6,\"kind\":\"input-status\",\"requestId\":1,\"status\":\"unknown\"}")]
    public void Rejects_invalid_v6_dialogue_messages(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("{\"version\":6,\"kind\":\"conversation-config\",\"previewEnabled\":true,\"previewMaxChars\":80,\"extra\":true}")]
    [InlineData("{\"version\":6,\"kind\":\"input-status\",\"requestId\":1,\"status\":\"queued\",\"extra\":true}")]
    [InlineData("{\"version\":6,\"kind\":\"clear-preview\",\"requestId\":1,\"reason\":\"closed\",\"extra\":true}")]
    [InlineData("{\"version\":6,\"kind\":\"reply-preview\",\"requestId\":1,\"text\":\"safe\",\"completed\":false,\"extra\":true}")]
    public void Rejects_unknown_fields_on_every_v6_dialogue_kind(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Fact]
    public void Parses_reply_message()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"reply\",\"requestId\":7,\"text\":\"final\",\"completed\":true}");

        var reply = Assert.IsType<ReplyMessage>(message);
        Assert.Equal(7, reply.RequestId);
        Assert.Equal("final", reply.Text);
        Assert.True(reply.Completed);
    }

    [Fact]
    public void Parses_conversation_history_message_with_blocks()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"conversation-history\",\"requestId\":8,\"available\":true,\"messages\":["
            + "{\"role\":\"user\",\"blocks\":[{\"type\":\"text\",\"text\":\"hi\"}]},"
            + "{\"role\":\"assistant\",\"blocks\":[{\"type\":\"text\",\"text\":\"hello\"},{\"type\":\"image\",\"name\":\"chart.png\",\"width\":640,\"height\":480}]}]}");

        var history = Assert.IsType<HistoryMessage>(message);
        Assert.True(history.Available);
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("user", history.Messages[0].Role);
        var text = Assert.IsType<HistoryTextBlock>(history.Messages[1].Blocks[0]);
        Assert.Equal("hello", text.Text);
        var image = Assert.IsType<HistoryImageBlock>(history.Messages[1].Blocks[1]);
        Assert.Equal("chart.png", image.Name);
        Assert.Equal(640, image.Width);
        Assert.Equal(480, image.Height);
    }

    [Fact]
    public void Rejects_over_limit_reply_and_history_entries()
    {
        var longReply = "{\"version\":6,\"kind\":\"reply\",\"requestId\":1,\"text\":\"" + new string('a', 8001) + "\",\"completed\":true}";
        Assert.Null(ProtocolReader.Parse(longReply));

        var tooMany = string.Join(",", Enumerable.Range(0, 21).Select(i => $"{{\"role\":\"user\",\"blocks\":[{{\"type\":\"text\",\"text\":\"m{i}\"}}]}}"));
        var history = $"{{\"version\":6,\"kind\":\"conversation-history\",\"requestId\":1,\"available\":true,\"messages\":[{tooMany}]}}";
        Assert.Null(ProtocolReader.Parse(history));

        var emptyBlocks = "{\"version\":6,\"kind\":\"conversation-history\",\"requestId\":1,\"available\":true,\"messages\":[{\"role\":\"user\",\"blocks\":[]}]}";
        Assert.Null(ProtocolReader.Parse(emptyBlocks));

        var badImage = "{\"version\":6,\"kind\":\"conversation-history\",\"requestId\":1,\"available\":true,\"messages\":[{\"role\":\"user\",\"blocks\":[{\"type\":\"image\",\"name\":\"a\",\"width\":0,\"height\":1}]}]}";
        Assert.Null(ProtocolReader.Parse(badImage));
    }

    [Fact]
    public void Parses_a_target_request_with_grouped_and_ungrouped_sessions()
    {
        var line = "{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,"
            + "\"workspaces\":[{\"id\":\"w-1\",\"title\":\"pet-helper\",\"path\":\"C:\\\\pet-helper\"}],"
            + "\"sessionsByWorkspace\":{\"w-1\":[{\"id\":\"s-9\",\"title\":\"修复窗口\",\"blank\":false}]},"
            + "\"ungrouped\":[{\"id\":\"s-2\",\"title\":\"\",\"blank\":true}],"
            + "\"defaultWorkspaceId\":\"w-1\",\"defaultSessionId\":\"s-9\"}";

        var message = ProtocolReader.Parse(line);

        var target = Assert.IsType<TargetRequestMessage>(message);
        Assert.Equal(7, target.RequestId);
        Assert.Null(target.Error);
        var workspace = Assert.Single(target.Workspaces);
        Assert.Equal(new TargetWorkspaceInfo("w-1", "pet-helper", "C:\\pet-helper"), workspace);
        var grouped = Assert.Single(target.SessionsByWorkspace["w-1"]);
        Assert.Equal(new TargetSessionInfo("s-9", "修复窗口", false), grouped);
        var loose = Assert.Single(target.Ungrouped);
        Assert.Equal(new TargetSessionInfo("s-2", "", true), loose);
        Assert.Equal("w-1", target.DefaultWorkspaceId);
        Assert.Equal("s-9", target.DefaultSessionId);
    }

    [Fact]
    public void Parses_a_target_request_error_state()
    {
        var message = ProtocolReader.Parse("{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,"
            + "\"workspaces\":[],\"sessionsByWorkspace\":{},\"ungrouped\":[],"
            + "\"defaultWorkspaceId\":null,\"defaultSessionId\":null,\"error\":\"数据加载失败，请重试\"}");

        var target = Assert.IsType<TargetRequestMessage>(message);
        Assert.Equal("数据加载失败，请重试", target.Error);
        Assert.Empty(target.Workspaces);
    }

    [Theory]
    [InlineData("{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,\"workspaces\":[{\"id\":\"\",\"title\":\"t\",\"path\":\"C:\\\\x\"}],\"sessionsByWorkspace\":{},\"ungrouped\":[],\"defaultWorkspaceId\":null,\"defaultSessionId\":null}")]
    [InlineData("{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,\"workspaces\":[],\"sessionsByWorkspace\":{\"w\":[{\"id\":\"s\",\"title\":\"t\",\"blank\":\"yes\"}]},\"ungrouped\":[],\"defaultWorkspaceId\":null,\"defaultSessionId\":null}")]
    [InlineData("{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,\"workspaces\":[],\"sessionsByWorkspace\":{},\"ungrouped\":[],\"defaultWorkspaceId\":\"\",\"defaultSessionId\":null}")]
    [InlineData("{\"version\":6,\"kind\":\"target-request\",\"requestId\":0,\"workspaces\":[],\"sessionsByWorkspace\":{},\"ungrouped\":[],\"defaultWorkspaceId\":null,\"defaultSessionId\":null}")]
    [InlineData("{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,\"workspaces\":[],\"sessionsByWorkspace\":{},\"ungrouped\":[],\"defaultWorkspaceId\":null,\"defaultSessionId\":null,\"extra\":true}")]
    public void Rejects_invalid_target_requests(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Fact]
    public void Rejects_a_target_request_with_too_many_workspaces()
    {
        var workspaces = string.Join(",", Enumerable.Range(0, 65).Select(i => $"{{\"id\":\"w-{i}\",\"title\":\"t\",\"path\":\"C:\\\\x\"}}"));
        var line = $"{{\"version\":6,\"kind\":\"target-request\",\"requestId\":7,\"workspaces\":[{workspaces}],\"sessionsByWorkspace\":{{}},\"ungrouped\":[],\"defaultWorkspaceId\":null,\"defaultSessionId\":null}}";
        Assert.Null(ProtocolReader.Parse(line));
    }
}
