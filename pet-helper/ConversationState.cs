using System.Collections.Immutable;
using System.Linq;

namespace PetHelper;

/// <summary>
/// Pure reducer over the conversation protocol. Keeps a bounded message list
/// (user/assistant) where only the current assistant message streams: the
/// streaming text is buffered in <see cref="PendingStreamText"/> so the UI can
/// throttle flushes, and terminal endings (stopped/interrupted/failed) never
/// leave the dialogue blank.
/// </summary>
public sealed class ConversationState
{
    private const int MaxPreviewChars = 8000;
    private const int MaxMessages = 40;

    private string? lastDefaultSessionId;

    public ConversationState(bool previewEnabled, int previewMaxChars)
    {
        PreviewEnabled = previewEnabled;
        PreviewMaxChars = ValidatePreviewMaxChars(previewMaxChars);
    }

    public bool PreviewEnabled { get; private set; }

    public int PreviewMaxChars { get; private set; }

    public long RequestId { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public bool HasActiveTurn { get; private set; }

    public bool HasStreamingMessage { get; private set; }

    /// <summary>Latest buffered streaming text; the UI flushes it to the message on a throttle tick.</summary>
    public string? PendingStreamText { get; private set; }

    public ImmutableArray<DialogueMessage> Messages { get; private set; } = [];

    public bool HistoryAvailable { get; private set; }

    public void Apply(ProtocolMessage message)
    {
        switch (message)
        {
            case ConversationConfigMessage config:
                PreviewEnabled = config.PreviewEnabled;
                PreviewMaxChars = ValidatePreviewMaxChars(config.PreviewMaxChars);
                if (config.DefaultSessionId != lastDefaultSessionId)
                {
                    lastDefaultSessionId = config.DefaultSessionId;
                    ResetConversation();
                }
                break;
            case InputStatusMessage status:
                if (status.RequestId < RequestId)
                {
                    break;
                }

                RequestId = status.RequestId;
                StatusText = StatusTextFor(status.Status);
                switch (status.Status)
                {
                    case "queued" or "sent":
                        HasActiveTurn = true;
                        break;
                    case "stopped" or "interrupted" or "failed":
                        HasActiveTurn = false;
                        FinalizeAssistant(status.RequestId, status.Status);
                        break;
                    default:
                        HasActiveTurn = false;
                        break;
                }
                break;
            case ReplyMessage reply when IsCurrentOrFirst(reply.RequestId):
                SetFirstRequestId(reply.RequestId);
                HasActiveTurn = false;
                HasStreamingMessage = false;
                PendingStreamText = null;
                var replyMessage = FindOrAddAssistant(reply.RequestId);
                replyMessage.Streaming = false;
                replyMessage.End = MessageEndState.None;
                replyMessage.Text = reply.Text;
                break;
            case ReplyPreviewMessage preview when PreviewEnabled && IsCurrentOrFirst(preview.RequestId):
                SetFirstRequestId(preview.RequestId);
                HasActiveTurn = true;
                var previewMessage = FindOrAddAssistant(preview.RequestId);
                previewMessage.Streaming = !preview.Completed;
                HasStreamingMessage = !preview.Completed;
                PendingStreamText = KeepLatest(preview.Text);
                if (preview.Completed)
                {
                    previewMessage.Text = KeepLatest(preview.Text);
                    PendingStreamText = null;
                }
                break;
            case ClearPreviewMessage clear when IsCurrentOrFirst(clear.RequestId):
                SetFirstRequestId(clear.RequestId);
                HasActiveTurn = false;
                HasStreamingMessage = false;
                if (FindAssistant(clear.RequestId) is { } clearedMessage)
                {
                    clearedMessage.Streaming = false;
                }
                break;
            case HistoryMessage history:
                HistoryAvailable = history.Available;
                HasActiveTurn = false;
                HasStreamingMessage = false;
                PendingStreamText = null;
                Messages = ToDialogueMessages(history);
                StatusText = !history.Available
                    ? "会话不可用"
                    : history.Messages.Length == 0
                        ? "暂无对话历史"
                        : string.Empty;
                break;
        }
    }

    public void ClearLocalInput()
    {
        PendingStreamText = null;
    }

    /// <summary>Echoes the user's own message (text, images, files) before the host confirms it.</summary>
    public void BeginInput(long requestId, string text, ImmutableArray<DialogueImage> images, ImmutableArray<DialogueFile> files)
    {
        if (requestId <= RequestId)
        {
            return;
        }

        RequestId = requestId;
        StatusText = "正在发送…";
        HasActiveTurn = true;
        HasStreamingMessage = false;
        PendingStreamText = null;
        Messages = AppendMessage(new DialogueMessage(requestId, "user", text, images, files));
    }

    private void ResetConversation()
    {
        RequestId = 0;
        StatusText = string.Empty;
        HasActiveTurn = false;
        HasStreamingMessage = false;
        PendingStreamText = null;
        Messages = [];
        HistoryAvailable = false;
    }

    private void FinalizeAssistant(long requestId, string status)
    {
        var end = status switch
        {
            "stopped" => MessageEndState.Stopped,
            "interrupted" => MessageEndState.Interrupted,
            _ => MessageEndState.Failed,
        };
        var message = FindAssistant(requestId);
        if (message is not null)
        {
            message.Streaming = false;
            message.End = end;
        }
        else
        {
            // Terminal endings must never leave the dialogue blank.
            Messages = AppendMessage(new DialogueMessage(requestId, "assistant", string.Empty, [], [], end: end));
        }
        HasStreamingMessage = false;
    }

    private DialogueMessage? FindAssistant(long requestId) =>
        Messages.FirstOrDefault(message => message.Id == requestId && message.Role == "assistant");

    private DialogueMessage FindOrAddAssistant(long requestId)
    {
        if (FindAssistant(requestId) is { } existing) return existing;
        var message = new DialogueMessage(requestId, "assistant", string.Empty, [], []);
        Messages = AppendMessage(message);
        return message;
    }

    private ImmutableArray<DialogueMessage> AppendMessage(DialogueMessage message)
    {
        var next = Messages.Add(message);
        return next.Length <= MaxMessages ? next : next.RemoveAt(0);
    }

    private static ImmutableArray<DialogueMessage> ToDialogueMessages(HistoryMessage history)
    {
        if (!history.Available || history.Messages.IsEmpty) return [];

        var builder = ImmutableArray.CreateBuilder<DialogueMessage>(history.Messages.Length);
        for (var index = 0; index < history.Messages.Length; index++)
        {
            var item = history.Messages[index];
            var text = string.Concat(item.Blocks.OfType<HistoryTextBlock>().Select(block => block.Text));
            var images = item.Blocks
                .OfType<HistoryImageBlock>()
                .Select(block => new DialogueImage(block.Name, block.Width, block.Height, null))
                .ToImmutableArray();
            builder.Add(new DialogueMessage(-(index + 1), item.Role, text, images, []));
        }
        return builder.ToImmutable();
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
        "session-unavailable" => "会话不可用",
        "rejected" => "未能发送",
        "stopped" => "已停止",
        "interrupted" => "已中断",
        "failed" => "生成失败",
        _ => string.Empty,
    };
}
