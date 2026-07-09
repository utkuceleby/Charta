namespace Charta.Layout.Elements;

/// <summary>
/// Takes all of the offered height (and width) regardless of the child's natural size — stretches
/// content to fill the rest of the page (a bordered area, a background panel). Use as the last item
/// of a column: anything after it is pushed to the next page. Under unbounded constraints (inside a
/// Row) it falls back to the child's natural size, and a paginating child passes through unchanged.
/// </summary>
internal sealed class ExtendElement(Element child) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var child = Child.Measure(constraints);
        if (child.Verdict != LayoutVerdict.Complete)
        {
            return child;
        }

        var width = double.IsInfinity(constraints.AvailableWidth) ? child.Size.Width : constraints.AvailableWidth;
        var height = double.IsInfinity(constraints.AvailableHeight) ? child.Size.Height : constraints.AvailableHeight;
        return MeasureResult.Complete(width, height);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds) => Child.Draw(context, bounds);
}

/// <summary>
/// Draws several children in the same bounds, in order — backgrounds and watermarks below, stamps
/// above. The primary layer defines the size and pagination; overlay layers are drawn with the
/// primary's bounds on every page and are expected to be non-paginating decoration.
/// </summary>
internal sealed class LayersElement(Element primary, IReadOnlyList<Element> below, IReadOnlyList<Element> above) : Element
{
    public override MeasureResult Measure(in LayoutConstraints constraints) => primary.Measure(constraints);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        foreach (var layer in below)
        {
            layer.Draw(context, bounds);
        }

        primary.Draw(context, bounds);
        foreach (var layer in above)
        {
            layer.Draw(context, bounds);
        }
    }
}
