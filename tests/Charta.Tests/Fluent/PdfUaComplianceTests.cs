using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class PdfUaComplianceTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static PdfUaComplianceTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string GenerateUa(Action<IDocumentDescriptor> describe)
    {
        var document = Document.Create(describe);
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, conformance: PdfConformance.PdfUA1, language: "en-US");
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static string GenerateSample() => GenerateUa(doc =>
    {
        doc.Metadata(m => m.Title("Accessible"));
        doc.Page(page => page.Content().Column(col =>
        {
            col.Item().Text("ABC").Heading(1);
            col.Item().Text("CAB");
            col.Item().Canvas(20, 20, c => c.Rectangle(0, 0, 20, 20).Fill(Color.Black), altText: "A square.");
        }));
    });

    [Fact]
    public void Ua_WritesMarkedStructTreeAndViewerPreferences()
    {
        var pdf = GenerateSample();

        Assert.Contains("/MarkInfo", pdf, StringComparison.Ordinal);
        Assert.Contains("/Marked true", pdf, StringComparison.Ordinal);
        Assert.Contains("/StructTreeRoot", pdf, StringComparison.Ordinal);
        Assert.Contains("/Type /StructTreeRoot", pdf, StringComparison.Ordinal);
        Assert.Contains("/ViewerPreferences", pdf, StringComparison.Ordinal);
        Assert.Contains("/DisplayDocTitle true", pdf, StringComparison.Ordinal);
        Assert.Contains("/Lang (en-US)", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Ua_TagsHeadingAndParagraphAndFigure()
    {
        var pdf = GenerateSample();

        Assert.Contains("/S /H1", pdf, StringComparison.Ordinal);
        Assert.Contains("/S /P", pdf, StringComparison.Ordinal);
        Assert.Contains("/S /Figure", pdf, StringComparison.Ordinal);
        Assert.Contains("/Alt (A square.)", pdf, StringComparison.Ordinal);
        // Marked content: tagged draws and the /ParentTree number tree.
        Assert.Contains("BDC", pdf, StringComparison.Ordinal);
        Assert.Contains("EMC", pdf, StringComparison.Ordinal);
        Assert.Contains("/ParentTree", pdf, StringComparison.Ordinal);
        Assert.Contains("/StructParents 0", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Ua_WritesPdfuaidMetadata()
    {
        var pdf = GenerateSample();

        Assert.Contains("xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\"", pdf, StringComparison.Ordinal);
        Assert.Contains("<pdfuaid:part>1</pdfuaid:part>", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Ua_RequiresTitle()
    {
        // PDF/UA mandates a document title (shown via DisplayDocTitle); generation must reject its absence.
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAB")));
        using var buffer = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() =>
            document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, conformance: PdfConformance.PdfUA1));
    }

    [Fact]
    public void NonUa_HasNoStructTreeOrPdfuaid()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAB")));
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        var pdf = Encoding.ASCII.GetString(buffer.ToArray());

        Assert.DoesNotContain("/StructTreeRoot", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("pdfuaid", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("/MarkInfo", pdf, StringComparison.Ordinal);
    }
}
