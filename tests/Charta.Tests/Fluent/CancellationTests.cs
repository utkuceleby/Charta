using Charta.Layout;
using Charta.Layout.Elements;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class CancellationTests
{
    static CancellationTests() => FontManager.RegisterFont(SyntheticFont.Build());

    [Fact]
    public void PreCanceledToken_ThrowsBeforeAnyPage()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("AB")));
        using var buffer = new MemoryStream();

        Assert.Throws<OperationCanceledException>(() =>
            document.GeneratePdf(buffer, cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void CancellationDuringGeneration_StopsAtNextPageBoundary()
    {
        using var cts = new CancellationTokenSource();
        var pagesStarted = 0;

        // The header factory runs at the start of every page: cancel while composing page 2.
        var section = new PageSection
        {
            Content = new ColumnElement(Enumerable.Range(0, 5).Select(Element (_) => new PageBreakForcer()).ToList()),
            Header = page =>
            {
                pagesStarted++;
                if (page == 2)
                {
                    cts.Cancel();
                }

                return EmptyElement.Instance;
            },
        };

        Assert.Throws<OperationCanceledException>(() =>
            LayoutDocument.Generate(Stream.Null, [section], OverflowBehavior.Clip, cancellationToken: cts.Token));

        Assert.Equal(2, pagesStarted); // page 3 was never started
    }

    /// <summary>Occupies a full page and forces continuation, so the document spans many pages.</summary>
    private sealed class PageBreakForcer : Element
    {
        private bool _done;

        public override MeasureResult Measure(in LayoutConstraints constraints) =>
            _done ? MeasureResult.Complete(0, 0) : MeasureResult.Partial(10, constraints.AvailableHeight);

        public override void Draw(DrawingContext context, in LayoutRect bounds) => _done = true;
    }
}
