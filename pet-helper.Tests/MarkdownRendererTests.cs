using System.Threading;
using System.Windows.Documents;
using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void Renders_markdown_to_a_flow_document()
    {
        FlowDocument? result = null;
        var thread = new Thread(() => result = MarkdownRenderer.Render("**粗体** 与 `代码`\n\n- 列表项\n\n```csharp\nvar x = 1;\n```"));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.NotNull(result);
        Assert.True(result!.Blocks.Count > 0);
    }

    [Fact]
    public void Renders_empty_markdown_without_throwing()
    {
        FlowDocument? result = null;
        var thread = new Thread(() => result = MarkdownRenderer.Render(string.Empty));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.NotNull(result);
        Assert.Single(result!.Blocks);
    }

    [Fact]
    public void Requests_a_refresh_when_a_completed_reply_replaces_the_stream_preview()
    {
        Assert.False(MarkdownRenderer.NeedsRender("预览末尾", "预览末尾"));
        Assert.True(MarkdownRenderer.NeedsRender("预览末尾", "这是完整回复，不能继续显示旧预览"));
    }

    [Fact]
    public void Preserves_links_and_nested_emphasis_instead_of_flattening_them()
    {
        var hasHyperlink = false;
        var hasBoldSpan = false;
        var thread = new Thread(() =>
        {
            var result = MarkdownRenderer.Render("[来源](https://example.com) 与 **粗体和 *斜体***");
            var paragraph = Assert.IsType<Paragraph>(Assert.Single(result.Blocks));
            hasHyperlink = paragraph.Inlines.Any(inline => inline is Hyperlink);
            hasBoldSpan = paragraph.Inlines.Any(inline => inline is Span span && span.FontWeight == FontWeights.Bold);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(hasHyperlink);
        Assert.True(hasBoldSpan);
    }
}
