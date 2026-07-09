using System.Text;
using Charta.Cos;
using Charta.Fonts;
using Charta.Layout;
using Charta.Layout.Elements;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Layout;

public class TextPolishTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static TextPolishTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string Generate(Document document, bool debug = false)
    {
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, debugOverflow: debug);
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    [Fact]
    public void LetterSpacing_EmitsTcAndWidensLine()
    {
        var narrow = new TextElement("CAC", new TextStyle { Fonts = new FontChain(PdfFont.Parse(SyntheticFont.Build())), FontSize = 10 });
        var spaced = new TextElement("CAC", new TextStyle { Fonts = new FontChain(PdfFont.Parse(SyntheticFont.Build())), FontSize = 10, LetterSpacing = 3 });

        var narrowWidth = narrow.Measure(LayoutTestHelpers.Constraints(500, 100)).Size.Width;
        var spacedWidth = spaced.Measure(LayoutTestHelpers.Constraints(500, 100)).Size.Width;

        Assert.Equal(narrowWidth + 3 * 3, spacedWidth, 2); // 3 glyphs × 3pt tracking

        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAC").FontSize(10).LetterSpacing(3)));
        var pdf = Generate(document);
        Assert.Contains("3 Tc", pdf, StringComparison.Ordinal);
        Assert.Contains("0 Tc", pdf, StringComparison.Ordinal); // reset after the run
    }

    [Fact]
    public void Superscript_RaisesBaselineAndShrinks()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text(t =>
        {
            t.Span("E=mc").FontSize(12);
            t.Span("2").FontSize(12).Superscript();
        })));

        var pdf = Generate(document);

        // The superscript span renders at 0.65 × 12 = 7.8pt.
        Assert.Contains("/F1 7.8 Tf", pdf, StringComparison.Ordinal);
        Assert.Contains("/F1 12 Tf", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Subscript_LowersBaseline()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text(t =>
        {
            t.Span("H").FontSize(12);
            t.Span("2").FontSize(12).Subscript();
            t.Span("O").FontSize(12);
        })));

        var pdf = Generate(document);
        Assert.Contains("/F1 7.8 Tf", pdf, StringComparison.Ordinal); // shrunk subscript
        Assert.Contains("/F1 12 Tf", pdf, StringComparison.Ordinal);  // full-size H and O
    }

    [Fact]
    public void DebugLayout_DrawsRedOverflowMarker()
    {
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Column(col => col.Item().Height(5000).Background(Color.FromHex(0x00FF00)).Text("AB"))));

        var normal = Generate(document, debug: false);
        var debug = Generate(document, debug: true);

        // The red marker (stroke color 0.85 0.1 0.1) appears only with debugging on.
        Assert.DoesNotContain("0.85 0.1 0.1 RG", normal, StringComparison.Ordinal);
        Assert.Contains("0.85 0.1 0.1 RG", debug, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugLayout_OffByDefault_ByteIdentical()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("AB")));

        // A well-fitting document is unaffected by the debug flag.
        Assert.Equal(Generate(document, debug: false), Generate(document, debug: true));
    }
}
