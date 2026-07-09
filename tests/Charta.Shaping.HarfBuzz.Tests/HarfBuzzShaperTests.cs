using System.Text;
using Charta;
using Charta.Fonts;
using Charta.Shaping.HarfBuzz;
using Xunit;

namespace Charta.Shaping.HarfBuzz.Tests;

/// <summary>
/// HarfBuzz shaping tests. Glyph-level assertions run against a system Arabic-capable font
/// (Windows-conditional); the loads-and-shapes test runs everywhere the native binary is present
/// (including Linux CI). These use the shapers directly, not the global registry, so they never
/// disturb other test assemblies.
/// </summary>
public class HarfBuzzShaperTests
{
    private static byte[]? ArabicFont()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static SfntFont Parse(byte[] data) => SfntFont.Parse(data);

    [Fact]
    public void HarfBuzz_LoadsAndShapesLatin_Everywhere()
    {
        var data = ArabicFont() ?? (OperatingSystem.IsLinux() && File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
            ? File.ReadAllBytes("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
            : null);
        if (data is null)
        {
            return;
        }

        var shaper = new HarfBuzzShaper();
        var glyphs = shaper.Shape(Parse(data), "Hello", ShaperDirection.LeftToRight);

        Assert.Equal(5, glyphs.Count);
        Assert.All(glyphs, g => Assert.NotEqual(0, g.GlyphId));
    }

    [Fact]
    public void Arabic_JoiningProducesContextualForms()
    {
        var data = ArabicFont();
        if (data is null)
        {
            return;
        }

        var font = Parse(data);
        var simple = new SimpleTextShaper();
        var harfbuzz = new HarfBuzzShaper();

        // Three connected beh letters. The simple shaper gives three identical isolated glyphs;
        // HarfBuzz gives initial, medial, and final forms — three DIFFERENT glyph ids.
        const string beh = "ببب";
        var simpleGlyphs = simple.Shape(font, beh, ShaperDirection.RightToLeft);
        var hbGlyphs = harfbuzz.Shape(font, beh, ShaperDirection.RightToLeft);

        Assert.Equal(3, simpleGlyphs.Count);
        Assert.Single(simpleGlyphs.Select(g => g.GlyphId).Distinct()); // simple: all the same isolated form

        Assert.Equal(3, hbGlyphs.Count);
        Assert.True(
            hbGlyphs.Select(g => g.GlyphId).Distinct().Count() >= 2,
            "HarfBuzz should produce distinct contextual (initial/medial/final) forms.");
    }

    [Fact]
    public void Arabic_LamAlef_FormsLigature()
    {
        var data = ArabicFont();
        if (data is null)
        {
            return;
        }

        var font = Parse(data);
        var harfbuzz = new HarfBuzzShaper();

        // Lam + Alef is a mandatory ligature in Arabic: two input codepoints → one glyph.
        var glyphs = harfbuzz.Shape(font, "لا", ShaperDirection.RightToLeft);

        Assert.Single(glyphs);
        Assert.Equal(0, glyphs[0].ClusterStart); // the ligature maps back to the whole source
        Assert.Equal(2, glyphs[0].ClusterLength);
    }

    [Fact]
    public void Register_SuppressesDiagnostic_AndGeneratesArabic()
    {
        var data = ArabicFont();
        if (data is null)
        {
            return;
        }

        ChartaHarfBuzz.Register();
        FontManager.RegisterFont(data);

        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Text("مرحبا بالعالم").FontFamily("Arial")));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics); // no "needs shaping" diagnostic once HarfBuzz is active
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(buffer.ToArray(), 0, 4));
    }
}
