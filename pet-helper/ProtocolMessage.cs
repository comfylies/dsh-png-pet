using System.Collections.Immutable;

namespace PetHelper;

public abstract record ProtocolMessage(int Version, string Kind);

public sealed record HelloMessage() : ProtocolMessage(4, "hello");

public sealed record ShutdownMessage() : ProtocolMessage(4, "shutdown");

public sealed record ConfigMessage(double Scale, bool ReducedMotion) : ProtocolMessage(4, "config");

public sealed record StateMessage(string State, ImmutableArray<string> Activities, string Label, long Sequence) : ProtocolMessage(4, "state");

public sealed record ConversationConfigMessage(bool PreviewEnabled, int PreviewMaxChars) : ProtocolMessage(4, "conversation-config");

public sealed record InputStatusMessage(long RequestId, string Status) : ProtocolMessage(4, "input-status");

public sealed record ReplyPreviewMessage(long RequestId, string Text, bool Completed) : ProtocolMessage(4, "reply-preview");

public sealed record ClearPreviewMessage(long RequestId, string Reason) : ProtocolMessage(4, "clear-preview");
