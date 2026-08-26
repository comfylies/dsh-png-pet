namespace PetHelper;

public sealed class InputSubmittedEventArgs(long requestId, string text) : EventArgs
{
    public long RequestId { get; } = requestId;
    public string Text { get; } = text;
}

public sealed class HistoryRequestedEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}
