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

/// <summary>Fluent styling for a text block.</summary>
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
}

/// <summary>A reusable document fragment.</summary>
public interface IComponent
{
    /// <summary>Composes the fragment into the given container.</summary>
    void Compose(IContainer container);
}
