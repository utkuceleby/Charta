using Charta.Imaging;
using Charta.Svg;

namespace Charta.Layout.Elements;

/// <summary>
/// A raster image scaled to the available width (its natural pixel size when width is unbounded),
/// preserving aspect ratio. Reports Overflowing when the resulting height does not fit — the parent
/// decides between a fresh page and clipping.
/// </summary>
internal sealed class ImageElement(PdfImage image, string? altText = null) : Element
{
    private Charta.Compliance.StructElement? _struct;

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var width = double.IsInfinity(constraints.AvailableWidth) ? image.Width : constraints.AvailableWidth;
        var height = width * image.Height / image.Width;
        return height <= constraints.AvailableHeight
            ? MeasureResult.Complete(width, height)
            : MeasureResult.Overflowing(width, height);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        if (_struct is null && context.Structure is not null)
        {
            _struct = context.AddStructElement("Figure");
            if (_struct is not null)
            {
                _struct.Alt = altText ?? "Image";
            }
        }

        var captured = bounds;
        context.Tagged("Figure", _struct, () => context.DrawImage(image, captured));
    }
}

/// <summary>
/// A fixed-size vector drawing surface. The draw callback runs once against a canvas sized to the
/// element; the recorded operators are clipped to and placed within the element's bounds.
/// </summary>
internal sealed class CanvasElement(double width, double height, Action<ICanvas> draw, string? altText = null) : Element
{
    private Charta.Compliance.StructElement? _struct;

    public override MeasureResult Measure(in LayoutConstraints constraints) =>
        width <= constraints.AvailableWidth + 0.01 && height <= constraints.AvailableHeight + 0.01
            ? MeasureResult.Complete(width, height)
            : MeasureResult.Overflowing(width, height);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        if (_struct is null && context.Structure is not null)
        {
            _struct = context.AddStructElement("Figure");
            if (_struct is not null)
            {
                _struct.Alt = altText ?? "Drawing";
            }
        }

        var canvas = new CanvasWriter(width, height);
        draw(canvas);
        // A fixed-size canvas draws at its own size, top-left aligned, not stretched to the bounds.
        var target = new LayoutRect(bounds.X, bounds.Y, width, height);
        context.Tagged("Figure", _struct, () => context.DrawCanvasContent(target, canvas.Content));
    }
}

/// <summary>
/// A scalable vector image parsed from SVG. Scales to the available width preserving the viewBox
/// aspect ratio (like a raster image), then renders through the vector canvas.
/// </summary>
internal sealed class SvgElement(SvgImage image, string? altText = null) : Element
{
    private Charta.Compliance.StructElement? _struct;

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var width = double.IsInfinity(constraints.AvailableWidth) ? image.ViewWidth : constraints.AvailableWidth;
        var height = width / image.Aspect;
        return height <= constraints.AvailableHeight
            ? MeasureResult.Complete(width, height)
            : MeasureResult.Overflowing(width, height);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        if (_struct is null && context.Structure is not null)
        {
            _struct = context.AddStructElement("Figure");
            if (_struct is not null)
            {
                _struct.Alt = altText ?? "Vector image";
            }
        }

        var canvas = new CanvasWriter(bounds.Width, bounds.Height);
        image.Render(canvas);
        var captured = bounds;
        context.Tagged("Figure", _struct, () => context.DrawCanvasContent(captured, canvas.Content));
    }
}

/// <summary>A horizontal rule filling the available width.</summary>
internal sealed class LineElement(double thickness, LayoutColor color) : Element
{
    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        if (thickness > constraints.AvailableHeight)
        {
            return MeasureResult.Empty;
        }

        var width = double.IsInfinity(constraints.AvailableWidth) ? 0 : constraints.AvailableWidth;
        return MeasureResult.Complete(width, thickness);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds) =>
        context.FillRect(bounds, color);
}

/// <summary>Forces a page break: reports Partial once, then Complete after it has been drawn.</summary>
internal sealed class PageBreakElement : Element
{
    private bool _consumed;

    public override MeasureResult Measure(in LayoutConstraints constraints) =>
        _consumed ? MeasureResult.Complete(0, 0) : MeasureResult.Partial(0, 0);

    public override void Draw(DrawingContext context, in LayoutRect bounds) => _consumed = true;
}

/// <summary>A zero-size placeholder for container slots that were never filled.</summary>
internal sealed class EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();

    private EmptyElement()
    {
    }

    public override MeasureResult Measure(in LayoutConstraints constraints) => MeasureResult.Complete(0, 0);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
    }
}
