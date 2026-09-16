using System.Collections.Immutable;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class QuestionnaireTests
{
    private static readonly QuestionView Single = new("q1", "选一个", ["A", "B"], false);
    [Fact]
    public void Protocol_rejects_old_version_unknown_fields_duplicates_and_empty_text()
    {
        var valid = """{"version":18,"kind":"question-request","requestId":1,"answerable":true,"questions":[{"id":"q1","question":"题目","options":["A"],"multiSelect":false}]}""";
        Assert.IsType<QuestionRequestMessage>(ProtocolReader.Parse(valid));
        foreach (var invalid in new[] { valid.Replace("18", "17"), valid.Replace("\"题目\"", "\" \""), valid.Replace("[\"A\"]", "[\"A\",\"A\"]"), valid.Replace("\"multiSelect\":false", "\"multiSelect\":false,\"extra\":true") }) Assert.Null(ProtocolReader.Parse(invalid));
    }
    [Fact]
    public void Card_retains_drafts_submits_a_whole_batch_once_and_clears_on_resolution() => Sta(() =>
    {
        var card = new QuestionnaireCard();
        var answers = new List<QuestionAnsweredEventArgs>();
        card.Answered += (_, e) => answers.Add(e);
        card.Apply(new QuestionRequestMessage(1, true, [Single, new("q2", "补充", ["C", "D"], true)]));
        Click(card.NextButton);
        Assert.Empty(answers);
        Assert.Contains("请", card.Status.Text);
        ((RadioButton)card.OptionsPanel.Children[0]).IsChecked = true;
        Click(card.NextButton);
        ((CheckBox)card.OptionsPanel.Children[0]).IsChecked = true;
        ((CheckBox)card.OptionsPanel.Children[1]).IsChecked = true;
        card.CustomInput.Text = "说明";
        Click(card.BackButton);
        Assert.True(((RadioButton)card.OptionsPanel.Children[0]).IsChecked);
        Click(card.NextButton);
        Click(card.NextButton);
        Click(card.NextButton);
        var answer = Assert.Single(answers);
        Assert.Equal(2, answer.Answers.Length);
        Assert.Equal(new[] { "C", "D" }, answer.Answers[1].Selected);
        Assert.Equal("说明", answer.Answers[1].Custom);
        using var json = JsonDocument.Parse(App.SerializeQuestionAnswer(answer));
        Assert.Equal("question-answer", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("q2", json.RootElement.GetProperty("answers")[1].GetProperty("id").GetString());
        card.Apply(new QuestionResolvedMessage(1, "answered"));
        Assert.Empty(card.OptionsPanel.Children.Cast<object>());
        Assert.Equal("", card.CustomInput.Text);
        Assert.False(card.HasPending);
    });
    [Fact]
    public void Single_choice_other_text_is_exclusive_and_mirrors_cannot_submit() => Sta(() =>
    {
        var card = new QuestionnaireCard(); int answered = 0; int cancelled = 0;
        card.Answered += (_, _) => answered++;
        card.Cancelled += (_, _) => cancelled++;
        card.Apply(new QuestionRequestMessage(2, true, [Single]));
        ((RadioButton)card.OptionsPanel.Children[0]).IsChecked = true;
        card.CustomInput.Text = "另一种";
        Assert.False(((RadioButton)card.OptionsPanel.Children[0]).IsChecked);
        card.CancelAndClear();
        Assert.Equal(1, cancelled);
        card.Apply(new QuestionRequestMessage(3, false, [Single]));
        Click(card.NextButton);
        Assert.Equal(0, answered);
        card.CancelAndClear();
        Assert.Equal(1, cancelled);
        card.Apply(new QuestionRequestMessage(2, true, [Single]));
        Assert.False(card.HasPending);
    });
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) throw error;
    }
}
