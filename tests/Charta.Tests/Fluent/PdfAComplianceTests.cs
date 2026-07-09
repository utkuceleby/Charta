using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class PdfAComplianceTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static PdfAComplianceTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string GeneratePdfA()
    {
        var document = Document.Create(doc =>
        {
            doc.Metadata(m => m.Title("Compliant").Author("Charta"));
            doc.Page(page => page.Content().Text("CAB"));
        });

        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, conformance: PdfConformance.PdfA2b);
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    [Fact]
    public void PdfA_WritesOutputIntentWithIccProfile()
    {
        var pdf = GeneratePdfA();

        Assert.Contains("/OutputIntents [", pdf, StringComparison.Ordinal);
        Assert.Contains("/S /GTS_PDFA1", pdf, StringComparison.Ordinal);
        Assert.Contains("/OutputConditionIdentifier (sRGB)", pdf, StringComparison.Ordinal);
        Assert.Contains("/DestOutputProfile", pdf, StringComparison.Ordinal);
        Assert.Contains("/N 3", pdf, StringComparison.Ordinal); // ICC has 3 components
    }

    [Fact]
    public void PdfA_WritesPdfaidMetadata()
    {
        var pdf = GeneratePdfA();

        Assert.Contains("<pdfaid:part>2</pdfaid:part>", pdf, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", pdf, StringComparison.Ordinal);
        Assert.Contains("/Type /Metadata", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfA_EmitsMetadataEvenWithoutDocumentInfo()
    {
        // PDF/A requires XMP; it is written even when the document set no metadata.
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAB")));
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, conformance: PdfConformance.PdfA2b);
        var pdf = Encoding.ASCII.GetString(buffer.ToArray());

        Assert.Contains("/OutputIntents", pdf, StringComparison.Ordinal);
        Assert.Contains("pdfaid:part", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPdfA_HasNoOutputIntentOrPdfaid()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAB")));
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        var pdf = Encoding.ASCII.GetString(buffer.ToArray());

        Assert.DoesNotContain("/OutputIntents", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("pdfaid", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void IccProfile_IsWellFormed()
    {
        var icc = Charta.Compliance.SrgbIccProfile.Build();

        Assert.True(icc.Length is > 128 and < 2000);
        // 'acsp' signature at offset 36 marks a valid ICC profile.
        Assert.Equal("acsp", Encoding.ASCII.GetString(icc, 36, 4));
        Assert.Equal("RGB ", Encoding.ASCII.GetString(icc, 16, 4)); // data colour space
        // The declared profile size matches the byte length.
        var declaredSize = (icc[0] << 24) | (icc[1] << 16) | (icc[2] << 8) | icc[3];
        Assert.Equal(icc.Length, declaredSize);
    }
}
