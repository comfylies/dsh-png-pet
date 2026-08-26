using System.Collections.Immutable;
using System.Text.Json;

namespace PetHelper;

public static class ProtocolReader
{
    private const long MaxSafeSequence = 9_007_199_254_740_991;
    private const int MaxLineLength = 65536;
    private const int MaxTextLength = 2000;
    private const int MaxReplyLength = 8000;
    private const int MaxHistoryMessages = 20;
    private const int MinPreviewChars = 80;

    public static ProtocolMessage? Parse(string line)
    {
        if (line.Length > MaxLineLength) return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasVersionFive(root)) return null;

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
        if (!HasExactlyProperties(root, "version", "kind", "previewEnabled", "previewMaxChars", "defaultSessionId")
            || !root.TryGetProperty("previewEnabled", out var previewEnabled)
            || !root.TryGetProperty("previewMaxChars", out var previewMaxChars)
            || !root.TryGetProperty("defaultSessionId", out var defaultSessionId)
            || previewEnabled.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || previewMaxChars.ValueKind != JsonValueKind.Number
            || !previewMaxChars.TryGetInt32(out var maxChars)
            || maxChars is < MinPreviewChars or > MaxTextLength
            || (defaultSessionId.ValueKind != JsonValueKind.Null
                && (defaultSessionId.ValueKind != JsonValueKind.String
                    || defaultSessionId.GetString() is not { Length: > 0 })))
        {
            return null;
        }

        return new ConversationConfigMessage(
            previewEnabled.GetBoolean(),
            maxChars,
            defaultSessionId.ValueKind == JsonValueKind.Null ? null : defaultSessionId.GetString());
    }

    private static InputStatusMessage? ParseInputStatus(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "requestId", "status")
            || !TryGetRequestId(root, out var requestId)
            || !root.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() is not string statusText
            || statusText is not ("queued" or "sent" or "no-default-session" or "session-unavailable" or "rejected"))
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
            || previewText.Length is 0 or > MaxTextLength
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
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("role", out var role)
                || !entry.TryGetProperty("text", out var text)
                || role.ValueKind != JsonValueKind.String
                || role.GetString() is not ("user" or "assistant")
                || text.ValueKind != JsonValueKind.String
                || text.GetString() is not { Length: > 0 and <= MaxTextLength } entryText)
            {
                return null;
            }

            items.Add(new HistoryItem(role.GetString()!, entryText));
            if (items.Count > MaxHistoryMessages) return null;
        }

        return new HistoryMessage(requestId, available.GetBoolean(), items.ToImmutableArray());
    }

    private static ConfigMessage? ParseConfig(JsonElement root)
    {
        if (!HasExactlyProperties(root, "version", "kind", "scale", "reducedMotion")
            || !root.TryGetProperty("scale", out var scale)
            || !root.TryGetProperty("reducedMotion", out var reducedMotion)
            || scale.ValueKind != JsonValueKind.Number
            || !scale.TryGetDouble(out var value)
            || reducedMotion.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || value is not (0.75d or 1d or 1.25d or 1.5d))
        {
            return null;
        }

        return new ConfigMessage(value, reducedMotion.GetBoolean());
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

    private static bool HasVersionFive(JsonElement root) =>
        root.TryGetProperty("version", out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out var value)
        && value == 5;

    private static bool HasExactlyProperties(JsonElement root, params string[] expected)
    {
        var names = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Remove(property.Name)) return false;
        }
        return names.Count == 0;
    }
}
