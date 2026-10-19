using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Center.Controls;

/// <summary>
/// Tiny Markdown renderer (bold, italic, inline code, fenced code blocks, headings,
/// lists, links). Built for the chat transcript.
///
/// It is a plain StackPanel: no reflection, no bindings inside, no dynamic XAML.
/// Cost: it is only rebuilt when the message is finished (the live stream is shown
/// by a plain TextBlock), so streaming stays O(n) and flicker-free.
/// </summary>
public sealed partial class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private static readonly FontFamily MonoFont = new("Consolas");

    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#ECECEC"));
    private static readonly IBrush StrongBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#D7D7D7"));
    private static readonly IBrush CodeBgBrush = new SolidColorBrush(Color.Parse("#0E0E0E"));
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.Parse("#242424"));
    private static readonly IBrush InlineCodeBrush = new SolidColorBrush(Color.Parse("#E7D9BF"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#9CC2FF"));

    private bool _dirty = true;

    static MarkdownView()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnMarkdownChanged());
        IsVisibleProperty.Changed.AddClassHandler<MarkdownView>((view, _) =>
        {
            if (view.IsVisible)
                view.Rebuild();
        });
    }

    public MarkdownView()
    {
        Spacing = 10;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private void OnMarkdownChanged()
    {
        _dirty = true;
        if (IsVisible)
            Rebuild();
    }

    private void Rebuild()
    {
        if (!_dirty)
            return;

        _dirty = false;
        Children.Clear();

        var text = Markdown;
        if (string.IsNullOrEmpty(text))
            return;

        BuildDocument(text.Replace("\r\n", "\n").Replace('\r', '\n'));
    }

    // ------------------------------------------------------------------
    // Block level
    // ------------------------------------------------------------------

    private void BuildDocument(string text)
    {
        var lines = text.Split('\n');
        var paragraph = new List<string>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index].TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph(paragraph);
                index++;
                continue;
            }

            // fenced code block
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                var language = trimmed[3..].Trim();
                index++;

                var code = new StringBuilder();
                while (index < lines.Length &&
                       !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[index].TrimEnd());
                    index++;
                }

                index++; // closing fence
                Children.Add(CreateCodeBlock(language, code.ToString().TrimEnd('\n')));
                continue;
            }

            // headings
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                Children.Add(CreateHeading(trimmed[4..], 15));
                index++;
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                Children.Add(CreateHeading(trimmed[3..], 17));
                index++;
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                Children.Add(CreateHeading(trimmed[2..], 21));
                index++;
                continue;
            }

            // horizontal rule
            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph(paragraph);
                Children.Add(new Border
                {
                    Height = 1,
                    Background = DividerBrush,
                    Margin = new Thickness(0, 6)
                });
                index++;
                continue;
            }

            // lists
            if (IsBullet(trimmed))
            {
                FlushParagraph(paragraph);
                Children.Add(CreateList(ReadList(lines, ref index, ordered: false), ordered: false));
                continue;
            }

            if (TryParseOrdered(trimmed, out _))
            {
                FlushParagraph(paragraph);
                Children.Add(CreateList(ReadList(lines, ref index, ordered: true), ordered: true));
                continue;
            }

            paragraph.Add(trimmed);
            index++;
        }

        FlushParagraph(paragraph);
    }

    private void FlushParagraph(List<string> lines)
    {
        if (lines.Count == 0)
            return;

        var text = string.Join(" ", lines);
        lines.Clear();
        Children.Add(CreateRichText(text));
    }

    private Control CreateHeading(string text, double fontSize)
    {
        var heading = CreateRichText(text, fontSize, FontWeight.SemiBold, StrongBrush);
        heading.Margin = new Thickness(0, 8, 0, 0);
        return heading;
    }

    private static bool IsBullet(string text)
        => text.StartsWith("- ", StringComparison.Ordinal)
           || text.StartsWith("* ", StringComparison.Ordinal)
           || text.StartsWith("• ", StringComparison.Ordinal);

    private static bool TryParseOrdered(string text, out string content)
    {
        content = string.Empty;

        var i = 0;
        while (i < text.Length && char.IsDigit(text[i]))
            i++;

        if (i == 0 || i + 2 >= text.Length)
            return false;

        if (text[i] != '.' && text[i] != ')')
            return false;

        if (text[i + 1] != ' ')
            return false;

        content = text[(i + 2)..];
        return content.Length > 0;
    }

    private static List<string> ReadList(string[] lines, ref int index, bool ordered)
    {
        var items = new List<string>();

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
                break;

            string? content = null;

            if (ordered)
            {
                if (TryParseOrdered(trimmed, out var parsed))
                    content = parsed;
            }
            else if (IsBullet(trimmed))
            {
                content = trimmed[2..];
            }

            if (content is null)
            {
                // Continuation line of the previous bullet.
                if (items.Count > 0 && (line.StartsWith("  ", StringComparison.Ordinal) ||
                                        line.StartsWith("\t", StringComparison.Ordinal)))
                {
                    items[^1] = items[^1] + " " + trimmed;
                    index++;
                    continue;
                }

                break;
            }

            items.Add(content);
            index++;
        }

        return items;
    }

    private Control CreateList(List<string> items, bool ordered)
    {
        var stack = new StackPanel { Spacing = 6 };

        for (var i = 0; i < items.Count; i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };

            Control marker = ordered
                ? new TextBlock
                {
                    Text = $"{i + 1}.",
                    FontSize = 13.5,
                    Foreground = MutedBrush
                }
                : new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = MutedBrush,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(6, 9, 0, 0)
                };

            var content = CreateRichText(items[i]);
            Grid.SetColumn(content, 1);

            row.Children.Add(marker);
            row.Children.Add(content);
            stack.Children.Add(row);
        }

        return stack;
    }

    // ------------------------------------------------------------------
    // Code block
    // ------------------------------------------------------------------

    private Control CreateCodeBlock(string language, string code)
    {
        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "code" : language.ToLowerInvariant(),
            FontSize = 11,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copy = new Button
        {
            Content = "Copier",
            FontSize = 11,
            Padding = new Thickness(8, 2),
            MinHeight = 0,
            MinWidth = 0,
            Background = Brushes.Transparent,
            Foreground = MutedBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        copy.Click += async (_, _) => await CopyAsync(code);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        header.Children.Add(label);
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);

        var codeText = new SelectableTextBlock
        {
            Text = code,
            FontFamily = MonoFont,
            FontSize = 12.5,
            Foreground = CodeBrush,
            TextWrapping = TextWrapping.NoWrap
        };

        var scroller = new ScrollViewer
        {
            Content = codeText,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(scroller);

        return new Border
        {
            Background = CodeBgBrush,
            BorderBrush = DividerBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = content
        };
    }

    private async Task CopyAsync(string code)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(code);
    }

    // ------------------------------------------------------------------
    // Inline level
    // ------------------------------------------------------------------

    private static TextBlock CreateRichText(
        string text,
        double fontSize = 14,
        FontWeight? weight = null,
        IBrush? brush = null)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            LineHeight = Math.Round(fontSize * 1.55),
            Foreground = brush ?? TextBrush
        };

        if (weight.HasValue)
            block.FontWeight = weight.Value;

        AppendInlines(block, text);
        return block;
    }

    private static void AppendInlines(TextBlock block, string text)
    {
        var inlines = block.Inlines;
        if (inlines is null)
        {
            block.Text = text;
            return;
        }

        var buffer = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    Flush(inlines, buffer);
                    inlines.Add(new Run(text.Substring(i + 1, end - i - 1))
                    {
                        FontFamily = MonoFont,
                        Foreground = InlineCodeBrush
                    });
                    i = end + 1;
                    continue;
                }
            }
            else if (c == '*')
            {
                if (i + 1 < text.Length && text[i + 1] == '*')
                {
                    var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i + 2)
                    {
                        Flush(inlines, buffer);
                        inlines.Add(new Run(text.Substring(i + 2, end - i - 2))
                        {
                            FontWeight = FontWeight.SemiBold,
                            Foreground = StrongBrush
                        });
                        i = end + 2;
                        continue;
                    }
                }
                else
                {
                    var end = text.IndexOf('*', i + 1);
                    if (end > i + 1)
                    {
                        Flush(inlines, buffer);
                        inlines.Add(new Run(text.Substring(i + 1, end - i - 1))
                        {
                            FontStyle = FontStyle.Italic
                        });
                        i = end + 1;
                        continue;
                    }
                }
            }
            else if (c == '[')
            {
                // [label](url): the label is highlighted; real clickable links
                // are the ones listed in the Sources panel.
                var close = text.IndexOf(']', i + 1);
                if (close > i + 1 && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var end = text.IndexOf(')', close + 2);
                    if (end > close)
                    {
                        Flush(inlines, buffer);
                        inlines.Add(new Run(text.Substring(i + 1, close - i - 1))
                        {
                            Foreground = LinkBrush
                        });
                        i = end + 1;
                        continue;
                    }
                }
            }

            buffer.Append(c);
            i++;
        }

        Flush(inlines, buffer);
    }

    private static void Flush(InlineCollection inlines, StringBuilder buffer)
    {
        if (buffer.Length == 0)
            return;

        inlines.Add(new Run(buffer.ToString()) { Foreground = TextBrush });
        buffer.Clear();
    }
}
