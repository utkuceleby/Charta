using AngleSharp.Dom;

namespace Charta.Html.Css;

/// <summary>One CSS declaration block bound to a simple selector, tagged with source order.</summary>
internal sealed class CssRule
{
    public required SimpleSelector Selector { get; init; }

    public required IReadOnlyDictionary<string, string> Declarations { get; init; }

    public required int Order { get; init; }
}

/// <summary>
/// A single compound selector: an optional type, any number of classes, and an optional id. No
/// combinators — descendant/child selectors are intentionally out of scope and reported as
/// diagnostics by the parser.
/// </summary>
internal sealed class SimpleSelector
{
    public string? Type { get; init; }

    public IReadOnlyList<string> Classes { get; init; } = [];

    public string? Id { get; init; }

    /// <summary>CSS specificity as (ids, classes, types) — compared left to right.</summary>
    public (int, int, int) Specificity => (Id is null ? 0 : 1, Classes.Count, Type is null ? 0 : 1);

    public bool Matches(IElement element)
    {
        if (Type is { } type && !string.Equals(element.LocalName, type, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Id is { } id && !string.Equals(element.Id, id, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var cls in Classes)
        {
            if (!element.ClassList.Contains(cls))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>A tiny CSS parser: flat rule sets of simple selectors. Enough for template stylesheets.</summary>
internal static class CssParser
{
    public static List<CssRule> Parse(string css, int startOrder, ICollection<string> unsupported)
    {
        var rules = new List<CssRule>();
        var order = startOrder;
        var text = StripComments(css);
        var i = 0;

        while (i < text.Length)
        {
            var braceOpen = text.IndexOf('{', i);
            if (braceOpen < 0)
            {
                break;
            }

            var braceClose = text.IndexOf('}', braceOpen);
            if (braceClose < 0)
            {
                break;
            }

            var selectorText = text[i..braceOpen].Trim();
            var body = text[(braceOpen + 1)..braceClose];
            i = braceClose + 1;

            if (selectorText.StartsWith('@'))
            {
                unsupported.Add($"CSS at-rule '{Head(selectorText)}' is not supported");
                continue;
            }

            var declarations = ParseDeclarations(body);
            if (declarations.Count == 0)
            {
                continue;
            }

            foreach (var selector in selectorText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ParseSelector(selector, unsupported) is { } parsed)
                {
                    rules.Add(new CssRule { Selector = parsed, Declarations = declarations, Order = order });
                }

                order++;
            }
        }

        return rules;
    }

    public static Dictionary<string, string> ParseDeclarations(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var decl in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = decl.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = decl[..colon].Trim().ToLowerInvariant();
            var value = decl[(colon + 1)..].Trim();
            // Drop !important; the cascade here is source-order + specificity only.
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^"!important".Length].Trim();
            }

            if (name.Length > 0 && value.Length > 0)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static SimpleSelector? ParseSelector(string selector, ICollection<string> unsupported)
    {
        if (selector == "*")
        {
            return new SimpleSelector();
        }

        // Combinators and pseudo-selectors are out of scope.
        if (selector.IndexOfAny([' ', '>', '+', '~', ':', '[']) >= 0)
        {
            unsupported.Add($"CSS selector '{selector}' is not supported (only type/.class/#id)");
            return null;
        }

        string? type = null;
        string? id = null;
        var classes = new List<string>();
        var j = 0;

        // Leading type name, if any.
        while (j < selector.Length && selector[j] is not ('.' or '#'))
        {
            j++;
        }

        if (j > 0)
        {
            type = selector[..j];
        }

        while (j < selector.Length)
        {
            var kind = selector[j++];
            var start = j;
            while (j < selector.Length && selector[j] is not ('.' or '#'))
            {
                j++;
            }

            var name = selector[start..j];
            if (name.Length == 0)
            {
                return null;
            }

            if (kind == '.')
            {
                classes.Add(name);
            }
            else
            {
                id = name;
            }
        }

        return new SimpleSelector { Type = type, Classes = classes, Id = id };
    }

    private static string StripComments(string css)
    {
        var start = css.IndexOf("/*", StringComparison.Ordinal);
        if (start < 0)
        {
            return css;
        }

        var sb = new System.Text.StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var open = css.IndexOf("/*", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(css, i, css.Length - i);
                break;
            }

            sb.Append(css, i, open - i);
            var close = css.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            i = close + 2;
        }

        return sb.ToString();
    }

    private static string Head(string s)
    {
        var space = s.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? s : s[..space];
    }
}
