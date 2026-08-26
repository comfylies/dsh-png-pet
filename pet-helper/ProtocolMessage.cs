using System.Collections.Immutable;

namespace PetHelper;

public abstract record ProtocolMessage(int Version, string Kind);

public sealed record HelloMessage() : ProtocolMessage(5, "hello");

public sealed record ShutdownMessage() : ProtocolMessage(5, "shutdown");

public sealed record ConfigMessage(double Scale, bool ReducedMotion) : ProtocolMessage(5, "config");

public sealed record StateMessage(string State, ImmutableArray<string> Activities, string Label, long Sequence) : ProtocolMessage(5, "state");

public sealed record ConversationConfigMessage(bool PreviewEnabled, int PreviewMaxChars, string? DefaultSessionId) : ProtocolMessage(5, "conversation-config");

public sealed record InputStatusMessage(long RequestId, string Status) : ProtocolMessage(5, "input-status");

public sealed record ReplyPreviewMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(5, "reply-preview");

public sealed record ClearPreviewMessage(long RequestId, string Reason) : ProtocolMessage(5, "clear-preview");

public sealed record ReplyMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(5, "reply");

public sealed record HistoryItem(string Role, string Text);

public sealed record HistoryMessage(long RequestId, bool Available, ImmutableArray<HistoryItem> Messages) : ProtocolMessage(5, "conversation-history");
