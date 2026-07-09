using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta.Fluent;

internal sealed class TableCellDescriptor : ITableCellDescriptor
{
    public ContainerImpl Container { get; } = new();

    public int Columns { get; private set; } = 1;

    public int Rows { get; private set; } = 1;

    public ITableCellDescriptor ColumnSpan(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        Columns = columns;
        return this;
    }

    public ITableCellDescriptor RowSpan(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        Rows = rows;
        return this;
    }
}

internal sealed class TableColumnsDescriptor : ITableColumnsDescriptor
{
    public List<TableColumnDefinition> Columns { get; } = [];

    public void ConstantColumn(double width) => Columns.Add(new TableColumnDefinition { FixedWidth = width });

    public void RelativeColumn(double weight = 1) => Columns.Add(new TableColumnDefinition { Weight = weight });
}

internal sealed class TableHeaderDescriptor : ITableHeaderDescriptor
{
    public List<TableCellDescriptor> Cells { get; } = [];

    public ITableCellDescriptor Cell()
    {
        var cell = new TableCellDescriptor();
        Cells.Add(cell);
        return cell;
    }
}

internal sealed class TableDescriptor : ITableDescriptor
{
    private readonly List<TableCellDescriptor> _cells = [];
    private List<TableColumnDefinition>? _columns;
    private TableHeaderDescriptor? _header;

    public void ColumnsDefinition(Action<ITableColumnsDescriptor> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (_columns is not null)
        {
            throw new InvalidOperationException("ColumnsDefinition can only be set once per table.");
        }

        var descriptor = new TableColumnsDescriptor();
        columns(descriptor);
        _columns = descriptor.Columns;
    }

    public void Header(Action<ITableHeaderDescriptor> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = new TableHeaderDescriptor();
        header(_header);
    }

    public ITableCellDescriptor Cell()
    {
        var cell = new TableCellDescriptor();
        _cells.Add(cell);
        return cell;
    }

    public Element Build(BuildContext context)
    {
        if (_columns is null || _columns.Count == 0)
        {
            throw new InvalidOperationException("The table needs columns: call table.ColumnsDefinition(...) first.");
        }

        var placedBody = PlaceCells(_cells, _columns.Count, out var rowCount);
        var bodyCells = placedBody
            .Select(p => new TableCell
            {
                Row = p.Row,
                Column = p.Column,
                RowSpan = p.Cell.Rows,
                ColumnSpan = p.ColumnSpan,
                Element = p.Cell.Container.Build(context),
            })
            .ToList();

        var headerCells = new List<TableHeaderCell>();
        var headerRowCount = 0;
        if (_header is { Cells.Count: > 0 })
        {
            foreach (var p in PlaceCells(_header.Cells, _columns.Count, out headerRowCount))
            {
                var container = p.Cell.Container;
                headerCells.Add(new TableHeaderCell
                {
                    Row = p.Row,
                    Column = p.Column,
                    RowSpan = p.Cell.Rows,
                    ColumnSpan = p.ColumnSpan,
                    Factory = () => container.Build(context),
                });
            }
        }

        return new TableElement(_columns, bodyCells, rowCount, headerCells, headerRowCount);
    }

    /// <summary>Row-major auto-placement flowing around occupied span areas.</summary>
    private static List<(TableCellDescriptor Cell, int Row, int Column, int ColumnSpan)> PlaceCells(
        List<TableCellDescriptor> cells,
        int columnCount,
        out int rowCount)
    {
        var occupied = new List<bool[]>();
        var placed = new List<(TableCellDescriptor, int, int, int)>();

        foreach (var cell in cells)
        {
            var columnSpan = Math.Min(cell.Columns, columnCount);
            var (row, column) = FindSlot(occupied, columnCount, columnSpan);
            for (var r = row; r < row + cell.Rows; r++)
            {
                while (occupied.Count <= r)
                {
                    occupied.Add(new bool[columnCount]);
                }

                for (var c = column; c < column + columnSpan; c++)
                {
                    occupied[r][c] = true;
                }
            }

            placed.Add((cell, row, column, columnSpan));
        }

        rowCount = occupied.Count;
        return placed;
    }

    private static (int Row, int Column) FindSlot(List<bool[]> occupied, int columnCount, int columnSpan)
    {
        for (var row = 0; ; row++)
        {
            while (occupied.Count <= row)
            {
                occupied.Add(new bool[columnCount]);
            }

            for (var column = 0; column + columnSpan <= columnCount; column++)
            {
                var free = true;
                for (var c = column; c < column + columnSpan; c++)
                {
                    if (occupied[row][c])
                    {
                        free = false;
                        break;
                    }
                }

                if (free)
                {
                    return (row, column);
                }
            }
        }
    }
}
