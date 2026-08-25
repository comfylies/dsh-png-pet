using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ProtocolReaderTests
{
    [Fact]
    public void Parses_v2_shutdown_command()
    {
        var message = ProtocolReader.Parse("{\"version\":2,\"kind\":\"shutdown\"}");

        Assert.Equal(new ShutdownMessage(), message);
    }

    [Fact]
    public void Parses_a_fixed_working_state()
    {
        var message = ProtocolReader.Parse("{\"version\":2,\"kind\":\"state\",\"state\":\"working\",\"label\":\"工作中…\",\"sequence\":4}");

        Assert.Equal(new StateMessage("working", "工作中…", 4), message);
    }

    [Fact]
    public void Parses_a_supported_config()
    {
        var message = ProtocolReader.Parse("{\"version\":2,\"kind\":\"config\",\"scale\":1.25,\"reducedMotion\":true}");

        Assert.Equal(new ConfigMessage(1.25d, true), message);
    }

    [Fact]
    public void Rejects_a_free_form_state_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":2,\"kind\":\"state\",\"state\":\"working\",\"label\":\"secret\",\"sequence\":4}"));
    }

    [Fact]
    public void Rejects_a_bad_disconnected_label()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":2,\"kind\":\"state\",\"state\":\"disconnected\",\"label\":\"secret\",\"sequence\":4}"));
    }

    [Theory]
    [InlineData("{\"version\":1,\"kind\":\"shutdown\"}")]
    [InlineData("{\"version\":2,\"kind\":\"shutdown\",\"extra\":true}")]
    [InlineData("{\"version\":2,\"kind\":\"unknown\"}")]
    public void Rejects_an_incompatible_command(string line)
    {
        Assert.Null(ProtocolReader.Parse(line));
    }
}
