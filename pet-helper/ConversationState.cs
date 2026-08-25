namespace PetHelper;

public sealed class ConversationState
{
    private const int MaxPreviewChars = 2000;

    public ConversationState(bool previewEnabled, int previewMaxChars)
    {
        PreviewEnabled = previewEnabled;
        PreviewMaxChars = ValidatePreviewMaxChars(previewMaxChars);
    }

    public bool PreviewEnabled { get; private set; }

    public int PreviewMaxChars { get; private set; }

    public long RequestId { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public string PreviewText { get; private set; } = string.Empty;

    public void Apply(ProtocolMessage message)
    {
        switch (message)
        {
            case ConversationConfigMessage config:
                PreviewEnabled = config.PreviewEnabled;
                PreviewMaxChars = ValidatePreviewMaxChars(config.PreviewMaxChars);
                PreviewText = PreviewEnabled ? KeepLatest(PreviewText) : string.Empty;
                break;
            case InputStatusMessage status:
                if (status.RequestId < RequestId)
                {
                    break;
                }

                var isNewerRequest = status.RequestId > RequestId;
                RequestId = status.RequestId;
                if (isNewerRequest)
                {
                    PreviewText = string.Empty;
                }
                StatusText = StatusTextFor(status.Status);
                break;
            case ReplyPreviewMessage preview when PreviewEnabled && IsCurrentOrFirst(preview.RequestId):
                SetFirstRequestId(preview.RequestId);
                PreviewText = KeepLatest(preview.Text);
                break;
            case ClearPreviewMessage clear when IsCurrentOrFirst(clear.RequestId):
                SetFirstRequestId(clear.RequestId);
                PreviewText = string.Empty;
                break;
        }
    }

    public void ClearLocalInput()
    {
        PreviewText = string.Empty;
    }

    public void BeginInput(long requestId)
    {
        if (requestId <= RequestId)
        {
            return;
        }

        RequestId = requestId;
        PreviewText = string.Empty;
        StatusText = string.Empty;
    }

    private bool IsCurrentOrFirst(long requestId) => RequestId == 0 || RequestId == requestId;

    private void SetFirstRequestId(long requestId)
    {
        if (RequestId == 0)
        {
            RequestId = requestId;
        }
    }

    private string KeepLatest(string text) =>
        text.Length <= PreviewMaxChars ? text : text[^PreviewMaxChars..];

    private static int ValidatePreviewMaxChars(int value) =>
        value is > 0 and <= MaxPreviewChars
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));

    private static string StatusTextFor(string status) => status switch
    {
        "queued" => "已排队",
        "sent" => "已发送",
        "no-default-session" => "请在 DSH 设置中选择会话",
        "session-unavailable" => "请在 DSH 设置中选择会话",
        "rejected" => "未能发送",
        _ => string.Empty,
    };
}
