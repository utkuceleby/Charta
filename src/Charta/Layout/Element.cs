namespace Charta.Layout;

/// <summary>
/// The layout protocol: two passes over a stateful element tree. <see cref="Measure"/> evaluates how
/// much of the element's remaining content (from its continuation cursor) fits the constraints;
/// <see cref="Draw"/> renders exactly what the immediately preceding measurement promised and advances
/// the cursor. The page loop repeats measure/draw with fresh pages until the root reports
/// <see cref="LayoutVerdict.Complete"/>. Trees are single-use: one generation per tree.
/// </summary>
internal abstract class Element
{
    public abstract MeasureResult Measure(in LayoutConstraints constraints);

    public abstract void Draw(DrawingContext context, in LayoutRect bounds);
}

/// <summary>Base for decorators wrapping exactly one child.</summary>
internal abstract class ContainerElement(Element child) : Element
{
    protected Element Child { get; } = child;
}
