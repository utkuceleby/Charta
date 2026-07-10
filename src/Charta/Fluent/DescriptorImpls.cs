using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta.Fluent;

internal sealed class DocumentDescriptor : IDocumentDescriptor
{
    public List<PageDescriptor> Pages { get; } = [];

    public Charta.Metadata.DocumentMetadata DocumentMetadata { get; } = new();

    /// <summary>Set when the description contains a TotalPages() span — triggers the counting pre-pass.</summary>
    public bool UsesTotalPages { get; set; }

    public void Page(Action<IPageDescriptor> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var page = new PageDescriptor();
        configure(page);
        Pages.Add(page);
    }

    public void Metadata(Action<IMetadataDescriptor> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new MetadataDescriptor(DocumentMetadata));
    }
}

internal sealed class MetadataDescriptor(Charta.Metadata.DocumentMetadata target) : IMetadataDescriptor
{
    public IMetadataDescriptor Title(string title)
    {
        target.Title = title;
        return this;
    }

    public IMetadataDescriptor Author(string author)
    {
        target.Author = author;
        return this;
    }

    public IMetadataDescriptor Subject(string subject)
    {
        target.Subject = subject;
        return this;
    }

    public IMetadataDescriptor Keywords(string keywords)
    {
        target.Keywords = keywords;
        return this;
    }

    public IMetadataDescriptor Creator(string creator)
    {
        target.Creator = creator;
        return this;
    }

    public IMetadataDescriptor CreationDate(DateTimeOffset timestamp)
    {
        target.CreationDate = timestamp;
        return this;
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
            Header = header is null ? null : page =>
            {
                context.CurrentPage = page;
                return header.Build(context);
            },
            Footer = footer is null ? null : page =>
            {
                context.CurrentPage = page;
                return footer.Build(context);
            },
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

internal sealed class LayersDescriptor : ILayersDescriptor
{
    private readonly List<(ContainerImpl Container, bool IsPrimary)> _layers = [];

    public IContainer Layer()
    {
        var layer = new ContainerImpl();
        _layers.Add((layer, false));
        return layer;
    }

    public IContainer PrimaryLayer()
    {
        var layer = new ContainerImpl();
        _layers.Add((layer, true));
        return layer;
    }

    public Element Build(BuildContext context)
    {
        var primaryIndex = _layers.FindIndex(l => l.IsPrimary);
        if (primaryIndex < 0 || _layers.Count(l => l.IsPrimary) > 1)
        {
            throw new InvalidOperationException("Layers require exactly one PrimaryLayer().");
        }

        var below = _layers.Take(primaryIndex).Select(l => l.Container.Build(context)).ToList();
        var above = _layers.Skip(primaryIndex + 1).Select(l => l.Container.Build(context)).ToList();
        return new LayersElement(_layers[primaryIndex].Container.Build(context), below, above);
    }
}

internal sealed class TextDescriptor(string text) : ITextDescriptor
{
    private string? _family;
    private double _size = 12;
    private Color _color = Color.Black;
    private double _lineSpacing = 1.0;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private bool _strikethrough;
    private double _letterSpacing;
    private TextAlignment _alignment = TextAlignment.Left;
    private string _tagRole = "P";

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

    public ITextDescriptor Underline()
    {
        _underline = true;
        return this;
    }

    public ITextDescriptor Strikethrough()
    {
        _strikethrough = true;
        return this;
    }

    public ITextDescriptor LetterSpacing(double points)
    {
        _letterSpacing = points;
        return this;
    }

    public ITextDescriptor AlignLeft()
    {
        _alignment = TextAlignment.Left;
        return this;
    }

    public ITextDescriptor AlignCenter()
    {
        _alignment = TextAlignment.Center;
        return this;
    }

    public ITextDescriptor AlignRight()
    {
        _alignment = TextAlignment.Right;
        return this;
    }

    public ITextDescriptor Justify()
    {
        _alignment = TextAlignment.Justify;
        return this;
    }

    public ITextDescriptor Heading(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 6);
        _tagRole = "H" + level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return this;
    }

    public Element Build(BuildContext context) =>
        new TextElement(
            text,
            new TextStyle
            {
                Fonts = context.ResolveFonts(_family, _bold, _italic),
                FontSize = _size,
                Color = _color.ToLayout(),
                LineSpacing = _lineSpacing,
                Underline = _underline,
                Strikethrough = _strikethrough,
                LetterSpacing = _letterSpacing,
            },
            _alignment)
        {
            TagRole = _tagRole,
        };
}

internal sealed class TextSpanDescriptor : ITextSpanDescriptor
{
    public enum SpanKind
    {
        Literal,
        CurrentPageNumber,
        TotalPages,
    }

    public required SpanKind Kind { get; init; }

    public string Text { get; init; } = string.Empty;

    public string? Family { get; private set; }

    public double Size { get; private set; } = 12;

    public Color TextColor { get; private set; } = Color.Black;

    public bool IsBold { get; private set; }

    public bool IsItalic { get; private set; }

    public bool IsUnderline { get; private set; }

    public bool IsStrikethrough { get; private set; }

    public double Tracking { get; private set; }

    public int Script { get; private set; } // 0 = baseline, 1 = superscript, -1 = subscript

    public ITextSpanDescriptor FontFamily(string family)
    {
        Family = family;
        return this;
    }

    public ITextSpanDescriptor FontSize(double size)
    {
        Size = size;
        return this;
    }

    public ITextSpanDescriptor FontColor(Color color)
    {
        TextColor = color;
        return this;
    }

    public ITextSpanDescriptor Bold()
    {
        IsBold = true;
        return this;
    }

    public ITextSpanDescriptor Italic()
    {
        IsItalic = true;
        return this;
    }

    public ITextSpanDescriptor Underline()
    {
        IsUnderline = true;
        return this;
    }

    public ITextSpanDescriptor Strikethrough()
    {
        IsStrikethrough = true;
        return this;
    }

    public ITextSpanDescriptor LetterSpacing(double points)
    {
        Tracking = points;
        return this;
    }

    public ITextSpanDescriptor Superscript()
    {
        Script = 1;
        return this;
    }

    public ITextSpanDescriptor Subscript()
    {
        Script = -1;
        return this;
    }
}

internal sealed class TextContentDescriptor : ITextContentDescriptor
{
    private readonly List<TextSpanDescriptor> _spans = [];
    private TextAlignment _alignment = TextAlignment.Left;
    private double _lineSpacing = 1.0;

    public ITextSpanDescriptor Span(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var span = new TextSpanDescriptor { Kind = TextSpanDescriptor.SpanKind.Literal, Text = text };
        _spans.Add(span);
        return span;
    }

    public ITextSpanDescriptor CurrentPageNumber()
    {
        var span = new TextSpanDescriptor { Kind = TextSpanDescriptor.SpanKind.CurrentPageNumber };
        _spans.Add(span);
        return span;
    }

    public ITextSpanDescriptor TotalPages()
    {
        DescriptionScope.MarkUsesTotalPages();
        var span = new TextSpanDescriptor { Kind = TextSpanDescriptor.SpanKind.TotalPages };
        _spans.Add(span);
        return span;
    }

    public void AlignLeft() => _alignment = TextAlignment.Left;

    public void AlignCenter() => _alignment = TextAlignment.Center;

    public void AlignRight() => _alignment = TextAlignment.Right;

    public void Justify() => _alignment = TextAlignment.Justify;

    public void LineSpacing(double multiplier) => _lineSpacing = multiplier;

    public Element Build(BuildContext context)
    {
        var spans = new List<StyledSpan>(_spans.Count);
        foreach (var span in _spans)
        {
            var text = span.Kind switch
            {
                TextSpanDescriptor.SpanKind.CurrentPageNumber =>
                    context.CurrentPage.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TextSpanDescriptor.SpanKind.TotalPages =>
                    context.TotalPages?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?",
                _ => span.Text,
            };

            // Super/subscript shrink the glyphs and shift the baseline.
            var size = span.Script == 0 ? span.Size : span.Size * 0.65;
            var baselineShift = span.Script switch
            {
                1 => span.Size * 0.34,
                -1 => -span.Size * 0.14,
                _ => 0.0,
            };

            spans.Add(new StyledSpan(text, new TextStyle
            {
                Fonts = context.ResolveFonts(span.Family, span.IsBold, span.IsItalic),
                FontSize = size,
                Color = span.TextColor.ToLayout(),
                LineSpacing = _lineSpacing,
                Underline = span.IsUnderline,
                Strikethrough = span.IsStrikethrough,
                LetterSpacing = span.Tracking,
                BaselineShift = baselineShift,
            }));
        }

        return new TextElement(spans, _alignment);
    }
}
