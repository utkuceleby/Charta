using AngleSharp.Dom;

namespace Charta.Html.Css;

/// <summary>
/// Resolves an element's computed style: user-agent defaults for its tag, then author rules ordered
/// by specificity and source order, then the inline <c>style</c> attribute — all layered over the
/// properties inherited from the parent.
/// </summary>
internal sealed class StyleResolver
{
    private readonly List<CssRule> _rules;
    private readonly ICollection<string> _unsupported;

    public StyleResolver(List<CssRule> rules, ICollection<string> unsupported)
    {
        _rules = rules;
        _unsupported = unsupported;
    }

    public ComputedStyle Resolve(IElement element, ComputedStyle parent)
    {
        var style = parent.InheritForChild();
        ApplyUserAgentDefaults(element, style);

        var matched = _rules
            .Where(r => r.Selector.Matches(element))
            .OrderBy(r => r.Selector.Specificity)
            .ThenBy(r => r.Order)
            .ToList();

        foreach (var rule in matched)
        {
            Apply(rule.Declarations, style, parent);
        }

        if (element.GetAttribute("style") is { Length: > 0 } inline)
        {
            Apply(CssParser.ParseDeclarations(inline), style, parent);
        }

        return style;
    }

    private static void ApplyUserAgentDefaults(IElement element, ComputedStyle style)
    {
        var tag = element.LocalName.ToLowerInvariant();
        switch (tag)
        {
            case "h1": Heading(style, 2.0); break;
            case "h2": Heading(style, 1.5); break;
            case "h3": Heading(style, 1.17); break;
            case "h4": Heading(style, 1.0); break;
            case "h5": Heading(style, 0.83); break;
            case "h6": Heading(style, 0.75); break;

            case "p":
                style.Display = DisplayKind.Block;
                style.MarginBottom = style.FontSize * 0.7;
                break;

            case "div" or "section" or "article" or "header" or "footer" or "main" or "nav" or "figure" or "form" or "fieldset":
                style.Display = DisplayKind.Block;
                break;

            case "blockquote":
                style.Display = DisplayKind.Block;
                style.MarginLeft = style.FontSize * 2;
                style.MarginBottom = style.FontSize * 0.5;
                break;

            case "ul" or "ol":
                style.Display = DisplayKind.Block;
                style.MarginBottom = style.FontSize * 0.5;
                style.ListStyleType = tag == "ol" ? ListMarker.Decimal : ListMarker.Disc;
                break;

            case "li":
                style.Display = DisplayKind.ListItem;
                break;

            case "table": style.Display = DisplayKind.Table; break;
            case "thead" or "tbody" or "tfoot": style.Display = DisplayKind.TableRowGroup; break;
            case "tr": style.Display = DisplayKind.TableRow; break;
            case "td": style.Display = DisplayKind.TableCell; break;
            case "th":
                style.Display = DisplayKind.TableCell;
                style.Bold = true;
                style.TextAlign = TextAlign.Center;
                break;

            case "hr": style.Display = DisplayKind.Block; break;
            case "img": style.Display = DisplayKind.InlineBlock; break;

            case "b" or "strong": style.Bold = true; break;
            case "i" or "em" or "cite" or "var": style.Italic = true; break;
            case "u" or "ins": style.Underline = true; break;
            case "s" or "del" or "strike": style.LineThrough = true; break;
            case "small": style.FontSize *= 0.83; break;
            case "sub": style.VerticalAlign = VerticalAlignKind.Sub; break;
            case "sup": style.VerticalAlign = VerticalAlignKind.Super; break;
            case "a":
                style.Underline = true;
                style.Color = new Charta.Color(0, 0, 238);
                break;
            case "code" or "kbd" or "samp": style.FontFamily = "monospace"; break;
            case "pre":
                style.Display = DisplayKind.Block;
                style.FontFamily = "monospace";
                style.MarginBottom = style.FontSize * 0.5;
                break;
            default: break;
        }
    }

    private static void Heading(ComputedStyle style, double scale)
    {
        style.Display = DisplayKind.Block;
        style.Bold = true;
        style.FontSize *= scale;
        style.MarginTop = style.FontSize * 0.35;
        style.MarginBottom = style.FontSize * 0.35;
    }

    private void Apply(IReadOnlyDictionary<string, string> declarations, ComputedStyle style, ComputedStyle parent)
    {
        foreach (var (name, value) in declarations)
        {
            ApplyOne(name, value, style, parent);
        }
    }

    private void ApplyOne(string name, string value, ComputedStyle style, ComputedStyle parent)
    {
        var em = style.FontSize;
        switch (name)
        {
            case "display":
                style.Display = value.ToLowerInvariant() switch
                {
                    "block" => DisplayKind.Block,
                    "inline" => DisplayKind.Inline,
                    "inline-block" => DisplayKind.InlineBlock,
                    "list-item" => DisplayKind.ListItem,
                    "table" => DisplayKind.Table,
                    "table-row-group" or "table-header-group" or "table-footer-group" => DisplayKind.TableRowGroup,
                    "table-row" => DisplayKind.TableRow,
                    "table-cell" => DisplayKind.TableCell,
                    "none" => DisplayKind.None,
                    _ => style.Display,
                };
                break;

            case "font-size":
                if (CssValues.ParseLength(value, parent.FontSize, parent.FontSize) is { } fs)
                {
                    style.FontSize = fs;
                }

                break;

            case "font-family":
                style.FontFamily = FirstFamily(value);
                break;

            case "font-weight":
                style.Bold = value.ToLowerInvariant() switch
                {
                    "bold" or "bolder" => true,
                    "normal" or "lighter" => false,
                    _ => int.TryParse(value, out var w) ? w >= 600 : style.Bold,
                };
                break;

            case "font-style":
                style.Italic = value.ToLowerInvariant() is "italic" or "oblique";
                break;

            case "font":
                _unsupported.Add("CSS 'font' shorthand is not supported; use the longhand properties");
                break;

            case "color":
                if (CssValues.ParseColor(value) is { } c)
                {
                    style.Color = c;
                }

                break;

            case "background-color" or "background":
                if (CssValues.ParseColor(value) is { } bg)
                {
                    style.BackgroundColor = bg;
                }
                else if (name == "background")
                {
                    _unsupported.Add("CSS 'background' shorthand supports only a solid color");
                }

                break;

            case "text-align":
                style.TextAlign = value.ToLowerInvariant() switch
                {
                    "center" => TextAlign.Center,
                    "right" or "end" => TextAlign.Right,
                    "justify" => TextAlign.Justify,
                    _ => TextAlign.Left,
                };
                break;

            case "text-decoration" or "text-decoration-line":
                var deco = value.ToLowerInvariant();
                style.Underline = deco.Contains("underline", StringComparison.Ordinal);
                style.LineThrough = deco.Contains("line-through", StringComparison.Ordinal);
                break;

            case "letter-spacing":
                style.LetterSpacing = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : CssValues.ParseLength(value, em) ?? style.LetterSpacing;
                break;

            case "line-height":
                style.LineHeight = ParseLineHeight(value, em) ?? style.LineHeight;
                break;

            case "vertical-align":
                style.VerticalAlign = value.ToLowerInvariant() switch
                {
                    "super" => VerticalAlignKind.Super,
                    "sub" => VerticalAlignKind.Sub,
                    _ => VerticalAlignKind.Baseline,
                };
                break;

            case "width":
                style.Width = CssValues.ParseLength(value, em);
                if (style.Width is null && value.Contains('%', StringComparison.Ordinal))
                {
                    _unsupported.Add("CSS percentage 'width' is not supported");
                }

                break;

            case "list-style-type" or "list-style":
                style.ListStyleType = value.ToLowerInvariant() switch
                {
                    var v when v.Contains("circle", StringComparison.Ordinal) => ListMarker.Circle,
                    var v when v.Contains("square", StringComparison.Ordinal) => ListMarker.Square,
                    var v when v.Contains("decimal", StringComparison.Ordinal) => ListMarker.Decimal,
                    var v when v.Contains("none", StringComparison.Ordinal) => ListMarker.None,
                    _ => ListMarker.Disc,
                };
                break;

            case "border" or "border-top" or "border-bottom" or "border-left" or "border-right":
                ApplyBorderShorthand(value, style);
                break;

            case "border-width": style.BorderThickness = CssValues.ParseLength(value, em) ?? style.BorderThickness; break;
            case "border-color": style.BorderColor = CssValues.ParseColor(value) ?? style.BorderColor; break;

            case "margin": ApplyBox(value, em, (t, r, b, l) => (style.MarginTop, style.MarginRight, style.MarginBottom, style.MarginLeft) = (t, r, b, l)); break;
            case "margin-top": style.MarginTop = CssValues.ParseLength(value, em) ?? style.MarginTop; break;
            case "margin-right": style.MarginRight = CssValues.ParseLength(value, em) ?? style.MarginRight; break;
            case "margin-bottom": style.MarginBottom = CssValues.ParseLength(value, em) ?? style.MarginBottom; break;
            case "margin-left": style.MarginLeft = CssValues.ParseLength(value, em) ?? style.MarginLeft; break;

            case "padding": ApplyBox(value, em, (t, r, b, l) => (style.PaddingTop, style.PaddingRight, style.PaddingBottom, style.PaddingLeft) = (t, r, b, l)); break;
            case "padding-top": style.PaddingTop = CssValues.ParseLength(value, em) ?? style.PaddingTop; break;
            case "padding-right": style.PaddingRight = CssValues.ParseLength(value, em) ?? style.PaddingRight; break;
            case "padding-bottom": style.PaddingBottom = CssValues.ParseLength(value, em) ?? style.PaddingBottom; break;
            case "padding-left": style.PaddingLeft = CssValues.ParseLength(value, em) ?? style.PaddingLeft; break;

            default:
                break;
        }
    }

    private static void ApplyBorderShorthand(string value, ComputedStyle style)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CssValues.ParseLength(token, style.FontSize) is { } len)
            {
                style.BorderThickness = len;
            }
            else if (CssValues.ParseColor(token) is { } col)
            {
                style.BorderColor = col;
            }
        }

        if (style.BorderThickness == 0)
        {
            style.BorderThickness = 1; // "border: solid" with no width still draws a hairline.
        }
    }

    private static void ApplyBox(string value, double em, Action<double, double, double, double> set)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => CssValues.ParseLength(p, em) ?? 0)
            .ToArray();

        double t, r, b, l;
        switch (parts.Length)
        {
            case 1: t = r = b = l = parts[0]; break;
            case 2: t = b = parts[0]; r = l = parts[1]; break;
            case 3: t = parts[0]; r = l = parts[1]; b = parts[2]; break;
            case >= 4: t = parts[0]; r = parts[1]; b = parts[2]; l = parts[3]; break;
            default: return;
        }

        set(t, r, b, l);
    }

    private static double? ParseLineHeight(string value, double em)
    {
        if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return 1.2;
        }

        // A unitless number is a direct multiplier; a length is divided by the font size.
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mult))
        {
            return mult;
        }

        return CssValues.ParseLength(value, em) is { } len && em > 0 ? len / em : null;
    }

    private static string FirstFamily(string value)
    {
        var first = value.Split(',')[0].Trim().Trim('"', '\'');
        return first;
    }
}
