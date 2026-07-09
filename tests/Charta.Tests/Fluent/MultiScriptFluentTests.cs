using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class MultiScriptFluentTests
{
    static MultiScriptFluentTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string? WindowsFontPath(string fileName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", fileName);
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void ArabicText_GeneratesWithLoudDiagnostic_NeverSilently()
    {
        var arial = WindowsFontPath("arial.ttf");
        if (arial is null)
        {
            return;
        }

        FontManager.RegisterFontFile(arial);
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Text("مرحبا بالعالم").FontFamily("Arial")));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(1, result.PageCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("shaping", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("bidi", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HebrewText_AlsoTriggersTheDiagnostic()
    {
        var arial = WindowsFontPath("arial.ttf");
        if (arial is null)
        {
            return;
        }

        FontManager.RegisterFontFile(arial);
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Text("שלום עולם").FontFamily("Arial")));

        var result = document.GeneratePdf(Stream.Null);

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void TurkishAndCyrillic_GenerateCleanly_NoDiagnostics()
    {
        var arial = WindowsFontPath("arial.ttf");
        if (arial is null)
        {
            return;
        }

        FontManager.RegisterFontFile(arial);
        var document = Document.Create(doc => doc.Page(page => page.Content().Column(col =>
        {
            col.Item().Text("İstanbul'da şoförlük: ğüşöçı ĞÜŞÖÇİ").FontFamily("Arial");
            col.Item().Text("Съешь ещё этих мягких французских булок, да выпей же чаю").FontFamily("Arial");
            col.Item().Text("Γειά σου Κόσμε — ελληνικό αλφάβητο").FontFamily("Arial");
        })));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void MixedLatinAndCjk_FallbackChainSplitsRuns()
    {
        var arial = WindowsFontPath("arial.ttf");
        var gothic = WindowsFontPath("msgothic.ttc");
        if (arial is null || gothic is null)
        {
            return;
        }

        var latin = Charta.Fonts.PdfFont.Parse(File.ReadAllBytes(arial));
        var cjk = Charta.Fonts.PdfFont.Parse(File.ReadAllBytes(gothic));
        var chain = new Charta.Fonts.FontChain(latin, cjk);

        var runs = chain.Shape("Hello 日本語 World");

        Assert.Equal(3, runs.Count);
        Assert.Same(latin, runs[0].Font);
        Assert.Same(cjk, runs[1].Font);
        Assert.Same(latin, runs[2].Font);
        Assert.All(runs[1].Text.Glyphs.Where(g => g.GlyphId == 0), _ => Assert.Fail("CJK run has unmapped glyphs."));
    }
}
