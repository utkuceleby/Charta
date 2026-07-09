using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class DocumentApiTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static DocumentApiTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static byte[] Generate(Document document)
    {
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        return buffer.ToArray();
    }

    [Fact]
    public void QuickStart_ProducesValidPdf()
    {
        var document = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimeter);
                page.Header().Text("CAB").FontSize(20);
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text("AB AB AB");
                    col.Item().LineHorizontal(1);
                    col.Item().Background(Color.FromHex(0xE0E0E0)).Padding(6).Text("BAC");
                });
                page.Footer().AlignCenter().Text("A");
            });
        });

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
        Assert.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(buffer.ToArray()[..8]));
    }

    [Fact]
    public void DefaultFont_UsesRegisteredFontFirst()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("ABC")));

        var text = Encoding.ASCII.GetString(Generate(document));

        Assert.Contains("+ChartaTest", text, StringComparison.Ordinal); // subset of the registered synthetic font
    }

    [Fact]
    public void PageBreak_SplitsIntoTwoPages()
    {
        var document = Document.Create(doc =>
        {
            doc.Page(page => page.Content().Column(col =>
            {
                col.Item().Text("AB");
                col.Item().PageBreak();
                col.Item().Text("BA");
            }));
        });

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void MultiplePageRuns_ProduceSequentialSections()
    {
        var document = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Content().Text("AB");
            });
            doc.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Content().Text("BA");
            });
        });

        var text = Encoding.ASCII.GetString(Generate(document));

        Assert.Contains("/MediaBox [0 0 595.276 841.89]", text, StringComparison.Ordinal);
        Assert.Contains("/MediaBox [0 0 419.53 595.276]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Container_RejectsSecondContent()
    {
        var document = Document.Create(doc => doc.Page(page =>
        {
            var content = page.Content();
            content.Text("AB");
            content.Text("BA"); // second fill of the same slot
        }));

        var ex = Assert.Throws<InvalidOperationException>(() => document.GeneratePdf(Stream.Null));
        Assert.Contains("Column or Row", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_WithoutPages_Throws()
    {
        var document = Document.Create(_ => { });

        Assert.Throws<InvalidOperationException>(() => document.GeneratePdf(Stream.Null));
    }

    [Fact]
    public void UnknownFontFamily_ThrowsWithGuidance()
    {
        var document = Document.Create(doc =>
            doc.Page(page => page.Content().Text("AB").FontFamily("No Such Family 123")));

        var ex = Assert.Throws<InvalidOperationException>(() => document.GeneratePdf(Stream.Null));
        Assert.Contains("RegisterFont", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Component_ComposesIntoContainer()
    {
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Column(col =>
            {
                col.Item().Component(new BadgeComponent("AB"));
                col.Item().Component(new BadgeComponent("BA"));
            })));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void RepeatedImage_EmbedsSingleXObject()
    {
        byte[] rgb = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        var png = PngFixtures.Build(2, 2, 8, colorType: 2, rgb);
        var document = Document.Create(doc => doc.Page(page => page.Content().Column(col =>
        {
            col.Item().MaxWidth(50).Image(png);
            col.Item().MaxWidth(50).Image(png);
        })));

        var text = Encoding.ASCII.GetString(Generate(document));

        Assert.Equal(1, CountOccurrences(text, "/Subtype /Image"));
        Assert.Equal(2, CountOccurrences(text, "/Im1 Do"));
    }

    [Fact]
    public void Document_IsRegeneratable_AndDeterministic()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAB AB")));

        Assert.Equal(Generate(document), Generate(document));
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

    private sealed class BadgeComponent(string label) : IComponent
    {
        public void Compose(IContainer container) =>
            container.Border(1).Padding(4).Text(label).FontSize(10);
    }

    [Fact]
    public void FluentSample_MatchesGoldenBytes()
    {
        using var buffer = new MemoryStream();
        var result = FluentSample.Build().Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);

        Assert.Equal(2, result.PageCount);
        Assert.Empty(result.Diagnostics);
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "fluent-sample.pdf"));
        Assert.Equal(expected, buffer.ToArray());
    }
}
