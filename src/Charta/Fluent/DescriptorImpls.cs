using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta.Fluent;

internal sealed class DocumentDescriptor : IDocumentDescriptor
{
    public List<PageDescriptor> Pages { get; } = [];

    public void Page(Action<IPageDescriptor> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var page = new PageDescriptor();
        configure(page);
        Pages.Add(page);
    }
}

internal sealed class PageDescriptor : IPageDescriptor
{
    private PageSize _size = PageSizes.A4;
    private double _margin = 42.5;
    private ContainerImpl? _header;
    private ContainerImpl? _content;
    private ContainerImpl? _footer;

    public void Size(PageSize size) => _size = size;

    public void Margin(double points) => _margin = points;

    public void Margin(double value, Unit unit) => _margin = unit.ToPoints(value);

    public IContainer Header() => _header ??= new ContainerImpl();

    public IContainer Content() => _content ??= new ContainerImpl();

    public IContainer Footer() => _footer ??= new ContainerImpl();

    public PageSection Build(BuildContext context)
    {
        var header = _header;
        var footer = _footer;
        return new PageSection
        {
            PageSize = new LayoutSize(_size.Width, _size.Height),
            Margin = _margin,
            Content = _content?.Build(context) ?? EmptyElement.Instance,
            Header = header is null ? null : () => header.Build(context),
            Footer = footer is null ? null : () => footer.Build(context),
        };
    }
}

internal sealed class ColumnDescriptor : IColumnDescriptor
{
    private readonly List<ContainerImpl> _items = [];
    private double _spacing;

    public void Spacing(double value) => _spacing = value;

    public IContainer Item()
    {
        var item = new ContainerImpl();
        _items.Add(item);
        return item;
    }

    public Element Build(BuildContext context) =>
        new ColumnElement(_items.Select(item => item.Build(context)).ToList(), _spacing);
}

internal sealed class RowDescriptor : IRowDescriptor
{
    private readonly List<(ContainerImpl Container, double? FixedWidth, double Weight)> _items = [];
    private double _spacing;

    public void Spacing(double value) => _spacing = value;

    public IContainer RelativeItem(double weight = 1)
    {
        var item = new ContainerImpl();
        _items.Add((item, null, weight));
        return item;
    }

    public IContainer ConstantItem(double width)
    {
        var item = new ContainerImpl();
        _items.Add((item, width, 0));
        return item;
    }

    public Element Build(BuildContext context) =>
        new RowElement(
            _items.Select(item => new RowItem
            {
                Element = item.Container.Build(context),
                FixedWidth = item.FixedWidth,
                Weight = item.Weight,
            }).ToList(),
            _spacing);
}

internal sealed class TextDescriptor(string text) : ITextDescriptor
{
    private string? _family;
    private double _size = 12;
    private Color _color = Color.Black;
    private double _lineSpacing = 1.0;
    private bool _bold;
    private bool _italic;

    public ITextDescriptor FontFamily(string family)
    {
        _family = family;
        return this;
    }

    public ITextDescriptor FontSize(double size)
    {
        _size = size;
        return this;
    }

    public ITextDescriptor FontColor(Color color)
    {
        _color = color;
        return this;
    }

    public ITextDescriptor LineSpacing(double multiplier)
    {
        _lineSpacing = multiplier;
        return this;
    }

    public ITextDescriptor Bold()
    {
        _bold = true;
        return this;
    }

    public ITextDescriptor Italic()
    {
        _italic = true;
        return this;
    }

    public Element Build(BuildContext context) =>
        new TextElement(text, new TextStyle
        {
            Fonts = context.ResolveFonts(_family, _bold, _italic),
            FontSize = _size,
            Color = _color.ToLayout(),
            LineSpacing = _lineSpacing,
        });
}
