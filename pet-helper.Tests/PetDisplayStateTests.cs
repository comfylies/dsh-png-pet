using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetDisplayStateTests
{
    [Fact]
    public void Keeps_a_valid_fixed_label()
    {
        Assert.Equal(
            new PetDisplayState("waiting", "等待你的操作", 9),
            PetDisplayState.From("waiting", Array.Empty<string>(), "等待你的操作", 9));
    }

    [Fact]
    public void Invalid_display_state_is_disconnected()
    {
        Assert.Equal(PetDisplayState.Disconnected, PetDisplayState.From("active", new[] { "working" }, "secret", 4));
    }

    [Fact]
    public void Idle_state_has_no_bubble_label()
    {
        Assert.Equal(string.Empty, PetDisplayState.From("idle", Array.Empty<string>(), string.Empty, 2).Label);
    }

    [Fact]
    public void Keeps_a_valid_composite_activity_label()
    {
        Assert.Equal(
            new PetDisplayState("active", "思考中/工作中", 3),
            PetDisplayState.From("active", new[] { "thinking", "working" }, "思考中/工作中", 3));
    }
}
