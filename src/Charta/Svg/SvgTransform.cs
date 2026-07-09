using System.Globalization;

namespace Charta.Svg;

/// <summary>Parses the SVG <c>transform</c> attribute (translate, scale, rotate, matrix), composed left to right.</summary>
internal static class SvgTransform
{
    public static SvgMatrix Parse(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform))
        {
            return SvgMatrix.Identity;
        }

        var result = SvgMatrix.Identity;
        var index = 0;
        while (index < transform.Length)
        {
            var open = transform.IndexOf('(', index);
            if (open < 0)
            {
                break;
            }

            var name = transform[index..open].Trim().TrimStart(',').Trim();
            var close = transform.IndexOf(')', open);
            if (close < 0)
            {
                break;
            }

            var args = transform[(open + 1)..close]
                .Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(a => double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .ToArray();

            var matrix = name switch
            {
                "translate" => SvgMatrix.Translate(Arg(args, 0), Arg(args, 1)),
                "scale" => SvgMatrix.Scale(Arg(args, 0, 1), args.Length > 1 ? args[1] : Arg(args, 0, 1)),
                "rotate" => Rotate(args),
                "matrix" when args.Length >= 6 => new SvgMatrix(args[0], args[1], args[2], args[3], args[4], args[5]),
                _ => SvgMatrix.Identity,
            };

            result = result.Multiply(matrix);
            index = close + 1;
        }

        return result;
    }

    private static SvgMatrix Rotate(double[] args)
    {
        var angle = Arg(args, 0);
        if (args.Length >= 3)
        {
            // rotate(a, cx, cy): translate to center, rotate, translate back.
            return SvgMatrix.Translate(args[1], args[2])
                .Multiply(SvgMatrix.Rotate(angle))
                .Multiply(SvgMatrix.Translate(-args[1], -args[2]));
        }

        return SvgMatrix.Rotate(angle);
    }

    private static double Arg(double[] args, int index, double fallback = 0) => index < args.Length ? args[index] : fallback;
}
