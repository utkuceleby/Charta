namespace Charta.Layout.Elements;

/// <summary>A table column: fixed width in points, or a weight over the leftover space.</summary>
internal sealed class TableColumnDefinition
{
    public double? FixedWidth { get; init; }

    public double Weight { get; init; } = 1;
}

/// <summary>A grid-placed body cell.</summary>
internal sealed class TableCell
{
    public required int Row { get; init; }

    public required int Column { get; init; }

    public required int RowSpan { get; init; }

    public required int ColumnSpan { get; init; }

    public required Element Element { get; init; }
}

/// <summary>A grid-placed header cell. A factory, not an element: headers redraw on every page.</summary>
internal sealed class TableHeaderCell
{
    public required int Row { get; init; }

    public required int Column { get; init; }

    public required int RowSpan { get; init; }

    public required int ColumnSpan { get; init; }

    public required Func<Element> Factory { get; init; }
}

/// <summary>
/// The table layout engine. Pagination follows the band model: rows are grouped into the maximal
/// ranges not crossed by any active rowspan, and pages may only break between bands — rows without
/// rowspans each form their own band, so ordinary tables paginate row by row. A band taller than a
/// whole page is clipped inside the table with a diagnostic and the table continues with the next
/// band on the next page: pagination can neither hang nor silently drop the rest of the table.
/// </summary>
internal sealed class TableElement : Element
{
    private readonly IReadOnlyList<TableColumnDefinition> _columns;
    private readonly IReadOnlyList<TableCell> _cells;
    private readonly IReadOnlyList<TableHeaderCell> _headerCells;
    private readonly int _headerRowCount;
    private readonly List<(int StartRow, int EndRow, List<TableCell> Cells)> _bands = [];

    private int _nextBand;
    private double _pageCapacity;

    // Caches keyed by the resolved total width (tables are almost always laid out at one width).
    private double _cachedWidth = double.NaN;
    private double[] _columnWidths = [];
    private double[] _bandHeights = [];
    private double _headerHeight;

    public TableElement(
        IReadOnlyList<TableColumnDefinition> columns,
        IReadOnlyList<TableCell> cells,
        int rowCount,
        IReadOnlyList<TableHeaderCell> headerCells,
        int headerRowCount)
    {
        _columns = columns;
        _cells = cells;
        _headerCells = headerCells;
        _headerRowCount = headerRowCount;
        BuildBands(rowCount);
    }

    private void BuildBands(int rowCount)
    {
        // A boundary below row r is blocked when some cell spans across it.
        var blocked = new bool[rowCount];
        foreach (var cell in _cells)
        {
            for (var r = cell.Row; r < cell.Row + cell.RowSpan - 1; r++)
            {
                blocked[r] = true;
            }
        }

        var start = 0;
        for (var r = 0; r < rowCount; r++)
        {
            if (!blocked[r])
            {
                _bands.Add((start, r, []));
                start = r + 1;
            }
        }

        foreach (var cell in _cells)
        {
            var band = _bands.FindIndex(b => b.StartRow <= cell.Row && cell.Row <= b.EndRow);
            _bands[band].Cells.Add(cell);
        }
    }

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        EnsureLayout(constraints.AvailableWidth);
        var totalWidth = _columnWidths.Sum();
        var bodyAvailable = constraints.AvailableHeight - _headerHeight;
        _pageCapacity = Math.Max(_pageCapacity, bodyAvailable);

        if (_nextBand >= _bands.Count)
        {
            return MeasureResult.Complete(0, 0);
        }

        var y = 0.0;
        var band = _nextBand;
        while (band < _bands.Count && y + _bandHeights[band] <= bodyAvailable)
        {
            y += _bandHeights[band];
            band++;
        }

        if (band == _nextBand)
        {
            // Nothing fits. A fresh page can hold the band → wait for one; otherwise claim the space
            // and let Draw clip the band so the table can continue afterwards.
            return _bandHeights[band] <= _pageCapacity
                ? MeasureResult.Empty
                : MeasureResult.Partial(totalWidth, Math.Max(0, constraints.AvailableHeight));
        }

        return band == _bands.Count
            ? MeasureResult.Complete(totalWidth, _headerHeight + y)
            : MeasureResult.Partial(totalWidth, _headerHeight + y);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        EnsureLayout(bounds.Width);
        if (_nextBand >= _bands.Count)
        {
            return;
        }

        var y = bounds.Y;
        if (_headerCells.Count > 0)
        {
            DrawHeader(context, bounds.X, y);
            y += _headerHeight;
        }

        var bottom = bounds.Y + bounds.Height;
        var drewAny = false;
        while (_nextBand < _bands.Count)
        {
            var bandHeight = _bandHeights[_nextBand];
            if (y + bandHeight <= bottom + 0.01)
            {
                DrawBand(context, _nextBand, bounds.X, y);
                y += bandHeight;
                _nextBand++;
                drewAny = true;
                continue;
            }

            if (!drewAny && bandHeight > _pageCapacity)
            {
                // The band cannot fit any page: clip it to the remaining space and move on.
                var clipRect = new LayoutRect(bounds.X, y, bounds.Width, Math.Max(0, bottom - y));
                context.ReportOverflow(this, new LayoutSize(bounds.Width, bandHeight), new LayoutConstraints(bounds.Width, clipRect.Height));
                var bandIndex = _nextBand;
                var bandY = y;
                var x = bounds.X;
                context.Clipped(clipRect, () => DrawBand(context, bandIndex, x, bandY));
                if (context.DebugOverflow)
                {
                    context.DrawOverflowMarker(clipRect);
                }

                _nextBand++;
            }

            return;
        }
    }

    private void EnsureLayout(double availableWidth)
    {
        if (_cachedWidth.Equals(availableWidth))
        {
            return;
        }

        _cachedWidth = availableWidth;
        _columnWidths = ResolveColumnWidths(availableWidth);
        _bandHeights = new double[_bands.Count];
        for (var i = 0; i < _bands.Count; i++)
        {
            _bandHeights[i] = ComputeRowHeights(_bands[i]).Sum();
        }

        _headerHeight = _headerCells.Count > 0 ? ComputeHeaderRowHeights().Sum() : 0;
    }

    private double[] ResolveColumnWidths(double availableWidth)
    {
        var widths = new double[_columns.Count];
        var remaining = availableWidth;
        var totalWeight = 0.0;
        for (var i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].FixedWidth is { } fixedWidth)
            {
                widths[i] = fixedWidth;
                remaining -= fixedWidth;
            }
            else
            {
                totalWeight += _columns[i].Weight;
            }
        }

        remaining = Math.Max(0, remaining);
        for (var i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].FixedWidth is null)
            {
                widths[i] = totalWeight > 0 ? remaining * _columns[i].Weight / totalWeight : 0;
            }
        }

        return widths;
    }

    private double CellWidth(int column, int span)
    {
        var width = 0.0;
        for (var i = column; i < Math.Min(column + span, _columnWidths.Length); i++)
        {
            width += _columnWidths[i];
        }

        return width;
    }

    private double[] ComputeRowHeights((int StartRow, int EndRow, List<TableCell> Cells) band)
    {
        var heights = new double[band.EndRow - band.StartRow + 1];

        // Single-row cells set the row minimums; rowspan cells then stretch the rows they cover.
        foreach (var cell in band.Cells)
        {
            if (cell.RowSpan == 1)
            {
                var natural = NaturalHeight(cell.Element, CellWidth(cell.Column, cell.ColumnSpan));
                var index = cell.Row - band.StartRow;
                heights[index] = Math.Max(heights[index], natural);
            }
        }

        foreach (var cell in band.Cells)
        {
            if (cell.RowSpan > 1)
            {
                var natural = NaturalHeight(cell.Element, CellWidth(cell.Column, cell.ColumnSpan));
                var first = cell.Row - band.StartRow;
                var last = first + cell.RowSpan - 1;
                var covered = 0.0;
                for (var i = first; i <= last; i++)
                {
                    covered += heights[i];
                }

                if (natural > covered)
                {
                    heights[last] += natural - covered; // deficit goes to the last spanned row
                }
            }
        }

        return heights;
    }

    private static double NaturalHeight(Element element, double width) =>
        element.Measure(new LayoutConstraints(width, double.PositiveInfinity)).Size.Height;

    private void DrawBand(DrawingContext context, int bandIndex, double x, double y)
    {
        var band = _bands[bandIndex];
        var heights = ComputeRowHeights(band);

        foreach (var cell in band.Cells)
        {
            var cellX = x + CellWidth(0, cell.Column);
            var firstRow = cell.Row - band.StartRow;
            var cellY = y;
            for (var i = 0; i < firstRow; i++)
            {
                cellY += heights[i];
            }

            var cellHeight = 0.0;
            for (var i = firstRow; i < Math.Min(firstRow + cell.RowSpan, heights.Length); i++)
            {
                cellHeight += heights[i];
            }

            cell.Element.Draw(context, new LayoutRect(cellX, cellY, CellWidth(cell.Column, cell.ColumnSpan), cellHeight));
        }
    }

    /// <summary>Measures fresh header instances (headers are factories) to find the row heights.</summary>
    private double[] ComputeHeaderRowHeights()
    {
        var heights = new double[_headerRowCount];
        var elements = _headerCells.Select(c => c.Factory()).ToList();
        for (var i = 0; i < _headerCells.Count; i++)
        {
            var cell = _headerCells[i];
            if (cell.RowSpan != 1)
            {
                continue;
            }

            var natural = NaturalHeight(elements[i], CellWidth(cell.Column, cell.ColumnSpan));
            heights[cell.Row] = Math.Max(heights[cell.Row], natural);
        }

        for (var i = 0; i < _headerCells.Count; i++)
        {
            var cell = _headerCells[i];
            if (cell.RowSpan <= 1)
            {
                continue;
            }

            var natural = NaturalHeight(elements[i], CellWidth(cell.Column, cell.ColumnSpan));
            var covered = 0.0;
            for (var r = cell.Row; r < Math.Min(cell.Row + cell.RowSpan, heights.Length); r++)
            {
                covered += heights[r];
            }

            if (natural > covered)
            {
                heights[Math.Min(cell.Row + cell.RowSpan, heights.Length) - 1] += natural - covered;
            }
        }

        return heights;
    }

    private void DrawHeader(DrawingContext context, double x, double y)
    {
        var heights = ComputeHeaderRowHeights();
        foreach (var cell in _headerCells)
        {
            var element = cell.Factory(); // fresh per page: element trees carry pagination cursors
            var cellX = x + CellWidth(0, cell.Column);
            var cellY = y;
            for (var r = 0; r < cell.Row; r++)
            {
                cellY += heights[r];
            }

            var cellHeight = 0.0;
            for (var r = cell.Row; r < Math.Min(cell.Row + cell.RowSpan, heights.Length); r++)
            {
                cellHeight += heights[r];
            }

            var width = CellWidth(cell.Column, cell.ColumnSpan);
            _ = element.Measure(new LayoutConstraints(width, double.PositiveInfinity));
            element.Draw(context, new LayoutRect(cellX, cellY, width, cellHeight));
        }
    }
}
