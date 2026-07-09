using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Layout;

public class TableTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static TableTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static (GenerationResult Result, string Pdf) Generate(Document document)
    {
        using var buffer = new MemoryStream();
        var result = document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        return (result, Encoding.ASCII.GetString(buffer.ToArray()));
    }

    [Fact]
    public void Table_BasicGrid_SinglePage()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(100);
                cols.RelativeColumn();
            });
            table.Cell().Text("AB");
            table.Cell().Text("BA");
            table.Cell().Text("CC");
            table.Cell().Text("AA");
        })));

        var (result, _) = Generate(document);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Table_WithoutColumns_ThrowsWithGuidance()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.Cell().Text("AB");
        })));

        var ex = Assert.Throws<InvalidOperationException>(() => document.GeneratePdf(Stream.Null));
        Assert.Contains("ColumnsDefinition", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_LongBody_PaginatesRowByRow_AndRepeatsHeader()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn();
                cols.RelativeColumn();
            });
            table.Header(header =>
            {
                header.Cell().Background(Color.FromHex(0xDDDDDD)).Padding(4).Text("CA").FontSize(10);
                header.Cell().Background(Color.FromHex(0xDDDDDD)).Padding(4).Text("CB").FontSize(10);
            });
            for (var i = 0; i < 120; i++)
            {
                table.Cell().Padding(4).Text("AB").FontSize(10);
                table.Cell().Padding(4).Text("BA").FontSize(10);
            }
        })));

        var (result, pdf) = Generate(document);

        Assert.True(result.PageCount >= 2, $"Expected pagination, got {result.PageCount} page(s).");
        Assert.Empty(result.Diagnostics);
        // Header text "CA" = gids <00030001>; must appear once per page.
        Assert.Equal(result.PageCount, CountOccurrences(pdf, "<00030001>"));
    }

    [Fact]
    public void Table_RowSpanBand_StaysTogetherAcrossPageBreak()
    {
        // 30 filler rows, then a 3-row band with a rowspan cell near the page boundary.
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(120);
                cols.RelativeColumn();
            });
            for (var i = 0; i < 30; i++)
            {
                table.Cell().Padding(8).Text("AB");
                table.Cell().Padding(8).Text("BA");
            }

            table.Cell().RowSpan(3).Background(Color.FromHex(0xEEEEFF)).Padding(4).Text("CC");
            for (var i = 0; i < 3; i++)
            {
                table.Cell().Padding(8).Text("AC");
            }
        })));

        var (result, pdf) = Generate(document);

        Assert.Empty(result.Diagnostics); // the band moved to a fresh page instead of clipping
        Assert.True(result.PageCount >= 2);

        // The rowspan cell background and all three sibling rows must be on the same page:
        // find the content stream containing the band color and count the sibling glyphs there.
        var pages = pdf.Split("stream\n");
        var bandPage = pages.Single(p => p.Contains("0.933333 0.933333 1 rg", StringComparison.Ordinal));
        Assert.Equal(3, CountOccurrences(bandPage, "<00010003>")); // the three sibling "AC" cells
    }

    [Fact]
    public void Table_ColumnSpan_CoversFullWidth()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(100);
                cols.ConstantColumn(100);
            });
            table.Cell().ColumnSpan(2).Background(Color.FromHex(0xFF0000)).Text("AB");
            table.Cell().Text("BA");
            table.Cell().Text("CC");
        })));

        var (result, pdf) = Generate(document);

        Assert.Empty(result.Diagnostics);
        // The spanning cell's background spans both columns: width 200.
        Assert.Contains("1 0 0 rg", pdf, StringComparison.Ordinal);
        Assert.Matches(@"1 0 0 rg\n[\d.]+ [\d.]+ 200 ", pdf);
    }

    [Fact]
    public void Table_BandTallerThanPage_ClipsAndContinues()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.ColumnsDefinition(cols => cols.RelativeColumn());
            table.Cell().Text("AB");
            table.Cell().Height(2000).Background(Color.FromHex(0x00FF00)).Text("BA"); // taller than any page
            table.Cell().Text("CC");
        })));

        var (result, _) = Generate(document);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("clipped", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(result.PageCount >= 2, "The row after the clipped band must still render.");
    }

    [Fact]
    public void Table_AutoPlacement_FlowsAroundSpans()
    {
        // Verified indirectly: build a table whose placement, if wrong, would stack text in one cell.
        var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(80);
                cols.ConstantColumn(80);
                cols.ConstantColumn(80);
            });
            table.Cell().RowSpan(2).Text("AB");   // (0,0)-(1,0)
            table.Cell().Text("BA");              // (0,1)
            table.Cell().Text("CC");              // (0,2)
            table.Cell().ColumnSpan(2).Text("AC");// (1,1)-(1,2), flowed around the rowspan
            table.Cell().Text("BB");              // (2,0)
        })));

        var (result, _) = Generate(document);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void Table_RandomSpans_AlwaysTerminate_NoLostCells()
    {
        // Property-style torture: random tables must generate without hanging and without
        // dropping cells (every cell's text must appear in the output).
        for (var seed = 0; seed < 25; seed++)
        {
            var random = new Random(seed);
            var columnCount = random.Next(1, 5);
            var cellCount = random.Next(1, 40);

            var document = Document.Create(doc => doc.Page(page => page.Content().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    for (var c = 0; c < columnCount; c++)
                    {
                        cols.RelativeColumn();
                    }
                });
                for (var i = 0; i < cellCount; i++)
                {
                    var cell = table.Cell();
                    if (random.Next(4) == 0)
                    {
                        cell = cell.RowSpan(random.Next(2, 4));
                    }

                    if (random.Next(4) == 0)
                    {
                        cell = cell.ColumnSpan(random.Next(2, columnCount + 2));
                    }

                    cell.Padding(2).Text("CA").FontSize(8); // C→A has no kern pair: plain Tj hex
                }
            })));

            var (result, pdf) = Generate(document);

            Assert.True(result.PageCount >= 1);
            Assert.Equal(cellCount, CountOccurrences(pdf, "<00030001>")); // every "CA" cell rendered once
        }
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
}
