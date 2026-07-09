using Charta.Fluent;
using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta;

/// <summary>The element vocabulary. Style methods chain (each wraps the next); content methods fill the slot.</summary>
public static class ContainerExtensions
{
    private static ContainerImpl Impl(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return (ContainerImpl)container;
    }

    /// <summary>Insets the content on all four sides, in points.</summary>
    public static IContainer Padding(this IContainer container, double all) =>
        container.Padding(all, all, all, all);

    /// <summary>Insets the content per side, in points.</summary>
    public static IContainer Padding(this IContainer container, double left, double top, double right, double bottom)
    {
        Impl(container).AddWrapper((_, e) => new PaddingElement(e, left, top, right, bottom));
        return container;
    }

    /// <summary>Insets the content left and right, in points.</summary>
    public static IContainer PaddingHorizontal(this IContainer container, double value) =>
        container.Padding(value, 0, value, 0);

    /// <summary>Insets the content top and bottom, in points.</summary>
    public static IContainer PaddingVertical(this IContainer container, double value) =>
        container.Padding(0, value, 0, value);

    /// <summary>Fills the container's bounds with a color behind the content.</summary>
    public static IContainer Background(this IContainer container, Color color)
    {
        Impl(container).AddWrapper((_, e) => new BackgroundElement(e, color.ToLayout()));
        return container;
    }

    /// <summary>Strokes a border around the container's bounds.</summary>
    public static IContainer Border(this IContainer container, double thickness) =>
        container.Border(thickness, Color.Black);

    /// <summary>Strokes a colored border around the container's bounds.</summary>
    public static IContainer Border(this IContainer container, double thickness, Color color)
    {
        Impl(container).AddWrapper((_, e) => new BorderElement(e, thickness, color.ToLayout()));
        return container;
    }

    /// <summary>Left-aligns the content within the available width.</summary>
    public static IContainer AlignLeft(this IContainer container) => Align(container, HorizontalAlignment.Left);

    /// <summary>Centers the content within the available width.</summary>
    public static IContainer AlignCenter(this IContainer container) => Align(container, HorizontalAlignment.Center);

    /// <summary>Right-aligns the content within the available width.</summary>
    public static IContainer AlignRight(this IContainer container) => Align(container, HorizontalAlignment.Right);

    private static IContainer Align(IContainer container, HorizontalAlignment alignment)
    {
        Impl(container).AddWrapper((_, e) => new AlignElement(e, alignment));
        return container;
    }

    /// <summary>Forces an exact width in points.</summary>
    public static IContainer Width(this IContainer container, double value) =>
        Constrain(container, minWidth: value, maxWidth: value);

    /// <summary>Caps the width in points.</summary>
    public static IContainer MaxWidth(this IContainer container, double value) =>
        Constrain(container, maxWidth: value);

    /// <summary>Requires at least this width in points.</summary>
    public static IContainer MinWidth(this IContainer container, double value) =>
        Constrain(container, minWidth: value);

    /// <summary>Forces an exact height in points.</summary>
    public static IContainer Height(this IContainer container, double value) =>
        Constrain(container, minHeight: value, maxHeight: value);

    /// <summary>Caps the height in points.</summary>
    public static IContainer MaxHeight(this IContainer container, double value) =>
        Constrain(container, maxHeight: value);

    /// <summary>Requires at least this height in points.</summary>
    public static IContainer MinHeight(this IContainer container, double value) =>
        Constrain(container, minHeight: value);

    private static IContainer Constrain(
        IContainer container,
        double? minWidth = null,
        double? maxWidth = null,
        double? minHeight = null,
        double? maxHeight = null)
    {
        Impl(container).AddWrapper((_, e) => new ConstrainedElement(e, minWidth, maxWidth, minHeight, maxHeight));
        return container;
    }

    /// <summary>Fills the slot with a vertical stack that paginates across pages.</summary>
    public static void Column(this IContainer container, Action<IColumnDescriptor> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var descriptor = new ColumnDescriptor();
        content(descriptor);
        Impl(container).SetSource(descriptor.Build);
    }

    /// <summary>Fills the slot with a horizontal arrangement (rows do not paginate).</summary>
    public static void Row(this IContainer container, Action<IRowDescriptor> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var descriptor = new RowDescriptor();
        content(descriptor);
        Impl(container).SetSource(descriptor.Build);
    }

    /// <summary>Fills the slot with a text block. Style it via the returned descriptor.</summary>
    public static ITextDescriptor Text(this IContainer container, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var descriptor = new TextDescriptor(text);
        Impl(container).SetSource(descriptor.Build);
        return descriptor;
    }

    /// <summary>Fills the slot with a PNG or JPEG image scaled to the available width.</summary>
    public static void Image(this IContainer container, byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        Impl(container).SetSource(ctx => new ImageElement(ctx.GetImage(imageData, imageData)));
    }

    /// <summary>Fills the slot with an image loaded from a file.</summary>
    public static void Image(this IContainer container, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var data = File.ReadAllBytes(filePath);
        Impl(container).SetSource(ctx => new ImageElement(ctx.GetImage(data, data)));
    }

    /// <summary>Fills the slot with a horizontal rule.</summary>
    public static void LineHorizontal(this IContainer container, double thickness) =>
        container.LineHorizontal(thickness, Color.Black);

    /// <summary>Fills the slot with a colored horizontal rule.</summary>
    public static void LineHorizontal(this IContainer container, double thickness, Color color) =>
        Impl(container).SetSource(_ => new LineElement(thickness, color.ToLayout()));

    /// <summary>Fills the slot with a forced page break.</summary>
    public static void PageBreak(this IContainer container) =>
        Impl(container).SetSource(_ => new PageBreakElement());

    /// <summary>Composes a reusable fragment into this container.</summary>
    public static void Component(this IContainer container, IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        component.Compose(container);
    }
}
