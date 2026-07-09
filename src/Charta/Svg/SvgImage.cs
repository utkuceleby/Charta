using System.Globalization;
using System.Xml.Linq;

namespace Charta.Svg;

/// <summary>Thrown when SVG content is malformed. Parsing arbitrary input yields this or success.</summary>
public sealed class SvgFormatException : Exception
{
    /// <summary>Initializes the exception without a message.</summary>
    public SvgFormatException()
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    public SvgFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an underlying cause.</summary>
    public SvgFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A parsed SVG document rendered through the vector canvas. Supports a practical subset: the
/// shape elements (path, rect, circle, ellipse, line, polyline, polygon), grouping with transforms,
/// and fill/stroke presentation. Coordinates share the canvas's top-left, y-down space, so nothing
/// needs flipping. Elliptical arcs in path data are approximated by line segments.
/// </summary>
internal sealed class SvgImage
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private readonly XElement _root;
    private readonly double _viewMinX;
    private readonly double _viewMinY;

    public double ViewWidth { get; }

    public double ViewHeight { get; }

    private SvgImage(XElement root, double minX, double minY, double width, double height)
    {
        _root = root;
        _viewMinX = minX;
        _viewMinY = minY;
        ViewWidth = width;
        ViewHeight = height;
    }

    /// <summary>Aspect ratio (width / height) from the viewBox or width/height attributes.</summary>
    public double Aspect => ViewHeight > 0 ? ViewWidth / ViewHeight : 1;

    public static SvgImage Parse(string svg)
    {
        ArgumentNullException.ThrowIfNull(svg);
        XDocument doc;
        try
        {
            doc = XDocument.Parse(svg);
        }
        catch (System.Xml.XmlException e)
        {
            throw new SvgFormatException("The SVG content is not well-formed XML.", e);
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "svg")
        {
            throw new SvgFormatException("The root element must be <svg>.");
        }

        double minX = 0, minY = 0, width, height;
        var viewBox = (string?)root.Attribute("viewBox");
        if (viewBox is not null)
        {
            var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 4 ||
                !TryLength(parts[0], out minX) || !TryLength(parts[1], out minY) ||
                !TryLength(parts[2], out width) || !TryLength(parts[3], out height))
            {
                throw new SvgFormatException("Invalid viewBox.");
            }
        }
        else
        {
            if (!TryLength((string?)root.Attribute("width"), out width) ||
                !TryLength((string?)root.Attribute("height"), out height))
            {
                throw new SvgFormatException("The <svg> element needs a viewBox or width and height.");
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new SvgFormatException("SVG dimensions must be positive.");
        }

        return new SvgImage(root, minX, minY, width, height);
    }

    /// <summary>Renders into a canvas sized in points, scaling the viewBox to fill it.</summary>
    public void Render(ICanvas canvas)
    {
        var scaleX = canvas.Width / ViewWidth;
        var scaleY = canvas.Height / ViewHeight;
        var rootMatrix = SvgMatrix.Scale(scaleX, scaleY).Multiply(SvgMatrix.Translate(-_viewMinX, -_viewMinY));
        var style = new SvgStyle(Fill: Color.Black, HasFill: true, Stroke: default, HasStroke: false, StrokeWidth: 1);
        RenderChildren(_root, canvas, rootMatrix, style);
    }

    private void RenderChildren(XElement element, ICanvas canvas, SvgMatrix matrix, SvgStyle style)
    {
        foreach (var child in element.Elements())
        {
            RenderElement(child, canvas, matrix, style);
        }
    }

    private void RenderElement(XElement element, ICanvas canvas, SvgMatrix parentMatrix, SvgStyle parentStyle)
    {
        var matrix = parentMatrix.Multiply(SvgTransform.Parse((string?)element.Attribute("transform")));
        var style = parentStyle.Inherit(element);

        switch (element.Name.LocalName)
        {
            case "g":
                RenderChildren(element, canvas, matrix, style);
                break;
            case "path":
                if ((string?)element.Attribute("d") is { } d)
                {
                    SvgPathData.Emit(canvas, d, matrix);
                    Paint(canvas, style, matrix);
                }

                break;
            case "rect":
                EmitRect(canvas, element, matrix);
                Paint(canvas, style, matrix);
                break;
            case "circle":
                EmitEllipse(canvas, element, matrix, radiusAttr: "r");
                Paint(canvas, style, matrix);
                break;
            case "ellipse":
                EmitEllipse(canvas, element, matrix, radiusAttr: null);
                Paint(canvas, style, matrix);
                break;
            case "line":
                EmitLine(canvas, element, matrix);
                Paint(canvas, style, matrix);
                break;
            case "polyline":
            case "polygon":
                EmitPoly(canvas, element, matrix, close: element.Name.LocalName == "polygon");
                Paint(canvas, style, matrix);
                break;
            default:
                RenderChildren(element, canvas, matrix, style); // unknown container: descend
                break;
        }
    }

    private static void Paint(ICanvas canvas, SvgStyle style, SvgMatrix matrix)
    {
        var width = style.StrokeWidth * matrix.MeanScale;
        if (style.HasFill && style.HasStroke)
        {
            canvas.FillAndStroke(style.Fill, style.Stroke, width);
        }
        else if (style.HasFill)
        {
            canvas.Fill(style.Fill);
        }
        else if (style.HasStroke)
        {
            canvas.Stroke(style.Stroke, width);
        }
        else
        {
            canvas.Fill(Color.Black); // SVG default is black fill
        }
    }

    private static void EmitRect(ICanvas canvas, XElement e, SvgMatrix m)
    {
        var x = Length(e, "x");
        var y = Length(e, "y");
        var w = Length(e, "width");
        var h = Length(e, "height");
        // Emit as a transformed quad so rotation/scale apply.
        var (x0, y0) = m.Apply(x, y);
        var (x1, y1) = m.Apply(x + w, y);
        var (x2, y2) = m.Apply(x + w, y + h);
        var (x3, y3) = m.Apply(x, y + h);
        canvas.MoveTo(x0, y0).LineTo(x1, y1).LineTo(x2, y2).LineTo(x3, y3).Close();
    }

    private static void EmitEllipse(ICanvas canvas, XElement e, SvgMatrix m, string? radiusAttr)
    {
        var cx = Length(e, "cx");
        var cy = Length(e, "cy");
        double rx, ry;
        if (radiusAttr is not null)
        {
            rx = ry = Length(e, radiusAttr);
        }
        else
        {
            rx = Length(e, "rx");
            ry = Length(e, "ry");
        }

        // Approximate with 4 transformed Bézier arcs (matches CanvasWriter.Ellipse, transformed).
        const double k = 0.5522847498307936;
        var ox = rx * k;
        var oy = ry * k;
        P(canvas.MoveTo, m, cx - rx, cy);
        C(canvas, m, cx - rx, cy - oy, cx - ox, cy - ry, cx, cy - ry);
        C(canvas, m, cx + ox, cy - ry, cx + rx, cy - oy, cx + rx, cy);
        C(canvas, m, cx + rx, cy + oy, cx + ox, cy + ry, cx, cy + ry);
        C(canvas, m, cx - ox, cy + ry, cx - rx, cy + oy, cx - rx, cy);
        canvas.Close();
    }

    private static void EmitLine(ICanvas canvas, XElement e, SvgMatrix m)
    {
        P(canvas.MoveTo, m, Length(e, "x1"), Length(e, "y1"));
        P(canvas.LineTo, m, Length(e, "x2"), Length(e, "y2"));
    }

    private static void EmitPoly(ICanvas canvas, XElement e, SvgMatrix m, bool close)
    {
        var points = ((string?)e.Attribute("points") ?? string.Empty)
            .Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var first = true;
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            if (!TryLength(points[i], out var x) || !TryLength(points[i + 1], out var y))
            {
                continue;
            }

            if (first)
            {
                P(canvas.MoveTo, m, x, y);
                first = false;
            }
            else
            {
                P(canvas.LineTo, m, x, y);
            }
        }

        if (close && !first)
        {
            canvas.Close();
        }
    }

    private static void C(ICanvas canvas, SvgMatrix m, double c1x, double c1y, double c2x, double c2y, double x, double y)
    {
        var (a1, b1) = m.Apply(c1x, c1y);
        var (a2, b2) = m.Apply(c2x, c2y);
        var (ex, ey) = m.Apply(x, y);
        canvas.CurveTo(a1, b1, a2, b2, ex, ey);
    }

    private static void P(Func<double, double, ICanvas> op, SvgMatrix m, double x, double y)
    {
        var (px, py) = m.Apply(x, y);
        op(px, py);
    }

    private static double Length(XElement e, string name) => TryLength((string?)e.Attribute(name), out var v) ? v : 0;

    internal static bool TryLength(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        // Strip a unit suffix (px, pt, …); the renderer scales the whole viewBox to the target box.
        var end = text.Length;
        while (end > 0 && !char.IsDigit(text[end - 1]) && text[end - 1] != '.')
        {
            end--;
        }

        return double.TryParse(text.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
