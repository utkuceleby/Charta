using System.Xml.Linq;

namespace Charta.Svg;

/// <summary>Resolved paint state, inherited down the SVG tree and overridden per element.</summary>
internal readonly record struct SvgStyle(Color Fill, bool HasFill, Color Stroke, bool HasStroke, double StrokeWidth)
{
    /// <summary>Applies this element's presentation attributes (and simple <c>style="..."</c>) over the inherited state.</summary>
    public SvgStyle Inherit(XElement element)
    {
        var result = this;
        var props = ParseStyleAttribute((string?)element.Attribute("style"));

        if (Resolve(element, props, "fill") is { } fillValue)
        {
            result = SvgColors.TryParse(fillValue, out var fill)
                ? result with { Fill = fill, HasFill = true }
                : result with { HasFill = false }; // fill="none"
        }

        if (Resolve(element, props, "stroke") is { } strokeValue)
        {
            result = SvgColors.TryParse(strokeValue, out var stroke)
                ? result with { Stroke = stroke, HasStroke = true }
                : result with { HasStroke = false };
        }

        if (Resolve(element, props, "stroke-width") is { } widthValue && SvgImage.TryLength(widthValue, out var width))
        {
            result = result with { StrokeWidth = width };
        }

        return result;
    }

    private static string? Resolve(XElement element, Dictionary<string, string> style, string name) =>
        style.TryGetValue(name, out var fromStyle) ? fromStyle : (string?)element.Attribute(name);

    private static Dictionary<string, string> ParseStyleAttribute(string? style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
        {
            return result;
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon > 0)
            {
                result[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
            }
        }

        return result;
    }
}
