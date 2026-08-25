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
            PetDisplayState.From("waiting", "等待你的操作", 9));
    }

    [Fact]
    public void Invalid_display_state_is_disconnected()
    {
        Assert.Equal(PetDisplayState.Disconnected, PetDisplayState.From("working", "secret", 4));
    }

    [Fact]
    public void Idle_state_has_no_bubble_label()
    {
        Assert.Equal(string.Empty, PetDisplayState.From("idle", string.Empty, 2).Label);
    }
}
