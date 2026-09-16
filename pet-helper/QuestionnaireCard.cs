using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PetHelper;

public sealed record QuestionAnswerItem(string Id, ImmutableArray<string> Selected, string? Custom);
public sealed class QuestionAnsweredEventArgs(long requestId, ImmutableArray<QuestionAnswerItem> answers) : EventArgs
{
    public long RequestId { get; } = requestId;
    public ImmutableArray<QuestionAnswerItem> Answers { get; } = answers;
}
public sealed class QuestionCancelledEventArgs(long requestId) : EventArgs
{
    public long RequestId { get; } = requestId;
}

/// <summary>A transient, paginated form. No question or draft is ever written to disk.</summary>
public sealed class QuestionnaireCard : Border
{
    private sealed class Draft { public HashSet<string> Selected = new(StringComparer.Ordinal); public string Custom = ""; }
    private QuestionRequestMessage? request;
    private Draft[] drafts = [];
    private int page;
    private long lastRequestId;
    private bool updating;
    private bool submitted;
    private readonly TextBlock heading = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock questionText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
    private readonly StackPanel content = new();
    private readonly WrapPanel navigation = new();
    private readonly Button openHarness = Button("打开 DSH");
    public StackPanel OptionsPanel { get; } = new();
    public TextBox CustomInput { get; } = new() { MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 32, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), Foreground = Brushes.DarkSlateGray };
    public Button BackButton { get; } = Button("上一题");
    public Button NextButton { get; } = Button("下一题");
    public Button CancelButton { get; } = Button("取消");
    public bool HasPending => request is not null;
    public event EventHandler<QuestionAnsweredEventArgs>? Answered;
    public event EventHandler<QuestionCancelledEventArgs>? Cancelled;
    public event EventHandler? OpenHarness;
    public event EventHandler? Changed;

    public QuestionnaireCard()
    {
        Visibility = Visibility.Collapsed;
        Padding = new Thickness(10); Margin = new Thickness(0, 0, 0, 8);
        CornerRadius = new CornerRadius(12);
        Background = new SolidColorBrush(Color.FromRgb(244, 241, 255));
        BorderBrush = new SolidColorBrush(Color.FromRgb(184, 166, 219)); BorderThickness = new Thickness(1);
        var root = new StackPanel(); Child = root;
        root.Children.Add(heading);
        content.Children.Add(questionText); content.Children.Add(OptionsPanel);
        content.Children.Add(new TextBlock { Text = "其他回答（可输入）", Margin = new Thickness(0, 6, 0, 3) });
        content.Children.Add(CustomInput);
        root.Children.Add(new ScrollViewer { Content = content, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        root.Children.Add(Status); root.Children.Add(navigation);
        foreach (var button in new[] { BackButton, NextButton, CancelButton, openHarness }) navigation.Children.Add(button);
        BackButton.Click += (_, _) => { if (request is null || submitted || page == 0) return; page--; RenderPage(); };
        NextButton.Click += (_, _) => Next();
        CancelButton.Click += (_, _) => CancelAndClear();
        openHarness.Click += (_, _) => OpenHarness?.Invoke(this, EventArgs.Empty);
        CustomInput.TextChanged += (_, _) =>
        {
            if (updating || submitted || request is not { Answerable: true }) return;
            drafts[page].Custom = CustomInput.Text;
            if (!request.Questions[page].MultiSelect && !string.IsNullOrWhiteSpace(CustomInput.Text))
            {
                drafts[page].Selected.Clear(); updating = true;
                foreach (var option in OptionsPanel.Children.OfType<RadioButton>()) option.IsChecked = false;
                updating = false;
            }
        };
    }

    public void Apply(ProtocolMessage message)
    {
        if (message is QuestionRequestMessage incoming)
        {
            if (incoming.RequestId <= lastRequestId || incoming.Questions.IsDefaultOrEmpty) return;
            Clear(); lastRequestId = incoming.RequestId; request = incoming;
            drafts = incoming.Questions.Select(_ => new Draft()).ToArray(); page = 0; submitted = false;
            Visibility = Visibility.Visible; RenderPage();
        }
        else if (message is QuestionResolvedMessage resolved && request?.RequestId == resolved.RequestId)
        {
            Clear(); heading.Text = "问卷";
            Status.Text = resolved.Outcome switch { "answered" => "已回答", "cancelled" => "已取消或过期", _ => "请求已结束" };
            Visibility = Visibility.Visible;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CancelAndClear()
    {
        var id = request is { Answerable: true } current && !submitted ? current.RequestId : (long?)null;
        Clear(); Visibility = Visibility.Collapsed;
        if (id is { } value) Cancelled?.Invoke(this, new(value));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Clear()
    {
        request = null; drafts = []; submitted = false;
        content.IsEnabled = navigation.IsEnabled = true;
        updating = true; CustomInput.Clear(); OptionsPanel.Children.Clear(); questionText.Text = ""; updating = false;
        content.Visibility = navigation.Visibility = Visibility.Collapsed; Status.Text = "";
    }

    private void RenderPage()
    {
        if (request is null) return;
        updating = true;
        content.Visibility = navigation.Visibility = Visibility.Visible;
        var q = request.Questions[page]; var draft = drafts[page];
        heading.Text = $"问卷 · {page + 1}/{request.Questions.Length}";
        questionText.Text = q.Question; OptionsPanel.Children.Clear();
        foreach (var label in q.Options)
        {
            ToggleButton option = q.MultiSelect ? new CheckBox() : new RadioButton { GroupName = $"question-{GetHashCode()}" };
            option.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
            option.Margin = new Thickness(0, 3, 0, 3); option.IsChecked = draft.Selected.Contains(label);
            option.IsEnabled = request.Answerable && !submitted;
            option.Checked += (_, _) =>
            {
                if (updating || submitted) return;
                if (!q.MultiSelect) { draft.Selected.Clear(); draft.Custom = ""; updating = true; CustomInput.Clear(); updating = false; }
                draft.Selected.Add(label);
            };
            option.Unchecked += (_, _) => { if (!updating) draft.Selected.Remove(label); };
            OptionsPanel.Children.Add(option);
        }
        CustomInput.Text = draft.Custom; CustomInput.IsEnabled = request.Answerable && !submitted;
        BackButton.IsEnabled = page > 0 && !submitted;
        NextButton.Content = page == request.Questions.Length - 1 ? "提交" : "下一题";
        NextButton.Visibility = request.Answerable || page < request.Questions.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.IsEnabled = !submitted;
        CancelButton.Content = request.Answerable ? "取消" : "收起";
        CancelButton.IsEnabled = !submitted;
        Status.Text = request.Answerable ? (q.MultiSelect ? "可多选，也可补充文字" : "单选，或填写其他回答") : "请在 DSH Web 回答，结果将同步到这里";
        updating = false;
    }

    private void Next()
    {
        if (request is null || submitted) return;
        if (request.Answerable && !Valid(page)) { Status.Text = "请选择选项或填写回答"; return; }
        if (page < request.Questions.Length - 1) { page++; RenderPage(); return; }
        if (!request.Answerable) return;
        for (var i = 0; i < drafts.Length; i++) if (!Valid(i)) { page = i; RenderPage(); Status.Text = "请完成这道题"; return; }
        var answers = request.Questions.Select((q, i) => new QuestionAnswerItem(q.Id,
            q.Options.Where(drafts[i].Selected.Contains).ToImmutableArray(),
            string.IsNullOrWhiteSpace(drafts[i].Custom) ? null : drafts[i].Custom)).ToImmutableArray();
        submitted = true; content.IsEnabled = false; navigation.IsEnabled = false; Status.Text = "正在提交…";
        Answered?.Invoke(this, new(request.RequestId, answers));
    }
    private bool Valid(int index)
    {
        var d = drafts[index]; var text = d.Custom;
        var choices = d.Selected.Count + (string.IsNullOrWhiteSpace(text) ? 0 : 1);
        return choices > 0 && (request!.Questions[index].MultiSelect || choices == 1) && text.Length <= 1000
            && !text.Any(c => c < 32 && c is not ('\t' or '\r' or '\n') || c == 127);
    }
    private static Button Button(string label) => new() { Content = label, MinWidth = 56, MinHeight = 28, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 5, 5, 0) };
}
