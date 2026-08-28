using System.Threading;
using System.Windows.Documents;
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
}
