using System.Globalization;
using System.Text;
using Charta.Cos;

namespace Charta.Layout.Elements;

/// <summary>
/// Records vector drawing as PDF path operators in a local top-left coordinate space. The recorded
/// operators are replayed by <see cref="DrawingContext"/> inside a Y-flipping transform, so drawing
/// code never converts coordinates itself.
/// </summary>
internal sealed class CanvasWriter(double width, double height) : ICanvas
{
    private const double Kappa = 0.5522847498307936; // circle-to-Bézier control ratio

    private readonly StringBuilder _ops = new();

    public double Width { get; } = width;

    public double Height { get; } = height;

    public string Content => _ops.ToString();

    public ICanvas MoveTo(double x, double y)
    {
        _ops.Append(F(x)).Append(' ').Append(F(y)).Append(" m\n");
        return this;
    }

    public ICanvas LineTo(double x, double y)
    {
        _ops.Append(F(x)).Append(' ').Append(F(y)).Append(" l\n");
        return this;
    }

    public ICanvas CurveTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
    {
        _ops.Append(F(c1x)).Append(' ').Append(F(c1y)).Append(' ')
            .Append(F(c2x)).Append(' ').Append(F(c2y)).Append(' ')
            .Append(F(x)).Append(' ').Append(F(y)).Append(" c\n");
        return this;
    }

    public ICanvas Rectangle(double x, double y, double w, double h)
    {
        _ops.Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re\n");
        return this;
    }

    public ICanvas Circle(double centerX, double centerY, double radius) =>
        Ellipse(centerX, centerY, radius, radius);

    public ICanvas Ellipse(double cx, double cy, double rx, double ry)
    {
        var ox = rx * Kappa;
        var oy = ry * Kappa;
        MoveTo(cx - rx, cy);
        CurveTo(cx - rx, cy - oy, cx - ox, cy - ry, cx, cy - ry);
        CurveTo(cx + ox, cy - ry, cx + rx, cy - oy, cx + rx, cy);
        CurveTo(cx + rx, cy + oy, cx + ox, cy + ry, cx, cy + ry);
        CurveTo(cx - ox, cy + ry, cx - rx, cy + oy, cx - rx, cy);
        return Close();
    }

    public ICanvas Close()
    {
        _ops.Append("h\n");
        return this;
    }

    public void Fill(Color color)
    {
        AppendFillColor(color);
        _ops.Append("f\n");
    }

    public void Stroke(Color color, double lineWidth = 1)
    {
        AppendStrokeColor(color);
        _ops.Append(F(lineWidth)).Append(" w\n");
        _ops.Append("S\n");
    }

    public void FillAndStroke(Color fillColor, Color strokeColor, double lineWidth = 1)
    {
        AppendFillColor(fillColor);
        AppendStrokeColor(strokeColor);
        _ops.Append(F(lineWidth)).Append(" w\n");
        _ops.Append("B\n");
    }

    private void AppendFillColor(Color color) =>
        _ops.Append(F(color.R / 255.0)).Append(' ').Append(F(color.G / 255.0)).Append(' ').Append(F(color.B / 255.0)).Append(" rg\n");

    private void AppendStrokeColor(Color color) =>
        _ops.Append(F(color.R / 255.0)).Append(' ').Append(F(color.G / 255.0)).Append(' ').Append(F(color.B / 255.0)).Append(" RG\n");

    private static string F(double value) => CosReal.Format(value);
}
