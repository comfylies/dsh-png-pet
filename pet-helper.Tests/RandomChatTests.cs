using PetHelper;
using System.Collections.Immutable;
using Xunit;

namespace PetHelper.Tests;

public sealed class RandomChatTests
{
    [Theory]
    [InlineData(5, 5, 0, 5)]
    [InlineData(8, 24, 0, 8)]
    [InlineData(8, 24, 16, 24)]
    [InlineData(8, 24, 17, 8)]
    [InlineData(1, 5, 0, 5)]
    public void Invitation_delay_stays_in_the_configured_range(int minimum, int maximum, int randomValue, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), MainWindow.RandomChatDelayFor(minimum, maximum, randomValue));
    }

    [Fact]
    public void Reader_accepts_only_the_constrained_random_chat_protocol_messages()
    {
        Assert.Equal(
            new RandomChatReadyMessage(9),
            ProtocolReader.Parse("{\"version\":14,\"kind\":\"random-chat-ready\",\"invitationId\":9}"));
        Assert.Equal(
            new RandomChatErrorMessage(9, "not-configured"),
            ProtocolReader.Parse("{\"version\":14,\"kind\":\"random-chat-error\",\"invitationId\":9,\"reason\":\"not-configured\"}"));
        Assert.Equal(
            new RandomChatTestMessage(),
            ProtocolReader.Parse("{\"version\":14,\"kind\":\"random-chat-test\"}"));
        Assert.Null(ProtocolReader.Parse("{\"version\":14,\"kind\":\"random-chat-error\",\"invitationId\":9,\"reason\":\"free-form\"}"));
    }

    [Fact]
    public void Loads_builtin_prompts_and_combines_custom_text_locally()
    {
        var prompts = MainWindow.BuildRandomChatPrompts(ImmutableArray.Create("要不要休息一分钟，和我聊聊？"));

        Assert.Contains(prompts, prompt => prompt.Topic == "news" && prompt.Cta == "点击查阅今日热点");
        Assert.Contains(prompts, prompt => prompt.Topic == "weather" && prompt.Cta == "点击查询天气");
        Assert.Contains(prompts, prompt => prompt.Text == "要不要休息一分钟，和我聊聊？" && prompt.Topic == "discovery");
    }
}
