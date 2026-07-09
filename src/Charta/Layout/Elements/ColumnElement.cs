namespace Charta.Layout.Elements;

/// <summary>
/// Stacks children vertically with optional spacing. The primary paginating container: its cursor is
/// the index of the first unfinished child; a child's own partial state nests naturally.
/// Overflow handling follows the retry-then-clip rule: a child that overflows mid-page gets a fresh
/// page first; a child that overflows a fresh page is clipped with a diagnostic (never an exception,
/// never a hang).
/// </summary>
internal sealed class ColumnElement(IReadOnlyList<Element> children, double spacing = 0) : Element
{
    private int _index;

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var y = 0.0;
        var width = 0.0;
        var anyRendered = false;

        for (var i = _index; i < children.Count; i++)
        {
            var spacingBefore = anyRendered ? spacing : 0;
            var remaining = constraints.AvailableHeight - y - spacingBefore;
            if (remaining <= 0)
            {
                return MeasureResult.Partial(width, y);
            }

            var child = children[i].Measure(new LayoutConstraints(constraints.AvailableWidth, remaining));
            switch (child.Verdict)
            {
                case LayoutVerdict.Complete:
                    y += spacingBefore + child.Size.Height;
                    width = Math.Max(width, child.Size.Width);
                    anyRendered = true;
                    break;

                case LayoutVerdict.Partial:
                    y += spacingBefore + child.Size.Height;
                    return MeasureResult.Partial(Math.Max(width, child.Size.Width), y);

                case LayoutVerdict.Empty:
                    return anyRendered ? MeasureResult.Partial(width, y) : MeasureResult.Empty;

                case LayoutVerdict.Overflowing:
                    if (anyRendered)
                    {
                        // Mid-page: give the child a fresh page before resorting to clipping.
                        return MeasureResult.Partial(width, y);
                    }

                    // Fresh constraints and still too big: it will be clipped to the remaining space.
                    y += spacingBefore + remaining;
                    return MeasureResult.Partial(Math.Max(width, child.Size.Width), y);

                default:
                    throw new InvalidOperationException($"Unknown verdict {child.Verdict}.");
            }
        }

        return MeasureResult.Complete(width, y);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        var y = bounds.Y;
        var anyRendered = false;

        while (_index < children.Count)
        {
            var spacingBefore = anyRendered ? spacing : 0;
            var remaining = bounds.Y + bounds.Height - y - spacingBefore;
            if (remaining <= 0)
            {
                return;
            }

            var element = children[_index];
            var child = element.Measure(new LayoutConstraints(bounds.Width, remaining));
            var childBounds = new LayoutRect(bounds.X, y + spacingBefore, bounds.Width, child.Size.Height);

            switch (child.Verdict)
            {
                case LayoutVerdict.Complete:
                    element.Draw(context, childBounds);
                    y += spacingBefore + child.Size.Height;
                    anyRendered = true;
                    _index++;
                    break;

                case LayoutVerdict.Partial:
                    element.Draw(context, childBounds);
                    return; // the child advanced its own cursor; it continues on the next page

                case LayoutVerdict.Empty:
                    return;

                case LayoutVerdict.Overflowing:
                    if (anyRendered)
                    {
                        return; // retry on a fresh page
                    }

                    var clipBounds = new LayoutRect(bounds.X, y + spacingBefore, bounds.Width, remaining);
                    context.ReportOverflow(element, child.Size, new LayoutConstraints(bounds.Width, remaining));
                    var captured = element;
                    var capturedBounds = new LayoutRect(clipBounds.X, clipBounds.Y, child.Size.Width, child.Size.Height);
                    context.Clipped(clipBounds, () => captured.Draw(context, capturedBounds));
                    if (context.DebugOverflow)
                    {
                        context.DrawOverflowMarker(clipBounds);
                    }

                    _index++;
                    return; // the clip consumed the rest of the page

                default:
                    throw new InvalidOperationException($"Unknown verdict {child.Verdict}.");
            }
        }
    }
}
