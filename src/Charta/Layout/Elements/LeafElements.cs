using Charta.Imaging;

namespace Charta.Layout.Elements;

/// <summary>
/// A raster image scaled to the available width (its natural pixel size when width is unbounded),
/// preserving aspect ratio. Reports Overflowing when the resulting height does not fit — the parent
/// decides between a fresh page and clipping.
/// </summary>
internal sealed class ImageElement(PdfImage image) : Element
{
    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var width = double.IsInfinity(constraints.AvailableWidth) ? image.Width : constraints.AvailableWidth;
        var height = width * image.Height / image.Width;
        return height <= constraints.AvailableHeight
            ? MeasureResult.Complete(width, height)
            : MeasureResult.Overflowing(width, height);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds) =>
        context.DrawImage(image, bounds);
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
