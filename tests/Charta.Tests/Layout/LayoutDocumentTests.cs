using Charta.Cos;
using Charta.Fonts;
using Charta.Layout;
using Charta.Layout.Elements;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Layout;

public class LayoutDocumentTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    private static TextStyle Style(double size = 12) => new()
    {
        Fonts = new FontChain(PdfFont.Parse(SyntheticFont.Build())),
        FontSize = size,
    };

    [Fact]
    public void Generate_SinglePageDocument()
    {
        var document = new LayoutDocument
        {
            Content = new TextElement("ABC", Style()),
        };

        using var buffer = new MemoryStream();
        var result = document.Generate(buffer, ClassicUncompressed);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
        Assert.StartsWith("%PDF-1.7", System.Text.Encoding.ASCII.GetString(buffer.ToArray()[..8]));
    }

    [Fact]
    public void Generate_PaginatesLongContent()
    {
        // A4 content box: 841.89 - 85 = 756.89pt; line height 12pt → 63 lines/page.
        var text = string.Join('\n', Enumerable.Repeat("AB", 100));
        var document = new LayoutDocument
        {
            Content = new TextElement(text, Style()),
        };

        var result = document.Generate(Stream.Null, ClassicUncompressed);

        Assert.Equal(2, result.PageCount); // 63 + 37
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Generate_PageBreakForcesSecondPage()
    {
        var document = new LayoutDocument
        {
            Content = new ColumnElement(
            [
                new TextElement("AB", Style()),
                new PageBreakElement(),
                new TextElement("BA", Style()),
            ]),
        };

        var result = document.Generate(Stream.Null, ClassicUncompressed);

        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void Generate_OverflowClipsAndReportsDiagnostic()
    {
        var document = new LayoutDocument
        {
            Content = new ColumnElement([new FixedElement(100, 5000)]),
        };

        var result = document.Generate(Stream.Null, ClassicUncompressed);

        Assert.Equal(1, result.PageCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("clipped", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ThrowPolicySurfacesLayoutException()
    {
        var document = new LayoutDocument
        {
            Content = new ColumnElement([new FixedElement(100, 5000)]),
            OverflowBehavior = OverflowBehavior.Throw,
        };

        Assert.Throws<LayoutException>(() => document.Generate(Stream.Null, ClassicUncompressed));
    }

    [Fact]
    public void Generate_StalledLayoutTruncatesWithDiagnostic_NeverHangs()
    {
        // Padding wider than the page: Overflowing at the root — but a root that always reports
        // Empty is the stall case. Simulate with an element that never fits and never overflows.
        var document = new LayoutDocument
        {
            Content = new AlwaysEmptyElement(),
        };

        var result = document.Generate(Stream.Null, ClassicUncompressed);

        Assert.Equal(1, result.PageCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("stalled", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_500Pages_StreamsWithFlatMemory()
    {
        var lines = string.Join('\n', Enumerable.Repeat("AB AB AB", 63 * 500));
        var document = new LayoutDocument
        {
            Content = new TextElement(lines, Style()),
        };

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = document.Generate(Stream.Null, new PdfWriterOptions { CompressStreams = true });
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(result.PageCount >= 500, $"Expected ≥500 pages, got {result.PageCount}.");
        Assert.Empty(result.Diagnostics);
        // Budget assertion: catches O(document) buffering regressions without being runtime-fragile.
        Assert.True(allocated < 600_000_000, $"Allocated {allocated / 1_000_000}MB for {result.PageCount} pages.");
    }

    [Fact]
    public void LayoutSample_MatchesGoldenBytes()
    {
        using var buffer = new MemoryStream();
        var result = LayoutSample.Build().Generate(buffer, ClassicUncompressed);

        Assert.Equal(2, result.PageCount);
        Assert.Empty(result.Diagnostics);
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "layout-sample.pdf"));
        Assert.Equal(expected, buffer.ToArray());
    }

    private sealed class AlwaysEmptyElement : Element
    {
        public override MeasureResult Measure(in LayoutConstraints constraints) => MeasureResult.Empty;

        public override void Draw(DrawingContext context, in LayoutRect bounds)
        {
        }
    }
}
