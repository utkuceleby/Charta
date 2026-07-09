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

        // Thread-local measurement: immune to other test classes allocating in parallel.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = document.Generate(Stream.Null, new PdfWriterOptions { CompressStreams = true });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result.PageCount >= 500, $"Expected ≥500 pages, got {result.PageCount}.");
        Assert.Empty(result.Diagnostics);
        // Budget assertion: catches O(document) buffering regressions, which jump by gigabytes.
        // Current allocation churn baseline is ~850MB (UAX#14 shaping); reducing it is benchmark work.
        Assert.True(allocated < 1_200_000_000, $"Allocated {allocated / 1_000_000}MB for {result.PageCount} pages.");
    }

    [Fact]
    public void Generate_RepeatsHeaderAndFooterOnEveryPage()
    {
        var document = new LayoutDocument
        {
            Header = _ => new TextElement("AA", Style()),
            Footer = _ => new TextElement("BB", Style()),
            Content = new ColumnElement(
            [
                new TextElement("CC", Style()),
                new PageBreakElement(),
                new TextElement("CC", Style()),
            ]),
        };

        using var buffer = new MemoryStream();
        var result = document.Generate(buffer, ClassicUncompressed);
        var text = System.Text.Encoding.ASCII.GetString(buffer.ToArray());

        Assert.Equal(2, result.PageCount);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, CountOccurrences(text, "<00010001> Tj")); // header "AA" on both pages
        Assert.Equal(2, CountOccurrences(text, "<00020002> Tj")); // footer "BB" on both pages
    }

    [Fact]
    public void Generate_OversizedHeaderClipsWithDiagnostic()
    {
        var document = new LayoutDocument
        {
            Header = _ => new ColumnElement([new FixedElement(10, 5000)]),
            Content = new TextElement("AB", Style()),
        };

        var result = document.Generate(Stream.Null, ClassicUncompressed);

        Assert.Equal(1, result.PageCount);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Header", StringComparison.Ordinal));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
