using Charta.Layout;
using Charta.Layout.Elements;
using Xunit;

namespace Charta.Tests.Layout;

public class ElementTests
{
    [Fact]
    public void Padding_AddsToChildSize_AndInsetsDrawBounds()
    {
        var child = new FixedElement(100, 50);
        var padding = new PaddingElement(child, 10);

        var result = padding.Measure(LayoutTestHelpers.Constraints(200, 200));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(120, result.Size.Width);
        Assert.Equal(70, result.Size.Height);
    }

    [Fact]
    public void Padding_LargerThanAvailableSpace_Overflows()
    {
        var padding = new PaddingElement(new FixedElement(1, 1), 100);

        var result = padding.Measure(LayoutTestHelpers.Constraints(150, 150));

        Assert.Equal(LayoutVerdict.Overflowing, result.Verdict);
    }

    [Fact]
    public void Background_EmitsFillBeforeChild()
    {
        var context = LayoutTestHelpers.CreateContext();
        var background = new BackgroundElement(new FixedElement(10, 10), LayoutColor.FromRgb(255, 0, 0));

        background.Draw(context, new LayoutRect(5, 5, 100, 50));

        // Top-left (5,5) 100x50 on an 800pt page → PDF rect y = 800 - 5 - 50 = 745.
        Assert.Contains("1 0 0 rg\n5 745 100 50 re f", context.GetContent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Border_EmitsStrokeRect()
    {
        var context = LayoutTestHelpers.CreateContext();
        var border = new BorderElement(new FixedElement(10, 10), 2, LayoutColor.Black);

        border.Draw(context, new LayoutRect(0, 0, 100, 100));

        Assert.Contains("2 w\n0 700 100 100 re S", context.GetContent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Align_CentersChildWithinBounds()
    {
        var context = LayoutTestHelpers.CreateContext();
        var child = new FixedElement(50, 20);
        var align = new AlignElement(new BackgroundElement(child, LayoutColor.Black), HorizontalAlignment.Center);

        var measured = align.Measure(LayoutTestHelpers.Constraints(200, 100));
        align.Draw(context, new LayoutRect(0, 0, 200, 20));

        Assert.Equal(200, measured.Size.Width); // fills the available width
        // x = (200 - 50) / 2 = 75; child height 20 at top → PDF y = 800 - 20 = 780.
        Assert.Contains("75 780 50 20 re f", context.GetContent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Constrained_MinWiderThanAvailable_Overflows()
    {
        var constrained = new ConstrainedElement(new FixedElement(10, 10), minWidth: 500);

        Assert.Equal(LayoutVerdict.Overflowing, constrained.Measure(LayoutTestHelpers.Constraints(100, 100)).Verdict);
    }

    [Fact]
    public void Constrained_ClampsChildConstraints()
    {
        var constrained = new ConstrainedElement(new FixedElement(300, 10), maxWidth: 200);

        // The child wants 300 wide but only 200 is offered → child overflows.
        Assert.Equal(LayoutVerdict.Overflowing, constrained.Measure(LayoutTestHelpers.Constraints(1000, 100)).Verdict);
    }

    [Fact]
    public void Row_ResolvesFixedAndWeightedWidths()
    {
        var left = new FixedElement(0, 30);
        var right = new FixedElement(0, 40);
        var row = new RowElement(
        [
            new RowItem { Element = left, FixedWidth = 100 },
            new RowItem { Element = right, Weight = 1 },
        ]);

        var result = row.Measure(LayoutTestHelpers.Constraints(500, 200));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(500, result.Size.Width);
        Assert.Equal(40, result.Size.Height); // tallest child
    }

    [Fact]
    public void Row_TallerThanAvailable_Overflows()
    {
        var row = new RowElement([new RowItem { Element = new FixedElement(10, 500) }]);

        Assert.Equal(LayoutVerdict.Overflowing, row.Measure(LayoutTestHelpers.Constraints(100, 100)).Verdict);
    }

    [Fact]
    public void PageBreak_ReportsPartialUntilDrawn()
    {
        var pageBreak = new PageBreakElement();
        var context = LayoutTestHelpers.CreateContext();

        Assert.Equal(LayoutVerdict.Partial, pageBreak.Measure(LayoutTestHelpers.Constraints(100, 100)).Verdict);
        pageBreak.Draw(context, default);
        Assert.Equal(LayoutVerdict.Complete, pageBreak.Measure(LayoutTestHelpers.Constraints(100, 100)).Verdict);
    }
}

public class ColumnTests
{
    [Fact]
    public void Column_StacksChildren_WithSpacing()
    {
        var column = new ColumnElement([new FixedElement(50, 100), new FixedElement(80, 100)], spacing: 10);

        var result = column.Measure(LayoutTestHelpers.Constraints(200, 500));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(80, result.Size.Width);
        Assert.Equal(210, result.Size.Height);
    }

    [Fact]
    public void Column_SplitsAcrossPages_AndResumesFromCursor()
    {
        var first = new FixedElement(10, 300);
        var second = new FixedElement(10, 300);
        var third = new FixedElement(10, 300);
        var column = new ColumnElement([first, second, third]);
        var constraints = LayoutTestHelpers.Constraints(100, 700);
        var context = LayoutTestHelpers.CreateContext();

        // Page 1: two children fit, the third does not.
        Assert.Equal(LayoutVerdict.Partial, column.Measure(constraints).Verdict);
        column.Draw(context, new LayoutRect(0, 0, 100, 700));
        Assert.Equal(1, first.DrawCount);
        Assert.Equal(1, second.DrawCount);
        Assert.Equal(0, third.DrawCount);

        // Page 2: the remaining child completes.
        Assert.Equal(LayoutVerdict.Complete, column.Measure(constraints).Verdict);
        column.Draw(context, new LayoutRect(0, 0, 100, 700));
        Assert.Equal(1, third.DrawCount);
    }

    [Fact]
    public void Column_OverflowingChildMidPage_GetsFreshPageBeforeClipping()
    {
        var small = new FixedElement(10, 100);
        var huge = new FixedElement(10, 650); // fits alone, not after `small`
        var column = new ColumnElement([small, huge]);
        var constraints = LayoutTestHelpers.Constraints(100, 700);
        var diagnostics = new List<LayoutDiagnostic>();
        var context = LayoutTestHelpers.CreateContext(diagnostics);

        Assert.Equal(LayoutVerdict.Partial, column.Measure(constraints).Verdict);
        column.Draw(context, new LayoutRect(0, 0, 100, 700));
        Assert.Equal(0, huge.DrawCount); // deferred to the fresh page

        Assert.Equal(LayoutVerdict.Complete, column.Measure(constraints).Verdict);
        column.Draw(context, new LayoutRect(0, 0, 100, 700));
        Assert.Equal(1, huge.DrawCount);
        Assert.Empty(diagnostics); // no clipping was needed
    }

    [Fact]
    public void Column_OverflowingChildOnFreshPage_ClipsWithDiagnostic()
    {
        var huge = new FixedElement(10, 2000);
        var column = new ColumnElement([huge]);
        var diagnostics = new List<LayoutDiagnostic>();
        var context = LayoutTestHelpers.CreateContext(diagnostics);

        Assert.Equal(LayoutVerdict.Partial, column.Measure(LayoutTestHelpers.Constraints(100, 700)).Verdict);
        column.Draw(context, new LayoutRect(0, 0, 100, 700));

        Assert.Equal(1, huge.DrawCount);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("clipped", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("re W n", context.GetContent(), StringComparison.Ordinal); // clip path emitted

        Assert.Equal(LayoutVerdict.Complete, column.Measure(LayoutTestHelpers.Constraints(100, 700)).Verdict);
    }

    [Fact]
    public void Column_ThrowPolicy_RaisesLayoutException()
    {
        var column = new ColumnElement([new FixedElement(10, 2000)]);
        var context = LayoutTestHelpers.CreateContext(overflow: OverflowBehavior.Throw);

        column.Measure(LayoutTestHelpers.Constraints(100, 700));

        Assert.Throws<LayoutException>(() => column.Draw(context, new LayoutRect(0, 0, 100, 700)));
    }
}
