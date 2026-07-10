using System.Globalization;
using Charta;

namespace Charta.Html.Css;

/// <summary>Parsers for the CSS value types Charta.Html understands: colors and lengths.</summary>
internal static class CssValues
{
    private const double PxToPt = 72.0 / 96.0; // CSS px is 1/96 in; a PDF point is 1/72 in.

    /// <summary>
    /// Parses a length to points. Relative units resolve against <paramref name="emSize"/> (the
    /// current font size, in points) and <paramref name="percentBasis"/> (for percentages).
    /// Returns null for unitless non-zero values or anything unrecognized.
    /// </summary>
    public static double? ParseLength(string? value, double emSize, double? percentBasis = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();
        if (text == "0")
        {
            return 0;
        }

        if (text.EndsWith('%'))
        {
            return percentBasis is { } basis && TryNumber(text[..^1], out var pct) ? basis * pct / 100.0 : null;
        }

        foreach (var (suffix, factor) in Units)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal) && TryNumber(text[..^suffix.Length], out var n))
            {
                return suffix switch
                {
                    "em" or "rem" => n * emSize,
                    _ => n * factor,
                };
            }
        }

        return null;
    }

    // Order matters: "rem" before "em", longer suffixes first where ambiguous.
    private static readonly (string Suffix, double Factor)[] Units =
    [
        ("px", PxToPt),
        ("pt", 1.0),
        ("rem", 0.0),
        ("em", 0.0),
        ("in", 72.0),
        ("cm", 72.0 / 2.54),
        ("mm", 72.0 / 25.4),
        ("pc", 12.0),
    ];

    private static bool TryNumber(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Parses a CSS color: #rgb, #rrggbb, rgb()/rgba(), or a named color. Alpha is dropped.</summary>
    public static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().ToLowerInvariant();

        if (text.StartsWith('#'))
        {
            return ParseHex(text[1..]);
        }

        if (text.StartsWith("rgb", StringComparison.Ordinal))
        {
            var open = text.IndexOf('(');
            var close = text.IndexOf(')');
            if (open < 0 || close < open)
            {
                return null;
            }

            var parts = text[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is 3 or 4
                && TryComponent(parts[0], out var r)
                && TryComponent(parts[1], out var g)
                && TryComponent(parts[2], out var b))
            {
                return new Color(r, g, b);
            }

            return null;
        }

        return Named.TryGetValue(text, out var named) ? named : null;
    }

    private static Color? ParseHex(string hex)
    {
        if (hex.Length is 3 or 4)
        {
            // Shorthand: each digit is doubled.
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length is 6 or 8
            && byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new Color(r, g, b);
        }

        return null;
    }

    private static bool TryComponent(string part, out byte value)
    {
        value = 0;
        if (part.EndsWith('%') && TryNumber(part[..^1], out var pct))
        {
            value = (byte)Math.Clamp(Math.Round(pct * 255.0 / 100.0), 0, 255);
            return true;
        }

        if (TryNumber(part, out var n))
        {
            value = (byte)Math.Clamp(Math.Round(n), 0, 255);
            return true;
        }

        return false;
    }

    // A compact, common subset of CSS named colors. Unknown names fall through to a diagnostic.
    private static readonly Dictionary<string, Color> Named = new(StringComparer.Ordinal)
    {
        ["black"] = new(0, 0, 0),
        ["white"] = new(255, 255, 255),
        ["red"] = new(255, 0, 0),
        ["green"] = new(0, 128, 0),
        ["lime"] = new(0, 255, 0),
        ["blue"] = new(0, 0, 255),
        ["yellow"] = new(255, 255, 0),
        ["cyan"] = new(0, 255, 255),
        ["aqua"] = new(0, 255, 255),
        ["magenta"] = new(255, 0, 255),
        ["fuchsia"] = new(255, 0, 255),
        ["gray"] = new(128, 128, 128),
        ["grey"] = new(128, 128, 128),
        ["silver"] = new(192, 192, 192),
        ["maroon"] = new(128, 0, 0),
        ["olive"] = new(128, 128, 0),
        ["navy"] = new(0, 0, 128),
        ["teal"] = new(0, 128, 128),
        ["purple"] = new(128, 0, 128),
        ["orange"] = new(255, 165, 0),
        ["pink"] = new(255, 192, 203),
        ["brown"] = new(165, 42, 42),
        ["gold"] = new(255, 215, 0),
        ["lightgray"] = new(211, 211, 211),
        ["lightgrey"] = new(211, 211, 211),
        ["darkgray"] = new(169, 169, 169),
        ["darkgrey"] = new(169, 169, 169),
        ["whitesmoke"] = new(245, 245, 245),
        ["transparent"] = new(255, 255, 255),
    };
}
