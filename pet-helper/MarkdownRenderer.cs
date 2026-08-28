using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using WpfInline = System.Windows.Documents.Inline;

namespace PetHelper;

/// <summary>
/// Renders markdown to a WPF FlowDocument once a message is complete.
/// Code blocks get their own copy button; streaming never passes through here.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    // Frozen so the shared brushes are safe across threads and cheap for the composition pipeline.
    private static readonly Brush CodeBackground = Frozen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)));
    private static readonly Brush QuoteBrush = Frozen(new SolidColorBrush(Color.FromArgb(200, 90, 90, 90)));
    private static readonly Brush LinkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4D, 0x6B, 0xFE)));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static FlowDocument Render(string markdown, Action<string>? copyCode = null)
    {
        var flow = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = 12.5,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            LineHeight = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(221, 0, 0, 0)),
        };

        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var block in document)
        {
            AppendBlock(flow, block, copyCode);
        }

        if (flow.Blocks.Count == 0)
        {
            flow.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }
        return flow;
    }

    private static void AppendBlock(FlowDocument flow, MdBlock block, Action<string>? copyCode)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingParagraph = new Paragraph
                {
                    FontSize = FontSizeForHeading(heading.Level),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 4, 0, 2),
                };
                headingParagraph.Inlines.AddRange(RenderInlines(heading.Inline));
                flow.Blocks.Add(headingParagraph);
                break;
            case ParagraphBlock paragraph:
                var paragraphBlock = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                paragraphBlock.Inlines.AddRange(RenderInlines(paragraph.Inline));
                flow.Blocks.Add(paragraphBlock);
                break;
            case FencedCodeBlock fenced:
                AppendCodeBlock(flow, fenced.Lines.ToString(), copyCode);
                break;
            case CodeBlock code:
                AppendCodeBlock(flow, code.Lines.ToString(), copyCode);
                break;
            case QuoteBlock quote:
                var quoteParagraph = new Paragraph
                {
                    Margin = new Thickness(8, 2, 0, 2),
                    Padding = new Thickness(8, 2, 0, 2),
                    Foreground = QuoteBrush,
                    BorderBrush = QuoteBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                };
                quoteParagraph.Inlines.AddRange(CollectQuoteInlines(quote).Select(RenderInline));
                flow.Blocks.Add(quoteParagraph);
                break;
            case ListBlock list:
                AppendList(flow, list);
                break;
            case MdTable table:
                AppendTable(flow, table);
                break;
            case ThematicBreakBlock:
                flow.Blocks.Add(new Paragraph(new Run(string.Empty))
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 4, 0, 4),
                });
                break;
            case HtmlBlock:
                break;
        }
    }

    private static void AppendCodeBlock(FlowDocument flow, string code, Action<string>? copyCode)
    {
        var codeBlock = new Border
        {
            Background = CodeBackground,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(4),
        };
        codeBlock.Child = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        };

        if (copyCode is null)
        {
            flow.Blocks.Add(new BlockUIContainer(codeBlock));
            return;
        }

        var copyButton = new Button
        {
            Content = "复制",
            Tag = code,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
        };
        copyButton.Click += (_, _) => copyCode(code);

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(copyButton, Dock.Top);
        panel.Children.Add(copyButton);
        panel.Children.Add(codeBlock);
        flow.Blocks.Add(new BlockUIContainer(panel) { Margin = new Thickness(0, 0, 0, 0) });
    }

    private static void AppendList(FlowDocument flow, ListBlock list)
    {
        var index = 1;
        foreach (var item in list)
        {
            var marker = list.IsOrdered ? $"{index}. " : "• ";
            index++;
            var paragraph = new Paragraph { Margin = new Thickness(10, 1, 0, 1) };
            paragraph.Inlines.Add(new Run(marker) { FontWeight = FontWeights.SemiBold });
            if (item is ListItemBlock itemBlock)
            {
                foreach (var child in itemBlock)
                {
                    if (child is ParagraphBlock childParagraph)
                    {
                        paragraph.Inlines.AddRange(RenderInlines(childParagraph.Inline));
                    }
                    else if (child is ListBlock nestedList)
                    {
                        foreach (var nested in nestedList)
                        {
                            if (nested is not ListItemBlock nestedItem) continue;
                            foreach (var nestedChild in nestedItem)
                            {
                                if (nestedChild is not ParagraphBlock nestedParagraph) continue;
                                paragraph.Inlines.Add(new LineBreak());
                                paragraph.Inlines.Add(new Run("  - "));
                                paragraph.Inlines.AddRange(RenderInlines(nestedParagraph.Inline));
                            }
                        }
                    }
                }
            }
            flow.Blocks.Add(paragraph);
        }
    }

    private static void AppendTable(FlowDocument flow, MdTable table)
    {
        var rows = new List<List<string>>();
        var columns = 0;
        foreach (var rowBlock in table)
        {
            if (rowBlock is not MdTableRow row || row.Count == 0) continue;
            var cells = new List<string>();
            foreach (var cellBlock in row)
            {
                cells.Add(cellBlock is MdTableCell cell ? CellText(cell) : string.Empty);
            }
            columns = Math.Max(columns, cells.Count);
            rows.Add(cells);
        }
        if (rows.Count == 0) return;

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = rows[rowIndex];
            for (var column = 0; column < columns; column++)
            {
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(4, 1, 4, 1),
                    Background = rowIndex == 0
                        ? new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
                        : null,
                };
                var text = column < cells.Count ? cells[column] : string.Empty;
                border.Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontWeight = rowIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
            }
        }
        flow.Blocks.Add(new BlockUIContainer(grid) { Margin = new Thickness(0, 2, 0, 2) });
    }

    private static string CellText(MdTableCell cell)
    {
        var text = string.Empty;
        foreach (var child in cell)
        {
            if (child is ParagraphBlock paragraph && paragraph.Inline is not null)
            {
                text += string.Concat(CollectInlines(paragraph.Inline).Select(PlainText));
            }
        }
        return text;
    }

    private static IEnumerable<WpfInline> RenderInlines(ContainerInline? inline)
    {
        if (inline is null) yield break;
        foreach (var child in CollectInlines(inline))
        {
            yield return RenderInline(child);
        }
    }

    private static IEnumerable<MdInline> CollectInlines(ContainerInline? root)
    {
        if (root is null) yield break;
        foreach (var child in root)
        {
            switch (child)
            {
                case ContainerInline container:
                    foreach (var nested in CollectInlines(container)) yield return nested;
                    break;
                default:
                    yield return child;
                    break;
            }
        }
    }

    private static WpfInline RenderInline(MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run(PlainText(literal));
            case LineBreakInline:
                return new LineBreak();
            case CodeInline code:
                return new Run(code.Content)
                {
                    FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                    FontSize = 11.5,
                    Background = CodeBackground,
                };
            case LinkInline link:
                return RenderLink(link);
            case EmphasisInline emphasis:
                var emphasisRun = new Run(PlainText(emphasis));
                if (emphasis.DelimiterChar is '*' or '_')
                {
                    if (CountEmphasis(emphasis) >= 2)
                    {
                        emphasisRun.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        emphasisRun.FontStyle = FontStyles.Italic;
                    }
                }
                return emphasisRun;
            case HtmlInline:
                return new Run(string.Empty);
            default:
                return new Run(PlainText(inline));
        }
    }

    private static WpfInline RenderLink(LinkInline link)
    {
        var text = PlainText(link);
        if (string.IsNullOrWhiteSpace(text)) text = link.Url ?? string.Empty;
        var hyperlink = new Hyperlink(new Run(text))
        {
            Foreground = LinkBrush,
            NavigateUri = Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ? uri : null,
        };
        if (hyperlink.NavigateUri is not null)
        {
            hyperlink.RequestNavigate += OpenExternalLink;
        }
        return hyperlink;
    }

    private static void OpenExternalLink(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Opening a link is best effort and never breaks the dialogue.
        }
        e.Handled = true;
    }

    private static string PlainText(MdInline inline) =>
        inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            EmphasisInline emphasis => string.Concat(CollectInlines(emphasis).Select(PlainText)),
            LinkInline link => string.Concat(CollectInlines(link).Select(PlainText)),
            _ => inline.ToString() ?? string.Empty,
        };

    private static int CountEmphasis(EmphasisInline emphasis)
    {
        var count = emphasis.DelimiterCount;
        foreach (var child in emphasis)
        {
            if (child is EmphasisInline nested) count += CountEmphasis(nested);
        }
        return count;
    }

    private static IEnumerable<MdInline> CollectQuoteInlines(QuoteBlock quote)
    {
        foreach (var child in quote)
        {
            if (child is ParagraphBlock paragraph && paragraph.Inline is not null)
            {
                foreach (var nested in CollectInlines(paragraph.Inline)) yield return nested;
            }
        }
    }

    private static double FontSizeForHeading(int level) => level switch
    {
        1 => 17,
        2 => 15.5,
        3 => 14,
        _ => 13,
    };
}
