using System.Globalization;

namespace Charta.Svg;

/// <summary>
/// Parses SVG path data and emits it to the canvas through a transform. Supports M/L/H/V/C/S/Q/T/Z
/// (absolute and relative). Elliptical arcs (A) are approximated by a straight line to the endpoint.
/// </summary>
internal static class SvgPathData
{
    public static void Emit(ICanvas canvas, string data, SvgMatrix matrix)
    {
        var tokens = new PathTokenizer(data);
        double curX = 0, curY = 0, startX = 0, startY = 0;
        double lastC1X = 0, lastC1Y = 0; // reflected control point for S/T
        var command = '\0';
        var prevCommand = '\0';

        void MoveTo(double x, double y)
        {
            var (px, py) = matrix.Apply(x, y);
            canvas.MoveTo(px, py);
        }

        void LineTo(double x, double y)
        {
            var (px, py) = matrix.Apply(x, y);
            canvas.LineTo(px, py);
        }

        void CurveTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
        {
            var (a1, b1) = matrix.Apply(c1x, c1y);
            var (a2, b2) = matrix.Apply(c2x, c2y);
            var (ex, ey) = matrix.Apply(x, y);
            canvas.CurveTo(a1, b1, a2, b2, ex, ey);
            lastC1X = c2x;
            lastC1Y = c2y;
        }

        while (tokens.TryReadCommand(out var next))
        {
            command = next;
            var relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    curX = Coord(tokens, relative, curX);
                    curY = Coord(tokens, relative, curY);
                    MoveTo(curX, curY);
                    startX = curX;
                    startY = curY;
                    // Subsequent implicit pairs are treated as line-to (SVG spec).
                    while (tokens.PeekIsNumber())
                    {
                        curX = Coord(tokens, relative, curX);
                        curY = Coord(tokens, relative, curY);
                        LineTo(curX, curY);
                    }

                    break;

                case 'L':
                    do
                    {
                        curX = Coord(tokens, relative, curX);
                        curY = Coord(tokens, relative, curY);
                        LineTo(curX, curY);
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'H':
                    do
                    {
                        curX = Coord(tokens, relative, curX);
                        LineTo(curX, curY);
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'V':
                    do
                    {
                        curY = Coord(tokens, relative, curY);
                        LineTo(curX, curY);
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'C':
                    do
                    {
                        var c1x = Coord(tokens, relative, curX);
                        var c1y = Coord(tokens, relative, curY);
                        var c2x = Coord(tokens, relative, curX);
                        var c2y = Coord(tokens, relative, curY);
                        var x = Coord(tokens, relative, curX);
                        var y = Coord(tokens, relative, curY);
                        CurveTo(c1x, c1y, c2x, c2y, x, y);
                        curX = x;
                        curY = y;
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'S':
                    do
                    {
                        var (rx, ry) = Reflect(prevCommand, command, curX, curY, lastC1X, lastC1Y);
                        var c2x = Coord(tokens, relative, curX);
                        var c2y = Coord(tokens, relative, curY);
                        var x = Coord(tokens, relative, curX);
                        var y = Coord(tokens, relative, curY);
                        CurveTo(rx, ry, c2x, c2y, x, y);
                        curX = x;
                        curY = y;
                        prevCommand = command;
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'Q':
                    do
                    {
                        var qx = Coord(tokens, relative, curX);
                        var qy = Coord(tokens, relative, curY);
                        var x = Coord(tokens, relative, curX);
                        var y = Coord(tokens, relative, curY);
                        EmitQuadratic(CurveTo, curX, curY, qx, qy, x, y);
                        lastC1X = qx;
                        lastC1Y = qy;
                        curX = x;
                        curY = y;
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'T':
                    do
                    {
                        var (qx, qy) = Reflect(prevCommand, command, curX, curY, lastC1X, lastC1Y);
                        var x = Coord(tokens, relative, curX);
                        var y = Coord(tokens, relative, curY);
                        EmitQuadratic(CurveTo, curX, curY, qx, qy, x, y);
                        lastC1X = qx;
                        lastC1Y = qy;
                        curX = x;
                        curY = y;
                        prevCommand = command;
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'A':
                    // Arc approximated by a line to the endpoint (rx ry rot large sweep x y).
                    do
                    {
                        for (var i = 0; i < 5; i++)
                        {
                            tokens.TryReadNumber(out _);
                        }

                        curX = Coord(tokens, relative, curX);
                        curY = Coord(tokens, relative, curY);
                        LineTo(curX, curY);
                    }
                    while (tokens.PeekIsNumber());
                    break;

                case 'Z':
                    canvas.Close();
                    curX = startX;
                    curY = startY;
                    break;

                default:
                    break;
            }

            prevCommand = command;
        }
    }

    private static (double X, double Y) Reflect(char prev, char current, double curX, double curY, double lastCx, double lastCy)
    {
        var p = char.ToUpperInvariant(prev);
        var c = char.ToUpperInvariant(current);
        var smoothChain = (c == 'S' && p is 'C' or 'S') || (c == 'T' && p is 'Q' or 'T');
        return smoothChain ? (2 * curX - lastCx, 2 * curY - lastCy) : (curX, curY);
    }

    private static void EmitQuadratic(Action<double, double, double, double, double, double> curve, double x0, double y0, double qx, double qy, double x1, double y1)
    {
        // Convert quadratic (start, control, end) to cubic control points.
        var c1x = x0 + 2.0 / 3.0 * (qx - x0);
        var c1y = y0 + 2.0 / 3.0 * (qy - y0);
        var c2x = x1 + 2.0 / 3.0 * (qx - x1);
        var c2y = y1 + 2.0 / 3.0 * (qy - y1);
        curve(c1x, c1y, c2x, c2y, x1, y1);
    }

    private static double Coord(PathTokenizer tokens, bool relative, double reference)
    {
        tokens.TryReadNumber(out var value);
        return relative ? reference + value : value;
    }

    private sealed class PathTokenizer(string data)
    {
        private int _index;

        public bool TryReadCommand(out char command)
        {
            SkipSeparators();
            if (_index < data.Length && char.IsLetter(data[_index]))
            {
                command = data[_index++];
                return true;
            }

            command = '\0';
            return false;
        }

        public bool PeekIsNumber()
        {
            SkipSeparators();
            return _index < data.Length && (char.IsDigit(data[_index]) || data[_index] is '-' or '+' or '.');
        }

        public bool TryReadNumber(out double value)
        {
            SkipSeparators();
            var start = _index;
            if (_index < data.Length && data[_index] is '-' or '+')
            {
                _index++;
            }

            while (_index < data.Length && (char.IsDigit(data[_index]) || data[_index] == '.'))
            {
                _index++;
            }

            if (_index < data.Length && data[_index] is 'e' or 'E')
            {
                _index++;
                if (_index < data.Length && data[_index] is '-' or '+')
                {
                    _index++;
                }

                while (_index < data.Length && char.IsDigit(data[_index]))
                {
                    _index++;
                }
            }

            return double.TryParse(data.AsSpan(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void SkipSeparators()
        {
            while (_index < data.Length && (data[_index] is ' ' or ',' or '\t' or '\n' or '\r'))
            {
                _index++;
            }
        }
    }
}
