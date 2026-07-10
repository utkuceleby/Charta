using Charta.Fluent;
using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta;

/// <summary>Describes the document: one or more page runs plus document properties.</summary>
public interface IDocumentDescriptor
{
    /// <summary>Adds a run of pages sharing a size, margins, and repeating header/footer bands.</summary>
    void Page(Action<IPageDescriptor> configure);

    /// <summary>Sets document properties (Info dictionary and XMP metadata).</summary>
    void Metadata(Action<IMetadataDescriptor> configure);
}

/// <summary>Document properties. Unset values are omitted from the output.</summary>
public interface IMetadataDescriptor
{
    /// <summary>Document title.</summary>
    IMetadataDescriptor Title(string title);

    /// <summary>Document author.</summary>
    IMetadataDescriptor Author(string author);

    /// <summary>Document subject/description.</summary>
    IMetadataDescriptor Subject(string subject);

    /// <summary>Keywords, comma-separated by convention.</summary>
    IMetadataDescriptor Keywords(string keywords);

    /// <summary>The application that created the original content.</summary>
    IMetadataDescriptor Creator(string creator);

    /// <summary>
    /// Creation timestamp. Charta never reads the system clock — set this explicitly, or leave it
    /// unset for byte-identical output across runs.
    /// </summary>
    IMetadataDescriptor CreationDate(DateTimeOffset timestamp);
}

/// <summary>Describes one page run.</summary>
public interface IPageDescriptor
{
    /// <summary>Page size. Default: A4.</summary>
    void Size(PageSize size);

    /// <summary>All four margins, in points. Default: 42.5 pt (1.5 cm).</summary>
    void Margin(double points);

    /// <summary>All four margins in the given unit.</summary>
    void Margin(double value, Unit unit);

    /// <summary>The band repeated at the top of every page.</summary>
    IContainer Header();

    /// <summary>The flowing content that paginates across pages.</summary>
    IContainer Content();

    /// <summary>The band repeated at the bottom of every page.</summary>
    IContainer Footer();
}

/// <summary>Describes a vertical stack.</summary>
public interface IColumnDescriptor
{
    /// <summary>Vertical gap between items, in points.</summary>
    void Spacing(double value);

    /// <summary>Adds the next item slot.</summary>
    IContainer Item();
}

/// <summary>Describes a horizontal arrangement.</summary>
public interface IRowDescriptor
{
    /// <summary>Horizontal gap between items, in points.</summary>
    void Spacing(double value);

    /// <summary>Adds an item taking a weighted share of the leftover width.</summary>
    IContainer RelativeItem(double weight = 1);

    /// <summary>Adds an item with a fixed width in points.</summary>
    IContainer ConstantItem(double width);
}

/// <summary>Fluent styling for a single-style text block.</summary>
public interface ITextDescriptor
{
    /// <summary>Font family; resolved against registered fonts first, then system fonts.</summary>
    ITextDescriptor FontFamily(string family);

    /// <summary>Font size in points. Default: 12.</summary>
    ITextDescriptor FontSize(double size);

    /// <summary>Text color. Default: black.</summary>
    ITextDescriptor FontColor(Color color);

    /// <summary>Line height multiplier over the font's natural line height. Default: 1.0.</summary>
    ITextDescriptor LineSpacing(double multiplier);

    /// <summary>Selects the bold face of the family.</summary>
    ITextDescriptor Bold();

    /// <summary>Selects the italic face of the family.</summary>
    ITextDescriptor Italic();

    /// <summary>Underlines the text.</summary>
    ITextDescriptor Underline();

    /// <summary>Strikes through the text.</summary>
    ITextDescriptor Strikethrough();

    /// <summary>Extra space between letters (tracking), in points.</summary>
    ITextDescriptor LetterSpacing(double points);

    /// <summary>Left-aligns the lines (default).</summary>
    ITextDescriptor AlignLeft();

    /// <summary>Centers the lines within the block.</summary>
    ITextDescriptor AlignCenter();

    /// <summary>Right-aligns the lines.</summary>
    ITextDescriptor AlignRight();

    /// <summary>Justifies the lines; the last line of each paragraph stays left-aligned.</summary>
    ITextDescriptor Justify();

    /// <summary>Tags the block as a heading of the given level (1–6) for accessibility (PDF/UA).</summary>
    ITextDescriptor Heading(int level);
}

/// <summary>Styling for one span inside a rich text block.</summary>
public interface ITextSpanDescriptor
{
    /// <summary>Font family; resolved against registered fonts first, then system fonts.</summary>
    ITextSpanDescriptor FontFamily(string family);

    /// <summary>Font size in points. Default: 12.</summary>
    ITextSpanDescriptor FontSize(double size);

    /// <summary>Text color. Default: black.</summary>
    ITextSpanDescriptor FontColor(Color color);

    /// <summary>Selects the bold face of the family.</summary>
    ITextSpanDescriptor Bold();

    /// <summary>Selects the italic face of the family.</summary>
    ITextSpanDescriptor Italic();

    /// <summary>Underlines the span.</summary>
    ITextSpanDescriptor Underline();

    /// <summary>Strikes through the span.</summary>
    ITextSpanDescriptor Strikethrough();

    /// <summary>Extra space between letters (tracking), in points.</summary>
    ITextSpanDescriptor LetterSpacing(double points);

    /// <summary>Renders the span smaller and raised (e.g. exponents, ordinals).</summary>
    ITextSpanDescriptor Superscript();

    /// <summary>Renders the span smaller and lowered (e.g. chemical subscripts).</summary>
    ITextSpanDescriptor Subscript();
}

/// <summary>A rich text block: multiple styled spans flowing as one paragraph stream.</summary>
public interface ITextContentDescriptor
{
    /// <summary>Adds a styled fragment.</summary>
    ITextSpanDescriptor Span(string text);

    /// <summary>
    /// Adds the current page number. Resolved per page in headers and footers; in flowing content
    /// it binds to the page the element tree was built for.
    /// </summary>
    ITextSpanDescriptor CurrentPageNumber();

    /// <summary>
    /// Adds the total page count. Enables a second generation pass: the document is laid out once
    /// to count pages, then rendered with the real number.
    /// </summary>
    ITextSpanDescriptor TotalPages();

    /// <summary>Left-aligns the lines (default).</summary>
    void AlignLeft();

    /// <summary>Centers the lines within the block.</summary>
    void AlignCenter();

    /// <summary>Right-aligns the lines.</summary>
    void AlignRight();

    /// <summary>Justifies the lines; the last line of each paragraph stays left-aligned.</summary>
    void Justify();

    /// <summary>Line height multiplier for the whole block. Default: 1.0.</summary>
    void LineSpacing(double multiplier);
}

/// <summary>Describes stacked layers sharing the same bounds.</summary>
public interface ILayersDescriptor
{
    /// <summary>Adds a decoration layer. Layers declared before the primary render below it, after — above.</summary>
    IContainer Layer();

    /// <summary>The layer that defines size and pagination. Exactly one is required.</summary>
    IContainer PrimaryLayer();
}

/// <summary>A reusable document fragment.</summary>
public interface IComponent
{
    /// <summary>Composes the fragment into the given container.</summary>
    void Compose(IContainer container);
}
