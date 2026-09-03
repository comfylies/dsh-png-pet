using System.Collections.Immutable;
using System.Text.Json;

namespace PetHelper;

public static class ProtocolReader
{
    private const long MaxSafeSequence = 9_007_199_254_740_991;
    private const int MaxLineLength = 16_000_000;
    private const int MaxTextLength = 2000;
    private const int MaxPreviewLength = 8000;
    private const int MaxReplyLength = 8000;
    private const int MaxHistoryMessages = 20;
    private const int MaxHistoryBlocks = 8;
    private const int MaxHistoryImageNameLength = 200;
    private const int MaxHistoryImageDimension = 100000;
    private const int MinPreviewChars = 80;
    private const int MaxWorkspaces = 64;
    private const int MaxSessionsPerWorkspace = 100;
    private const int MaxUngroupedSessions = 100;
    private const int MaxTargetIdLength = 200;
    private const int MaxTargetTitleLength = 200;
    private const int MaxTargetPathLength = 2048;
    private const int MinRandomChatIntervalMinutes = 5;
    private const int MaxRandomChatIntervalMinutes = 1440;
    private const int MaxRandomChatCustomPrompts = 12;
    private const int MaxRandomChatCustomPromptLength = 120;

    public static ProtocolMessage? Parse(string line)
    {
        if (line.Length > MaxLineLength) return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasVersionEight(root)) return null;

            return root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                ? kind.GetString() switch
                {
                    "hello" when HasExactlyProperties(root, "version", "kind") => new HelloMessage(),
                    "shutdown" when HasExactlyProperties(root, "version", "kind") => new ShutdownMessage(),
                    "config" => ParseConfig(root),
                    "state" => ParseState(root),
                    "conversation-config" => ParseConversationConfig(root),
                    "input-status" => ParseInputStatus(root),
                    "reply-preview" => ParseReplyPreview(root),
                    "clear-preview" => ParseClearPreview(root),
                    "reply" => ParseReply(root),
                    "conversation-history" => ParseHistory(root),
                    "approval-request" => ParseApprovalRequest(root),
                    "approval-resolved" => ParseApprovalResolved(root),
                    "target-request" => ParseTargetRequest(root),
                    "random-chat-ready" => ParseRandomChatReady(root),
                    "random-chat-error" => ParseRandomChatError(root),
                    "random-chat-test" when HasExactlyProperties(root, "version", "kind") => new RandomChatTestMessage(),
                    _ => null,
                }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ConversationConfigMessage? ParseConversationConfig(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "previewEnabled", "previewMaxChars", "defaultSessionId", "defaultWorkspaceId")
            || !root.TryGetProperty("previewEnabled", out var previewEnabled)
            || !root.TryGetProperty("previewMaxChars", out var previewMaxChars)
            || !root.TryGetProperty("defaultSessionId", out var defaultSessionId)
            || !root.TryGetProperty("defaultWorkspaceId", out var defaultWorkspaceId)
            || previewEnabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || previewMaxChars.ValueKind != JsonValueKind.Number
            || !previewMaxChars.TryGetInt32(out var maxChars)
            || maxChars is < MinPreviewChars or > MaxPreviewLength
            || (defaultSessionId.ValueKind != JsonValueKind.Null
                && (defaultSessionId.ValueKind != JsonValueKind.String
                    || defaultSessionId.GetString() is not { Length: > 0 }))
            || (defaultWorkspaceId.ValueKind != JsonValueKind.Null
                && (defaultWorkspaceId.ValueKind != JsonValueKind.String
                    || defaultWorkspaceId.GetString() is not { Length: > 0 })))
        {
            return null;
        }

        return new ConversationConfigMessage(
            previewEnabled.GetBoolean(),
            maxChars,
            defaultSessionId.ValueKind == JsonValueKind.Null ? null : defaultSessionId.GetString(),
            defaultWorkspaceId.ValueKind == JsonValueKind.Null ? null : defaultWorkspaceId.GetString());
    }

    private static TargetRequestMessage? ParseTargetRequest(JsonElement root)
    {
        if (!HasPropertiesWithOptional(root, ["version", "kind", "requestId", "workspaces", "sessionsByWorkspace", "ungrouped", "defaultWorkspaceId", "defaultSessionId"], ["error"])
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("workspaces", out var workspaces)
            || !root.TryGetProperty("sessionsByWorkspace", out var sessionsByWorkspace)
            || !root.TryGetProperty("ungrouped", out var ungrouped)
            || !root.TryGetProperty("defaultWorkspaceId", out var defaultWorkspaceId)
            || !root.TryGetProperty("defaultSessionId", out var defaultSessionId)
            || workspaces.ValueKind != JsonValueKind.Array
            || sessionsByWorkspace.ValueKind != JsonValueKind.Object
            || ungrouped.ValueKind != JsonValueKind.Array
            || (defaultWorkspaceId.ValueKind != JsonValueKind.Null
                && (defaultWorkspaceId.ValueKind != JsonValueKind.String
                    || defaultWorkspaceId.GetString() is not { Length: > 0 and <= MaxTargetIdLength }))
            || (defaultSessionId.ValueKind != JsonValueKind.Null
                && (defaultSessionId.ValueKind != JsonValueKind.String
                    || defaultSessionId.GetString() is not { Length: > 0 and <= MaxTargetIdLength })))
        {
            return null;
        }

        var error = root.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.String
            && errorElement.GetString() is { Length: > 0 and <= MaxTargetTitleLength } errorText
            ? errorText
            : null;
        if (root.TryGetProperty("error", out var rawError) && rawError.ValueKind != JsonValueKind.Null && error is null)
        {
            return null;
        }

        var workspaceItems = new List<TargetWorkspaceInfo>();
        foreach (var entry in workspaces.EnumerateArray())
        {
            if (workspaceItems.Count >= MaxWorkspaces
                || entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var id)
                || !entry.TryGetProperty("title", out var title)
                || !entry.TryGetProperty("path", out var path)
                || id.ValueKind != JsonValueKind.String
                || id.GetString() is not { Length: > 0 and <= MaxTargetIdLength } idText
                || title.ValueKind != JsonValueKind.String
                || title.GetString() is not { Length: > 0 and <= MaxTargetTitleLength } titleText
                || path.ValueKind != JsonValueKind.String
                || path.GetString() is not { Length: > 0 and <= MaxTargetPathLength } pathText)
            {
                return null;
            }

            workspaceItems.Add(new TargetWorkspaceInfo(idText, titleText, pathText));
        }
        if (workspaceItems.Count > MaxWorkspaces) return null;

        var byWorkspace = new Dictionary<string, ImmutableArray<TargetSessionInfo>>(StringComparer.Ordinal);
        foreach (var property in sessionsByWorkspace.EnumerateObject())
        {
            if (property.Name.Length is 0 or > MaxTargetIdLength
                || property.Value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sessions = ParseTargetSessions(property.Value, MaxSessionsPerWorkspace);
            if (sessions is null) return null;
            byWorkspace[property.Name] = sessions.Value;
        }

        var ungroupedSessions = ParseTargetSessions(ungrouped, MaxUngroupedSessions);
        if (ungroupedSessions is null) return null;

        return new TargetRequestMessage(
            requestId,
            workspaceItems.ToImmutableArray(),
            byWorkspace.ToImmutableDictionary(StringComparer.Ordinal),
            ungroupedSessions.Value,
            defaultWorkspaceId.ValueKind == JsonValueKind.Null ? null : defaultWorkspaceId.GetString(),
            defaultSessionId.ValueKind == JsonValueKind.Null ? null : defaultSessionId.GetString(),
            error);
    }

    private static ImmutableArray<TargetSessionInfo>? ParseTargetSessions(JsonElement array, int limit)
    {
        var items = new List<TargetSessionInfo>();
        foreach (var entry in array.EnumerateArray())
        {
            if (items.Count >= limit
                || entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var id)
                || !entry.TryGetProperty("title", out var title)
                || !entry.TryGetProperty("blank", out var blank)
                || id.ValueKind != JsonValueKind.String
                || id.GetString() is not { Length: > 0 and <= MaxTargetIdLength } idText
                || title.ValueKind != JsonValueKind.String
                || title.GetString() is not { Length: <= MaxTargetTitleLength } titleText
                || blank.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return null;
            }

            items.Add(new TargetSessionInfo(idText, titleText, blank.GetBoolean()));
        }
        return items.ToImmutableArray();
    }

    private static InputStatusMessage? ParseInputStatus(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "status")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() is not string statusText
            || statusText is not ("queued" or "sent" or "no-default-session" or "session-unavailable" or "rejected" or "stopped" or "interrupted" or "failed"))
        {
            return null;
        }

        return new InputStatusMessage(requestId, statusText);
    }

    private static ReplyPreviewMessage? ParseReplyPreview(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "text", "completed")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("text", out var text)
            || !root.TryGetProperty("completed", out var completed)
            || text.ValueKind != JsonValueKind.String
            || text.GetString() is not { } previewText
            || previewText.Length is 0 or > MaxPreviewLength
            || completed.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return null;
        }

        return new ReplyPreviewMessage(requestId, previewText, completed.GetBoolean());
    }

    private static ClearPreviewMessage? ParseClearPreview(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "reason")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("reason", out var reason)
            || reason.ValueKind != JsonValueKind.String
            || reason.GetString() is not string reasonText
            || reasonText is not ("disabled" or "next-input" or "cancelled" or "closed" or "session-unavailable"))
        {
            return null;
        }

        return new ClearPreviewMessage(requestId, reasonText);
    }

    private static ReplyMessage? ParseReply(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "text", "completed")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("text", out var text)
            || !root.TryGetProperty("completed", out var completed)
            || text.ValueKind != JsonValueKind.String
            || text.GetString() is not { } replyText
            || replyText.Length is 0 or > MaxReplyLength
            || completed.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return null;
        }

        return new ReplyMessage(requestId, replyText, completed.GetBoolean());
    }

    private static HistoryMessage? ParseHistory(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "available", "messages")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("available", out var available)
            || !root.TryGetProperty("messages", out var messages)
            || available.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<HistoryItem>();
        foreach (var entry in messages.EnumerateArray())
        {
            if (items.Count >= MaxHistoryMessages
                || entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("role", out var role)
                || !entry.TryGetProperty("blocks", out var blocks)
                || role.ValueKind != JsonValueKind.String
                || role.GetString() is not ("user" or "assistant")
                || blocks.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsedBlocks = ParseHistoryBlocks(blocks);
            if (parsedBlocks is null) return null;
            items.Add(new HistoryItem(role.GetString()!, parsedBlocks.Value));
        }
        if (items.Count > MaxHistoryMessages) return null;

        return new HistoryMessage(requestId, available.GetBoolean(), items.ToImmutableArray());
    }

    private static ImmutableArray<HistoryBlock>? ParseHistoryBlocks(JsonElement array)
    {
        var blocks = new List<HistoryBlock>();
        foreach (var block in array.EnumerateArray())
        {
            if (blocks.Count >= MaxHistoryBlocks
                || block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            switch (type.GetString())
            {
                case "text" when block.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String
                    && text.GetString() is { Length: > 0 and <= MaxTextLength } textValue:
                    blocks.Add(new HistoryTextBlock(textValue));
                    break;
                case "image" when block.TryGetProperty("name", out var name)
                    && block.TryGetProperty("width", out var width)
                    && block.TryGetProperty("height", out var height)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() is { Length: <= MaxHistoryImageNameLength } nameValue
                    && width.ValueKind == JsonValueKind.Number
                    && width.TryGetInt32(out var widthValue)
                    && widthValue is >= 1 and <= MaxHistoryImageDimension
                    && height.ValueKind == JsonValueKind.Number
                    && height.TryGetInt32(out var heightValue)
                    && heightValue is >= 1 and <= MaxHistoryImageDimension:
                    blocks.Add(new HistoryImageBlock(nameValue, widthValue, heightValue));
                    break;
                default:
                    return null;
            }
        }
        return blocks.Count == 0 ? null : blocks.ToImmutableArray();
    }

    private static ConfigMessage? ParseConfig(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "scale", "reducedMotion", "physicsEnabled", "physicsBouncePercent", "petPlacement", "dialoguePlacement", "dialogueWidth", "dialogueHeight", "randomChatEnabled", "randomChatBrowseOnOpen", "randomChatConfigured", "randomChatMinIntervalMinutes", "randomChatMaxIntervalMinutes", "randomChatCustomPrompts")
            || !root.TryGetProperty("scale", out var scale)
            || !root.TryGetProperty("reducedMotion", out var reducedMotion)
            || !root.TryGetProperty("physicsEnabled", out var physicsEnabled)
            || !root.TryGetProperty("physicsBouncePercent", out var physicsBouncePercent)
            || !root.TryGetProperty("petPlacement", out var petPlacement)
            || !root.TryGetProperty("dialoguePlacement", out var dialoguePlacement)
            || !root.TryGetProperty("dialogueWidth", out var dialogueWidth)
            || !root.TryGetProperty("dialogueHeight", out var dialogueHeight)
            || !root.TryGetProperty("randomChatEnabled", out var randomChatEnabled)
            || !root.TryGetProperty("randomChatBrowseOnOpen", out var randomChatBrowseOnOpen)
            || !root.TryGetProperty("randomChatConfigured", out var randomChatConfigured)
            || !root.TryGetProperty("randomChatMinIntervalMinutes", out var randomChatMinIntervalMinutes)
            || !root.TryGetProperty("randomChatMaxIntervalMinutes", out var randomChatMaxIntervalMinutes)
            || !root.TryGetProperty("randomChatCustomPrompts", out var randomChatCustomPrompts)
            || scale.ValueKind != JsonValueKind.Number
            || !scale.TryGetDouble(out var value)
            || reducedMotion.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || physicsEnabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || physicsBouncePercent.ValueKind != JsonValueKind.Number
            || !physicsBouncePercent.TryGetInt32(out var physicsBouncePercentValue)
            || physicsBouncePercentValue is < 0 or > 100
            || petPlacement.ValueKind != JsonValueKind.String
            || petPlacement.GetString() is not { } petPlacementValue
            || !DefaultLayout.IsPetPlacement(petPlacementValue)
            || dialoguePlacement.ValueKind != JsonValueKind.String
            || dialoguePlacement.GetString() is not { } dialoguePlacementValue
            || !DefaultLayout.IsDialoguePlacement(dialoguePlacementValue)
            || dialogueWidth.ValueKind != JsonValueKind.Number
            || !dialogueWidth.TryGetInt32(out var dialogueWidthValue)
            || dialogueWidthValue is < 220 or > 4000
            || dialogueHeight.ValueKind != JsonValueKind.Number
            || !dialogueHeight.TryGetInt32(out var dialogueHeightValue)
            || dialogueHeightValue is < 240 or > 3000
            || randomChatEnabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || randomChatBrowseOnOpen.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || randomChatConfigured.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || randomChatMinIntervalMinutes.ValueKind != JsonValueKind.Number
            || !randomChatMinIntervalMinutes.TryGetInt32(out var randomChatMinIntervalMinutesValue)
            || randomChatMinIntervalMinutesValue is < MinRandomChatIntervalMinutes or > MaxRandomChatIntervalMinutes
            || randomChatMaxIntervalMinutes.ValueKind != JsonValueKind.Number
            || !randomChatMaxIntervalMinutes.TryGetInt32(out var randomChatMaxIntervalMinutesValue)
            || randomChatMaxIntervalMinutesValue is < MinRandomChatIntervalMinutes or > MaxRandomChatIntervalMinutes
            || randomChatMinIntervalMinutesValue > randomChatMaxIntervalMinutesValue
            || !TryParseRandomChatCustomPrompts(randomChatCustomPrompts, out var randomChatCustomPromptsValue)
            || value is not (0.75d or 1d or 1.25d or 1.5d))
        {
            return null;
        }

        return new ConfigMessage(value, reducedMotion.GetBoolean(), physicsEnabled.GetBoolean(), physicsBouncePercentValue, petPlacementValue, dialoguePlacementValue, dialogueWidthValue, dialogueHeightValue, randomChatEnabled.GetBoolean(), randomChatBrowseOnOpen.GetBoolean(), randomChatConfigured.GetBoolean(), randomChatMinIntervalMinutesValue, randomChatMaxIntervalMinutesValue, randomChatCustomPromptsValue);
    }

    private static ApprovalRequestMessage? ParseApprovalRequest(JsonElement root)
    {
        return HasExactlyProperties(root, "version", "kind", "requestId")
            && TryGetRequestId(root, out var requestId)
            ? new ApprovalRequestMessage(requestId)
            : null;
    }

    private static ApprovalResolvedMessage? ParseApprovalResolved(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "outcome")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("outcome", out var outcome)
            || outcome.ValueKind != JsonValueKind.String
            || outcome.GetString() is not string outcomeText
            || outcomeText is not ("allowed-once" or "rejected" or "cancelled" or "unavailable"))
        {
            return null;
        }

        return new ApprovalResolvedMessage(requestId, outcomeText);
    }

    private static bool TryParseRandomChatCustomPrompts(JsonElement value, out ImmutableArray<string> prompts)
    {
        prompts = ImmutableArray<string>.Empty;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MaxRandomChatCustomPrompts) return false;
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var prompt in value.EnumerateArray())
        {
            if (prompt.ValueKind != JsonValueKind.String
                || prompt.GetString() is not { Length: > 0 and <= MaxRandomChatCustomPromptLength } text
                || text.Contains('\n') || text.Contains('\r') || builder.Contains(text))
            {
                return false;
            }
            builder.Add(text);
        }
        prompts = builder.ToImmutable();
        return true;
    }

    private static StateMessage? ParseState(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "state", "activities", "label", "sequence")
            || !root.TryGetProperty("state", out var state)
            || !root.TryGetProperty("activities", out var activities)
            || !root.TryGetProperty("label", out var label)
            || !root.TryGetProperty("sequence", out var sequence)
            || state.ValueKind != JsonValueKind.String
            || activities.ValueKind != JsonValueKind.Array
            || label.ValueKind != JsonValueKind.String
            || sequence.ValueKind != JsonValueKind.Number
            || !sequence.TryGetInt64(out var value)
            || value < 0
            || value > MaxSafeSequence)
        {
            return null;
        }

        var activityItems = new List<string>();
        foreach (var activity in activities.EnumerateArray())
        {
            if (activity.ValueKind != JsonValueKind.String || activity.GetString() is not { } activityText)
            {
                return null;
            }

            activityItems.Add(activityText);
        }

        var stateText = state.GetString();
        var labelText = label.GetString();
        var immutableActivities = activityItems.ToImmutableArray();
        var displayState = PetDisplayState.From(stateText, immutableActivities, labelText, value);
        return displayState.State == stateText
            && displayState.Label == labelText
            && displayState.Sequence == value
            ? new StateMessage(displayState.State, immutableActivities, displayState.Label, displayState.Sequence)
            : null;
    }

    private static bool TryGetRequestId(JsonElement root, out long requestId)
    {
        requestId = 0;
        return root.TryGetProperty("requestId", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out requestId)
            && requestId is > 0 and <= MaxSafeSequence;
    }

    private static RandomChatReadyMessage? ParseRandomChatReady(JsonElement root)
    {
        return HasExactlyProperties(root, "version", "kind", "invitationId")
            && TryGetInvitationId(root, out var invitationId)
            ? new RandomChatReadyMessage(invitationId)
            : null;
    }

    private static RandomChatErrorMessage? ParseRandomChatError(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "invitationId", "reason")
            || !TryGetInvitationId(root, out var invitationId)
            || !root.TryGetProperty("reason", out var reason)
            || reason.ValueKind != JsonValueKind.String
            || reason.GetString() is not string reasonText
            || reasonText is not ("not-configured" or "unavailable"))
        {
            return null;
        }
        return new RandomChatErrorMessage(invitationId, reasonText);
    }

    private static bool HasVersionEight(JsonElement root) =>
        root.TryGetProperty("version", out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out var value)
        && value == ProtocolMessage.ProtocolVersion;

    private static bool TryGetInvitationId(JsonElement root, out long invitationId)
    {
        invitationId = 0;
        return root.TryGetProperty("invitationId", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out invitationId)
            && invitationId is > 0 and <= MaxSafeSequence;
    }

    private static bool HasExactlyProperties(JsonElement root, params string[] expected)
    {
        var names = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Remove(property.Name)) return false;
        }
        return names.Count == 0;
    }

    private static bool HasPropertiesWithOptional(JsonElement root, string[] required, string[] optional)
    {
        var allowed = new HashSet<string>(required, StringComparer.Ordinal);
        allowed.UnionWith(optional);
        var missing = new HashSet<string>(required, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) return false;
            missing.Remove(property.Name);
        }
        return missing.Count == 0;
    }
}
