using System.Globalization;

namespace Charta.Svg;

/// <summary>Parses SVG paint values: hex, rgb(), and the common named colors. Returns null for "none".</summary>
internal static class SvgColors
{
    private static readonly Dictionary<string, Color> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Color.Black,
        ["white"] = Color.White,
        ["red"] = Color.FromHex(0xFF0000),
        ["green"] = Color.FromHex(0x008000),
        ["lime"] = Color.FromHex(0x00FF00),
        ["blue"] = Color.FromHex(0x0000FF),
        ["yellow"] = Color.FromHex(0xFFFF00),
        ["cyan"] = Color.FromHex(0x00FFFF),
        ["aqua"] = Color.FromHex(0x00FFFF),
        ["magenta"] = Color.FromHex(0xFF00FF),
        ["fuchsia"] = Color.FromHex(0xFF00FF),
        ["gray"] = Color.FromHex(0x808080),
        ["grey"] = Color.FromHex(0x808080),
        ["silver"] = Color.FromHex(0xC0C0C0),
        ["maroon"] = Color.FromHex(0x800000),
        ["olive"] = Color.FromHex(0x808000),
        ["navy"] = Color.FromHex(0x000080),
        ["teal"] = Color.FromHex(0x008080),
        ["purple"] = Color.FromHex(0x800080),
        ["orange"] = Color.FromHex(0xFFA500),
        ["pink"] = Color.FromHex(0xFFC0CB),
        ["brown"] = Color.FromHex(0xA52A2A),
    };

    public static bool TryParse(string? value, out Color color)
    {
        color = Color.Black;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            }

            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                color = Color.FromHex(rgb);
                return true;
            }

            return false;
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var inner = value[(value.IndexOf('(') + 1)..value.IndexOf(')')];
            var parts = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                TryComponent(parts[0], out var r) && TryComponent(parts[1], out var g) && TryComponent(parts[2], out var b))
            {
                color = new Color(r, g, b);
                return true;
            }

            return false;
        }

        return Named.TryGetValue(value, out color);
    }

    private static bool TryComponent(string text, out byte value)
    {
        value = 0;
        if (text.EndsWith('%') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            value = (byte)Math.Clamp(percent / 100.0 * 255.0, 0, 255);
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            value = (byte)Math.Clamp(number, 0, 255);
            return true;
        }

        return false;
    }
}
