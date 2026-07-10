using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Charta;
using Charta.Html.Css;

namespace Charta.Html;

/// <summary>
/// Walks a parsed HTML tree and drives Charta's fluent API to lay it out. The cascade is resolved by
/// <see cref="StyleResolver"/>; anything outside the supported subset is reported through
/// <see cref="HtmlRenderOptions.OnUnsupported"/> and skipped — never thrown.
/// </summary>
internal sealed class HtmlRenderer
{
    private readonly HtmlRenderOptions _options;
    private readonly StyleResolver _resolver;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    private HtmlRenderer(HtmlRenderOptions options, StyleResolver resolver)
    {
        _options = options;
        _resolver = resolver;
    }

    public static void Render(IContainer container, string html, HtmlRenderOptions options)
    {
        var document = new HtmlParser().ParseDocument(html);
        var unsupported = new List<string>();

        var order = 0;
        var rules = new List<CssRule>();
        foreach (var styleEl in document.QuerySelectorAll("style"))
        {
            rules.AddRange(CssParser.Parse(styleEl.TextContent, order, unsupported));
            order += 10_000;
        }

        var renderer = new HtmlRenderer(options, new StyleResolver(rules, unsupported));
        foreach (var message in unsupported)
        {
            renderer.Report(message);
        }

        var root = new ComputedStyle
        {
            Display = DisplayKind.Block,
            FontSize = options.BaseFontSize,
            FontFamily = options.BaseFontFamily,
            Color = options.BaseColor,
        };

        var body = (INode?)document.Body ?? document.DocumentElement;
        renderer.RenderContainerContent(container, body.ChildNodes, root);
    }

    /// <summary>Renders a node list into a container: a single text block if all inline, else a column.</summary>
    private void RenderContainerContent(IContainer container, INodeList children, ComputedStyle style)
    {
        if (AllInline(children))
        {
            RenderInlineBlock(container, children, style);
        }
        else
        {
            container.Column(col => RenderBlockFlow(col, children, style));
        }
    }

    private void RenderBlockFlow(IColumnDescriptor col, INodeList children, ComputedStyle parentStyle)
    {
        var inlineRun = new List<INode>();

        void FlushInline()
        {
            if (inlineRun.Any(HasVisibleInline))
            {
                var run = inlineRun.ToList();
                RenderInlineBlock(col.Item(), run, parentStyle);
            }

            inlineRun.Clear();
        }

        foreach (var node in children)
        {
            if (node is IElement element)
            {
                var style = _resolver.Resolve(element, parentStyle);
                if (style.Display == DisplayKind.None)
                {
                    continue;
                }

                if (IsBlockLevel(style.Display))
                {
                    FlushInline();
                    RenderBlockElement(col, element, style);
                    continue;
                }
            }

            inlineRun.Add(node);
        }

        FlushInline();
    }

    private void RenderBlockElement(IColumnDescriptor col, IElement element, ComputedStyle style)
    {
        var tag = element.LocalName.ToLowerInvariant();
        var box = ApplyBox(col.Item(), style);

        switch (style.Display)
        {
            case DisplayKind.Table:
                RenderTable(box, element, style);
                return;
            case DisplayKind.Flex:
                RenderFlex(box, element, style);
                return;
            case DisplayKind.ListItem:
                // A stray list-item outside a list: render its content as a block.
                RenderContainerContent(box, element.ChildNodes, style);
                return;
        }

        switch (tag)
        {
            case "hr":
                box.LineHorizontal(Math.Max(style.BorderThickness, 1), style.BorderColor);
                return;
            case "ul" or "ol":
                RenderList(box, element, style);
                return;
            case "img":
                RenderImage(box, element, style);
                return;
            case "a" when element.GetAttribute("href") is { Length: > 0 } href && !href.StartsWith('#'):
                RenderContainerContent(box.Hyperlink(href), element.ChildNodes, style);
                return;
        }

        RenderContainerContent(box, element.ChildNodes, style);
    }

    private void RenderInlineBlock(IContainer container, IEnumerable<INode> nodes, ComputedStyle style)
    {
        // A run of inline content becomes one rich-text block; images inside are not representable here.
        container.Text(text =>
        {
            switch (style.TextAlign)
            {
                case TextAlign.Center: text.AlignCenter(); break;
                case TextAlign.Right: text.AlignRight(); break;
                case TextAlign.Justify: text.Justify(); break;
                default: text.AlignLeft(); break;
            }

            text.LineSpacing(style.LineHeight);
            var state = new InlineState();
            foreach (var node in nodes)
            {
                AppendInline(text, node, style, state);
            }

            if (!state.WroteAnything)
            {
                text.Span(" "); // never emit an empty text block
            }
        });
    }

    private void AppendInline(ITextContentDescriptor text, INode node, ComputedStyle style, InlineState state)
    {
        switch (node)
        {
            case IText textNode:
                // white-space: pre keeps runs of spaces and newlines; otherwise they collapse.
                var content = style.WhiteSpace == WhiteSpaceKind.Pre
                    ? textNode.Data
                    : CollapseWhitespace(textNode.Data, state);
                content = Transform(content, style.TextTransform);
                if (content.Length > 0)
                {
                    StyleSpan(text.Span(content), style);
                    state.WroteAnything = true;
                    if (style.WhiteSpace == WhiteSpaceKind.Pre)
                    {
                        state.AtLineStart = content[^1] == '\n';
                    }
                }

                break;

            case IElement element:
                var childStyle = _resolver.Resolve(element, style);
                if (childStyle.Display == DisplayKind.None)
                {
                    return;
                }

                var tag = element.LocalName.ToLowerInvariant();
                if (tag == "br")
                {
                    StyleSpan(text.Span("\n"), style);
                    state.AtLineStart = true;
                    return;
                }

                if (tag == "img")
                {
                    Report("inline <img> is not supported; place it as a block-level element");
                    return;
                }

                foreach (var child in element.ChildNodes)
                {
                    AppendInline(text, child, childStyle, state);
                }

                break;

            default:
                break;
        }
    }

    private static void StyleSpan(ITextSpanDescriptor span, ComputedStyle style)
    {
        if (MapFamily(style.FontFamily) is { } family)
        {
            span.FontFamily(family);
        }

        span.FontSize(style.FontSize).FontColor(style.Color);
        if (style.Bold)
        {
            span.Bold();
        }

        if (style.Italic)
        {
            span.Italic();
        }

        if (style.Underline)
        {
            span.Underline();
        }

        if (style.LineThrough)
        {
            span.Strikethrough();
        }

        if (style.LetterSpacing != 0)
        {
            span.LetterSpacing(style.LetterSpacing);
        }

        switch (style.VerticalAlign)
        {
            case VerticalAlignKind.Super: span.Superscript(); break;
            case VerticalAlignKind.Sub: span.Subscript(); break;
            default: break;
        }
    }

    private void RenderList(IContainer container, IElement listElement, ComputedStyle style)
    {
        var ordered = listElement.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase);
        var markerWidth = style.FontSize * 1.7;

        container.Column(col =>
        {
            var index = 1;
            foreach (var child in listElement.Children)
            {
                if (!child.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var itemStyle = _resolver.Resolve(child, style);
                if (itemStyle.Display == DisplayKind.None)
                {
                    continue;
                }

                var marker = MarkerText(itemStyle.ListStyleType, ordered, index);
                var body = child;
                col.Item().Row(row =>
                {
                    row.Spacing(2);
                    row.ConstantItem(markerWidth).Text(marker).FontSize(itemStyle.FontSize).FontColor(itemStyle.Color);
                    RenderContainerContent(row.RelativeItem(), body.ChildNodes, itemStyle);
                });
                index++;
            }
        });
    }

    private static string MarkerText(ListMarker marker, bool ordered, int index) => marker switch
    {
        ListMarker.None => string.Empty,
        ListMarker.Decimal => $"{index}.",
        ListMarker.Circle => "◦",
        ListMarker.Square => "▪",
        _ => ordered ? $"{index}." : "•",
    };

    /// <summary>
    /// Maps a flex container to a Charta Row (or Column for <c>flex-direction: column</c>). Items with
    /// an explicit width become fixed columns; the rest share the remaining space, weighted by
    /// <c>flex-grow</c> (defaulting to an equal share). Alignment/justification are not modeled.
    /// </summary>
    private void RenderFlex(IContainer container, IElement element, ComputedStyle style)
    {
        var items = new List<(IElement Element, ComputedStyle Style)>();
        foreach (var child in element.Children)
        {
            var childStyle = _resolver.Resolve(child, style);
            if (childStyle.Display != DisplayKind.None)
            {
                items.Add((child, childStyle));
            }
        }

        if (items.Count == 0)
        {
            return;
        }

        if (style.FlexDirection == FlexDirection.Column)
        {
            container.Column(col =>
            {
                if (style.Gap > 0)
                {
                    col.Spacing(style.Gap);
                }

                foreach (var (child, childStyle) in items)
                {
                    RenderContainerContent(ApplyBox(col.Item(), childStyle), child.ChildNodes, childStyle);
                }
            });
            return;
        }

        container.Row(row =>
        {
            if (style.Gap > 0)
            {
                row.Spacing(style.Gap);
            }

            foreach (var (child, childStyle) in items)
            {
                var cell = childStyle.Width is { } w
                    ? row.ConstantItem(w)
                    : row.RelativeItem(childStyle.FlexGrow > 0 ? childStyle.FlexGrow : 1);
                // Width is already consumed by ConstantItem; don't reapply it in the box.
                RenderContainerContent(ApplyBox(cell, childStyle, applyWidth: false), child.ChildNodes, childStyle);
            }
        });
    }

    private void RenderTable(IContainer container, IElement table, ComputedStyle style)
    {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var columnCount = rows.Max(r => r.Children
            .Count(c => c.LocalName is "td" or "th")
            + r.Children.Where(c => c.LocalName is "td" or "th")
                .Sum(c => Math.Max(0, ParseSpan(c, "colspan") - 1)));
        columnCount = Math.Max(1, columnCount);

        var headerRows = table.QuerySelectorAll("thead tr").ToList();
        var bodyRows = rows.Where(r => !headerRows.Contains(r)).ToList();

        container.Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < columnCount; i++)
                {
                    cols.RelativeColumn();
                }
            });

            if (headerRows.Count > 0)
            {
                t.Header(header =>
                {
                    foreach (var row in headerRows)
                    {
                        EmitRowCells(row, style, () => header.Cell());
                    }
                });
            }

            foreach (var row in bodyRows)
            {
                EmitRowCells(row, style, () => t.Cell());
            }
        });
    }

    private void EmitRowCells(IElement row, ComputedStyle parentStyle, Func<ITableCellDescriptor> newCell)
    {
        foreach (var cellEl in row.Children)
        {
            if (cellEl.LocalName is not ("td" or "th"))
            {
                continue;
            }

            var cellStyle = _resolver.Resolve(cellEl, parentStyle);
            var cell = newCell();
            var colspan = ParseSpan(cellEl, "colspan");
            var rowspan = ParseSpan(cellEl, "rowspan");
            if (colspan > 1)
            {
                cell.ColumnSpan(colspan);
            }

            if (rowspan > 1)
            {
                cell.RowSpan(rowspan);
            }

            // A default cell padding keeps text off the gridlines; author padding overrides it.
            IContainer content = cellStyle is { PaddingTop: 0, PaddingRight: 0, PaddingBottom: 0, PaddingLeft: 0 }
                ? cell.Padding(3)
                : cell;
            content = ApplyBox(content, cellStyle);
            RenderContainerContent(content, cellEl.ChildNodes, cellStyle);
        }
    }

    private void RenderImage(IContainer container, IElement element, ComputedStyle style)
    {
        var src = element.GetAttribute("src");
        var alt = element.GetAttribute("alt");
        if (LoadImage(src) is not { } bytes)
        {
            Report($"could not load <img src=\"{Truncate(src)}\">");
            return;
        }

        var target = style.Width is { } w ? container.Width(w) : container;
        target.Image(bytes, alt);
    }

    private byte[]? LoadImage(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }

        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = src.IndexOf(',', StringComparison.Ordinal);
            if (comma > 0 && src[..comma].Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Convert.FromBase64String(src[(comma + 1)..]);
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            return null;
        }

        var path = _options.BasePath is { Length: > 0 } bp && !Path.IsPathRooted(src)
            ? Path.Combine(bp, src)
            : src;

        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Wraps a container with margin, width, background, border, and padding, innermost last.</summary>
    private static IContainer ApplyBox(IContainer container, ComputedStyle style, bool applyWidth = true)
    {
        var c = container;
        if (style.MarginTop != 0 || style.MarginRight != 0 || style.MarginBottom != 0 || style.MarginLeft != 0)
        {
            c = c.Padding(style.MarginLeft, style.MarginTop, style.MarginRight, style.MarginBottom);
        }

        if (applyWidth && style.Width is { } w)
        {
            c = c.Width(w);
        }

        if (style.BackgroundColor is { } bg)
        {
            c = c.Background(bg);
        }

        if (style.BorderThickness > 0)
        {
            c = c.Border(style.BorderThickness, style.BorderColor);
        }

        if (style.PaddingTop != 0 || style.PaddingRight != 0 || style.PaddingBottom != 0 || style.PaddingLeft != 0)
        {
            c = c.Padding(style.PaddingLeft, style.PaddingTop, style.PaddingRight, style.PaddingBottom);
        }

        return c;
    }

    private static bool IsBlockLevel(DisplayKind display) => display
        is DisplayKind.Block or DisplayKind.ListItem or DisplayKind.Table or DisplayKind.InlineBlock or DisplayKind.Flex;

    private static bool AllInline(INodeList nodes)
    {
        foreach (var node in nodes)
        {
            if (node is IElement)
            {
                // Elements are inspected lazily in block flow; treat presence of any element as "maybe block".
                return false;
            }
        }

        return true;
    }

    private static bool HasVisibleInline(INode node) => node switch
    {
        IText t => t.Data.Trim().Length > 0,
        IElement => true,
        _ => false,
    };

    private static int ParseSpan(IElement element, string attribute) =>
        int.TryParse(element.GetAttribute(attribute), out var n) && n > 0 ? n : 1;

    private static string? MapFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        // Generic families have no portable mapping (Charta bundles no fonts); fall back to the
        // document's default font. Register a concrete family and name it for a specific look.
        return family.ToLowerInvariant() switch
        {
            "monospace" or "serif" or "sans-serif" or "cursive" or "fantasy"
                or "system-ui" or "ui-monospace" or "ui-sans-serif" or "ui-serif" => null,
            _ => family,
        };
    }

    /// <summary>Collapses runs of whitespace to single spaces (CSS <c>white-space: normal</c>).</summary>
    private static string CollapseWhitespace(string text, InlineState state)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && (sb.Length > 0 || !state.AtLineStart))
            {
                sb.Append(' ');
            }

            pendingSpace = false;
            sb.Append(ch);
            state.AtLineStart = false;
        }

        if (pendingSpace && !state.AtLineStart && sb.Length >= 0)
        {
            // Preserve a single trailing space so adjacent inline elements stay separated.
            sb.Append(' ');
        }

        return sb.ToString();
    }

    private static string Transform(string text, TextTransformKind transform)
    {
        switch (transform)
        {
            case TextTransformKind.Uppercase:
                return text.ToUpperInvariant();
            case TextTransformKind.Lowercase:
                return text.ToLowerInvariant();
            case TextTransformKind.Capitalize:
                var chars = text.ToCharArray();
                var atStart = true;
                for (var i = 0; i < chars.Length; i++)
                {
                    if (char.IsWhiteSpace(chars[i]))
                    {
                        atStart = true;
                    }
                    else
                    {
                        if (atStart)
                        {
                            chars[i] = char.ToUpperInvariant(chars[i]);
                        }

                        atStart = false;
                    }
                }

                return new string(chars);
            default:
                return text;
        }
    }

    private void Report(string message)
    {
        if (_options.OnUnsupported is { } sink && _reported.Add(message))
        {
            sink(message);
        }
    }

    private static string Truncate(string? s) => s is null ? string.Empty : s.Length <= 40 ? s : s[..40] + "…";

    private sealed class InlineState
    {
        public bool AtLineStart = true;

        public bool WroteAnything;
    }
}
