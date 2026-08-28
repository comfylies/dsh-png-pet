using System.Collections.Immutable;
using System.ComponentModel;

namespace PetHelper;

public enum MessageEndState
{
    None,
    Stopped,
    Interrupted,
    Failed,
}

public sealed record DialogueImage(string? Name, int? Width, int? Height, string? DataBase64);

public sealed record DialogueFile(string Name, string Path);

/// <summary>
/// One message in the dialogue's message list. Instances are stable while
/// streaming mutates <see cref="Text"/>/<see cref="Streaming"/>/<see cref="End"/>,
/// so the UI never rebuilds the whole list for one delta.
/// </summary>
public sealed class DialogueMessage : INotifyPropertyChanged
{
    private string text;
    private bool streaming;
    private MessageEndState end;

    public DialogueMessage(
        long id,
        string role,
        string text,
        ImmutableArray<DialogueImage> images,
        ImmutableArray<DialogueFile> files,
        bool streaming = false,
        MessageEndState end = MessageEndState.None)
    {
        Id = id;
        Role = role;
        this.text = text;
        Images = images;
        Files = files;
        this.streaming = streaming;
        this.end = end;
    }

    public long Id { get; }

    public string Role { get; }

    public string Text
    {
        get => text;
        set
        {
            if (text == value) return;
            text = value;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(ShowMarkdown));
        }
    }

    public ImmutableArray<DialogueImage> Images { get; }

    public ImmutableArray<DialogueFile> Files { get; }

    public bool Streaming
    {
        get => streaming;
        set
        {
            if (streaming == value) return;
            streaming = value;
            OnPropertyChanged(nameof(Streaming));
            OnPropertyChanged(nameof(ShowStreaming));
            OnPropertyChanged(nameof(ShowMarkdown));
        }
    }

    public MessageEndState End
    {
        get => end;
        set
        {
            if (end == value) return;
            end = value;
            OnPropertyChanged(nameof(End));
            OnPropertyChanged(nameof(ShowMarkdown));
            OnPropertyChanged(nameof(ShowEnd));
            OnPropertyChanged(nameof(EndLabel));
        }
    }

    /// <summary>Plain-text streaming surface (no markdown while streaming).</summary>
    public bool ShowStreaming => streaming;

    /// <summary>Markdown-rendered surface once the final text is available (partial text included).</summary>
    public bool ShowMarkdown => !streaming && text.Length > 0;

    public bool ShowEnd => end != MessageEndState.None;

    public string EndLabel => end switch
    {
        MessageEndState.Stopped => "已停止",
        MessageEndState.Interrupted => "已中断",
        MessageEndState.Failed => "生成失败",
        _ => string.Empty,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
