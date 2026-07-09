namespace Charta.Layout.Elements;

/// <summary>Makes its child a clickable link to an external URI.</summary>
internal sealed class HyperlinkElement(Element child, string uri) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints) => Child.Measure(constraints);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        context.AddAnnotation(new PageAnnotation { Rect = bounds, Uri = uri });
        Child.Draw(context, bounds);
    }
}

/// <summary>Marks its child's position as a named destination for internal links and bookmarks.</summary>
internal sealed class SectionElement(Element child, string name, string? bookmarkTitle) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints) => Child.Measure(constraints);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        context.RegisterDestination(name, bounds.Y, bookmarkTitle);
        Child.Draw(context, bounds);
    }
}

/// <summary>Makes its child a clickable link to a named destination in the same document.</summary>
internal sealed class SectionLinkElement(Element child, string sectionName) : ContainerElement(child)
{
    public override MeasureResult Measure(in LayoutConstraints constraints) => Child.Measure(constraints);

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        context.AddAnnotation(new PageAnnotation { Rect = bounds, DestinationName = sectionName });
        Child.Draw(context, bounds);
    }
}
