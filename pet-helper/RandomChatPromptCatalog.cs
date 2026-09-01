using System.Reflection;
using System.Text.Json;

namespace PetHelper;

internal sealed record RandomChatPrompt(string Topic, string Text, string Cta);

internal static class RandomChatPromptCatalog
{
    private const string ResourceName = "PetHelper.Assets.random-chat-prompts.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<RandomChatPrompt> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("random chat prompt resource is missing");
        var document = JsonSerializer.Deserialize<RandomChatPromptDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("random chat prompt resource is invalid");
        var prompts = document.Invitations?.Where(IsValid).ToArray() ?? [];
        if (prompts.Length == 0) throw new InvalidOperationException("random chat prompt resource is empty");
        return prompts;
    }

    private static bool IsValid(RandomChatPrompt prompt) =>
        prompt.Topic is "news" or "discovery"
        && prompt.Text is { Length: > 0 and <= 120 }
        && prompt.Cta is { Length: > 0 and <= 80 };

    private sealed record RandomChatPromptDocument(int Version, RandomChatPrompt[]? Invitations);
}
