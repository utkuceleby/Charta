namespace Charta;

/// <summary>Describes a table: column definitions, an optional repeating header, and cells.</summary>
public interface ITableDescriptor
{
    /// <summary>Defines the columns. Required, exactly once, before any cells are meaningful.</summary>
    void ColumnsDefinition(Action<ITableColumnsDescriptor> columns);

    /// <summary>Header rows repeated at the top of the table on every page.</summary>
    void Header(Action<ITableHeaderDescriptor> header);

    /// <summary>
    /// Adds the next cell. Cells fill the grid left-to-right, top-to-bottom, flowing around spans.
    /// </summary>
    ITableCellDescriptor Cell();
}

/// <summary>Column definitions for a table.</summary>
public interface ITableColumnsDescriptor
{
    /// <summary>A column with a fixed width in points.</summary>
    void ConstantColumn(double width);

    /// <summary>A column taking a weighted share of the leftover width.</summary>
    void RelativeColumn(double weight = 1);
}

/// <summary>Header cells for a table.</summary>
public interface ITableHeaderDescriptor
{
    /// <summary>Adds the next header cell (same auto-placement as body cells).</summary>
    ITableCellDescriptor Cell();
}

/// <summary>
/// A table cell. It is an <see cref="IContainer"/>: style and fill it with the usual extension
/// methods (<c>.Padding(...)</c>, <c>.Text(...)</c>, …) after setting the spans.
/// </summary>
public interface ITableCellDescriptor : IContainer
{
    /// <summary>Spans this cell across multiple columns.</summary>
    ITableCellDescriptor ColumnSpan(int columns);

    /// <summary>Spans this cell across multiple rows. Spanned rows paginate as one unbreakable band.</summary>
    ITableCellDescriptor RowSpan(int rows);
}
