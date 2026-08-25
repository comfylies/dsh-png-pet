using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class ProtocolReaderTests
{
    [Fact]
    public void Parses_shutdown_command()
    {
        var message = ProtocolReader.Parse("{\"version\":1,\"kind\":\"shutdown\"}");

        Assert.NotNull(message);
        Assert.Equal("shutdown", message!.Kind);
    }

    [Fact]
    public void Ignores_an_unknown_command()
    {
        Assert.Null(ProtocolReader.Parse("{\"version\":1,\"kind\":\"unknown\"}"));
    }
}
