using System.Collections.Immutable;

namespace PetHelper;

public abstract record InputAttachment;

public sealed record ImageInputAttachment(string MediaType, string Base64, string Name) : InputAttachment;

public sealed record FileInputAttachment(string Path, string Name) : InputAttachment;

public sealed class InputSubmittedEventArgs(long requestId, string text, ImmutableArray<InputAttachment> attachments) : EventArgs
{
    public long RequestId { get; } = requestId;

    public string Text { get; } = text;

    public ImmutableArray<InputAttachment> Attachments { get; } = attachments;
}

public sealed class HistoryRequestedEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}

public sealed class StopRequestedEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}
