namespace Charta.Layout.Elements;

/// <summary>Insets its child on all four sides. Top and bottom padding repeat on every page of a split child.</summary>
internal sealed class PaddingElement(Element child, double left, double top, double right, double bottom)
    : ContainerElement(child)
{
    public PaddingElement(Element child, double all)
        : this(child, all, all, all, all)
    {
    }

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var innerWidth = constraints.AvailableWidth - left - right;
        var innerHeight = constraints.AvailableHeight - top - bottom;
        if (innerWidth <= 0 || innerHeight <= 0)
        {
            return MeasureResult.Overflowing(left + right, top + bottom);
        }

        var child = Child.Measure(new LayoutConstraints(innerWidth, innerHeight));
        return new MeasureResult(
            new LayoutSize(child.Size.Width + left + right, child.Size.Height + top + bottom),
            child.Verdict);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        Child.Draw(context, new LayoutRect(
            bounds.X + left,
            bounds.Y + top,
            bounds.Width - left - right,
            bounds.Height - top - bottom));
    }
}

/// <summary>Paints a solid background behind its child, filling the allotted bounds.</summary>
internal sealed class BackgroundElement(Element child, LayoutColor color) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints) => Child.Measure(constraints);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        context.FillRect(bounds, color);
        Child.Draw(context, bounds);
    }
}

/// <summary>Strokes a border around the allotted bounds after drawing the child.</summary>
internal sealed class BorderElement(Element child, double thickness, LayoutColor color) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints) => Child.Measure(constraints);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        Child.Draw(context, bounds);
        context.StrokeRect(bounds, color, thickness);
    }
}

internal enum HorizontalAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>Takes the full available width and positions its child inside it.</summary>
internal sealed class AlignElement(Element child, HorizontalAlignment alignment) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var child = Child.Measure(constraints);
        var width = double.IsInfinity(constraints.AvailableWidth) ? child.Size.Width : constraints.AvailableWidth;
        return new MeasureResult(new LayoutSize(width, child.Size.Height), child.Verdict);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        var child = Child.Measure(new LayoutConstraints(bounds.Width, bounds.Height));
        var offset = alignment switch
        {
            HorizontalAlignment.Center => (bounds.Width - child.Size.Width) / 2,
            HorizontalAlignment.Right => bounds.Width - child.Size.Width,
            _ => 0,
        };
        Child.Draw(context, new LayoutRect(bounds.X + Math.Max(0, offset), bounds.Y, child.Size.Width, bounds.Height));
    }
}

/// <summary>Clamps the constraints offered to its child.</summary>
internal sealed class ConstrainedElement(
    Element child,
    double? minWidth = null,
    double? maxWidth = null,
    double? minHeight = null,
    double? maxHeight = null) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        if (minWidth > constraints.AvailableWidth || minHeight > constraints.AvailableHeight)
        {
            return MeasureResult.Overflowing(minWidth ?? 0, minHeight ?? 0);
        }

        var inner = new LayoutConstraints(
            Math.Min(constraints.AvailableWidth, maxWidth ?? double.PositiveInfinity),
            Math.Min(constraints.AvailableHeight, maxHeight ?? double.PositiveInfinity));
        var child = Child.Measure(inner);
        return new MeasureResult(
            new LayoutSize(
                Math.Max(child.Size.Width, minWidth ?? 0),
                Math.Max(child.Size.Height, minHeight ?? 0)),
            child.Verdict);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds) => Child.Draw(context, bounds);
}
