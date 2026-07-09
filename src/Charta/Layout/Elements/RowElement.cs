namespace Charta.Layout.Elements;

/// <summary>A slot in a row: either a fixed width in points or a relative weight over the leftover space.</summary>
internal sealed class RowItem
{
    public required Element Element { get; init; }

    public double? FixedWidth { get; init; }

    public double Weight { get; init; } = 1;
}

/// <summary>
/// Places children side by side. Rows do not paginate in v1: children are measured with unbounded
/// height to find the row's natural height, and a row that does not fit reports Overflowing so its
/// parent applies the retry-then-clip rule. (Splittable rows are a Table concern — M4.)
/// </summary>
internal sealed class RowElement(IReadOnlyList<RowItem> items, double spacing = 0) : Element
{
    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var widths = ResolveWidths(constraints.AvailableWidth);
        var height = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var child = items[i].Element.Measure(new LayoutConstraints(widths[i], double.PositiveInfinity));
            height = Math.Max(height, child.Size.Height);
        }

        var totalWidth = widths.Sum() + spacing * (items.Count - 1);
        return height <= constraints.AvailableHeight
            ? MeasureResult.Complete(totalWidth, height)
            : MeasureResult.Overflowing(totalWidth, height);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        var widths = ResolveWidths(bounds.Width);
        var x = bounds.X;
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Element.Draw(context, new LayoutRect(x, bounds.Y, widths[i], bounds.Height));
            x += widths[i] + spacing;
        }
    }

    private double[] ResolveWidths(double availableWidth)
    {
        var widths = new double[items.Count];
        var remaining = availableWidth - spacing * (items.Count - 1);
        var totalWeight = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].FixedWidth is { } fixedWidth)
            {
                widths[i] = fixedWidth;
                remaining -= fixedWidth;
            }
            else
            {
                totalWeight += items[i].Weight;
            }
        }

        remaining = Math.Max(0, remaining);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].FixedWidth is null)
            {
                widths[i] = totalWeight > 0 ? remaining * items[i].Weight / totalWeight : 0;
            }
        }

        return widths;
    }
}
