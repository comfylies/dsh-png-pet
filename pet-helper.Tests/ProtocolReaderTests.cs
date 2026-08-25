using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ProtocolReaderTests
{
    [Fact]
    public void Parses_v3_shutdown_command()
    {
        var message = ProtocolReader.Parse("{\"version\":3,\"kind\":\"shutdown\"}");

        Assert.Equal(new ShutdownMessage(), message);
    }

    [Fact]
    public void Parses_a_composite_active_state()
    {
        var message = ProtocolReader.Parse("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"working\"],\"label\":\"思考中/工作中\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal("active", state.State);
        Assert.Equal(new[] { "thinking", "working" }, state.Activities);
        Assert.Equal("思考中/工作中", state.Label);
        Assert.Equal(4, state.Sequence);
    }

    [Fact]
    public void Parsed_activities_are_immutable()
    {
        var message = ProtocolReader.Parse("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4}");

        var state = Assert.IsType<StateMessage>(message);
        var activities = Assert.IsAssignableFrom<IList<string>>(state.Activities);
        Assert.Throws<NotSupportedException>(() => activities.Add("working"));
        Assert.Equal(new[] { "thinking" }, state.Activities);
    }

    [Fact]
    public void Parses_the_largest_safe_sequence()
    {
        var message = ProtocolReader.Parse("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":9007199254740991}");

        var state = Assert.IsType<StateMessage>(message);
        Assert.Equal(9_007_199_254_740_991, state.Sequence);
    }

    [Fact]
    public void Parses_a_supported_config()
    {
        var message = ProtocolReader.Parse("{\"version\":3,\"kind\":\"config\",\"scale\":1.25,\"reducedMotion\":true}");

        Assert.Equal(new ConfigMessage(1.25d, true), message);
    }

    [Fact]
    public void Rejects_a_free_form_state_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"working\"],\"label\":\"secret\",\"sequence\":4}"));
    }

    [Fact]
    public void Rejects_a_bad_disconnected_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":3,\"kind\":\"state\",\"state\":\"disconnected\",\"activities\":[],\"label\":\"secret\",\"sequence\":4}"));
    }

    [Theory]
    [InlineData("{\"version\":2,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4}")]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\",\"thinking\"],\"label\":\"思考中/思考中\",\"sequence\":4}")]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"working\",\"thinking\"],\"label\":\"思考中/工作中\",\"sequence\":4}")]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"工作中…\",\"sequence\":4}")]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":4,\"extra\":true}")]
    [InlineData("{\"version\":3,\"kind\":\"state\",\"state\":\"active\",\"activities\":[\"thinking\"],\"label\":\"思考中…\",\"sequence\":9007199254740992}")]
    public void Rejects_an_invalid_v3_state(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }

    [Theory]
    [InlineData("{\"version\":1,\"kind\":\"shutdown\"}")]
    [InlineData("{\"version\":3,\"kind\":\"shutdown\",\"extra\":true}")]
    [InlineData("{\"version\":3,\"kind\":\"unknown\"}")]
    public void Rejects_an_incompatible_command(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }
}
