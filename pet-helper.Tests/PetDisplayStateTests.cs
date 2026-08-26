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

    [Theory]
    [InlineData("idle", "", PetAnimationKey.Idle)]
    [InlineData("active", "思考中…", PetAnimationKey.Thinking)]
    [InlineData("active", "工作中…", PetAnimationKey.Working)]
    [InlineData("active", "思考中/工作中", PetAnimationKey.ThinkingWorking)]
    [InlineData("waiting", "等待你的操作", PetAnimationKey.Waiting)]
    [InlineData("success", "已完成", PetAnimationKey.Success)]
    [InlineData("error", "发生错误", PetAnimationKey.Error)]
    [InlineData("disconnected", "未连接", PetAnimationKey.Disconnected)]
    public void Maps_every_valid_display_state_to_an_animation_key(string state, string label, PetAnimationKey expected)
    {
        var activities = state == "active"
            ? label == "思考中/工作中" ? new[] { "thinking", "working" }
            : label == "思考中…" ? new[] { "thinking" } : new[] { "working" }
            : Array.Empty<string>();

        Assert.Equal(expected, PetDisplayState.From(state, activities, label, 1).AnimationKey);
    }

    [Fact]
    public void Maps_an_invalid_display_state_to_disconnected_animation()
    {
        Assert.Equal(
            PetAnimationKey.Disconnected,
            PetDisplayState.From("active", new[] { "working" }, "secret", 4).AnimationKey);
    }
}
