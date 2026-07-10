using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class NotdefDiagnosticTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static NotdefDiagnosticTests() => FontManager.RegisterFont(SyntheticFont.Build());

    // The synthetic font maps only A/B/C; any other letter shapes to .notdef (glyph 0).
    private static GenerationResult Generate(string text, PdfConformance conformance)
    {
        var document = Document.Create(doc =>
        {
            doc.Metadata(m => m.Title("Sample"));
            doc.Page(page => page.Content().Text(text));
        });

        using var buffer = new MemoryStream();
        return document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, conformance: conformance);
    }

    private static bool HasNotdefDiagnostic(GenerationResult result) =>
        result.Diagnostics.Any(d => d.Message.Contains(".notdef", StringComparison.Ordinal));

    [Fact]
    public void UnmappedGlyph_UnderPdfA_RaisesDiagnostic()
    {
        var result = Generate("CABX", PdfConformance.PdfA2b); // X is unmapped
        Assert.True(HasNotdefDiagnostic(result));
    }

    [Fact]
    public void UnmappedGlyph_UnderPdfUA_RaisesDiagnostic()
    {
        var result = Generate("ABZ", PdfConformance.PdfUA1); // Z is unmapped
        Assert.True(HasNotdefDiagnostic(result));
    }

    [Fact]
    public void MappedText_UnderPdfA_NoDiagnostic()
    {
        var result = Generate("CABCAB", PdfConformance.PdfA2b);
        Assert.False(HasNotdefDiagnostic(result));
    }

    [Fact]
    public void UnmappedGlyph_WithoutConformance_NoDiagnostic()
    {
        // Outside PDF/A or PDF/UA, an unmapped glyph is tofu the caller may have intended — no noise.
        var result = Generate("CABX", PdfConformance.None);
        Assert.False(HasNotdefDiagnostic(result));
    }

    [Fact]
    public void AtMostOneNotdefDiagnosticPerPage()
    {
        var result = Generate("XXXX XXXX XXXX", PdfConformance.PdfA2b);
        Assert.Equal(1, result.Diagnostics.Count(d => d.Message.Contains(".notdef", StringComparison.Ordinal)));
    }
}
