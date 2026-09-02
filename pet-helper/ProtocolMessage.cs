using System.Collections.Immutable;

namespace PetHelper;

public abstract record ProtocolMessage(int Version, string Kind)
{
    public const int ProtocolVersion = 14;
}

public sealed record HelloMessage() : ProtocolMessage(ProtocolMessage.ProtocolVersion, "hello");

public sealed record ShutdownMessage() : ProtocolMessage(ProtocolMessage.ProtocolVersion, "shutdown");

public sealed record ConfigMessage(
    double Scale,
    bool ReducedMotion,
    string PetPlacement,
    string DialoguePlacement,
    int DialogueWidth,
    int DialogueHeight,
    bool RandomChatEnabled = false,
    bool RandomChatBrowseOnOpen = false,
    bool RandomChatConfigured = false,
    int RandomChatMinIntervalMinutes = 8,
    int RandomChatMaxIntervalMinutes = 24,
    ImmutableArray<string> RandomChatCustomPrompts = default) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "config");

public sealed record StateMessage(string State, ImmutableArray<string> Activities, string Label, long Sequence) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "state");

public sealed record ConversationConfigMessage(bool PreviewEnabled, int PreviewMaxChars, string? DefaultSessionId, string? DefaultWorkspaceId) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "conversation-config");

public sealed record InputStatusMessage(long RequestId, string Status) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "input-status");

public sealed record ReplyPreviewMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "reply-preview");

public sealed record ClearPreviewMessage(long RequestId, string Reason) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "clear-preview");

public sealed record ReplyMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "reply");

public abstract record HistoryBlock(string Type);

public sealed record HistoryTextBlock(string Text) : HistoryBlock("text");

public sealed record HistoryImageBlock(string Name, int Width, int Height) : HistoryBlock("image");

public sealed record HistoryItem(string Role, ImmutableArray<HistoryBlock> Blocks);

public sealed record HistoryMessage(long RequestId, bool Available, ImmutableArray<HistoryItem> Messages) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "conversation-history");

/** A content-free, one-shot approval prompt. No DSH identifiers or tool data reach the Helper. */
public sealed record ApprovalRequestMessage(long RequestId) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "approval-request");

public sealed record ApprovalResolvedMessage(long RequestId, string Outcome) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "approval-resolved");

public sealed record TargetWorkspaceInfo(string Id, string Title, string Path);

public sealed record TargetSessionInfo(string Id, string Title, bool Blank);

public sealed record TargetRequestMessage(
    long RequestId,
    ImmutableArray<TargetWorkspaceInfo> Workspaces,
    ImmutableDictionary<string, ImmutableArray<TargetSessionInfo>> SessionsByWorkspace,
    ImmutableArray<TargetSessionInfo> Ungrouped,
    string? DefaultWorkspaceId,
    string? DefaultSessionId,
    string? Error) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "target-request");

public sealed record RandomChatReadyMessage(long InvitationId) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "random-chat-ready");

public sealed record RandomChatErrorMessage(long InvitationId, string Reason) : ProtocolMessage(ProtocolMessage.ProtocolVersion, "random-chat-error");

public sealed record RandomChatTestMessage() : ProtocolMessage(ProtocolMessage.ProtocolVersion, "random-chat-test");
