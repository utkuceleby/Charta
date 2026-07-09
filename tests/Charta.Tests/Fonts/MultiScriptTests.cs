using System.Text;
using Charta.Cos;
using Charta.Fonts;
using Charta.Text;
using Xunit;

namespace Charta.Tests.Fonts;

/// <summary>
/// Multi-script coverage with real system fonts (Windows-conditional, no-op elsewhere).
/// Turkish, Cyrillic, and Greek must ship correctly today; Arabic/Hebrew must degrade loudly
/// (diagnostic) rather than silently, until the shaping add-on lands.
/// </summary>
public class MultiScriptTests
{
    private static byte[]? LoadWindowsFont(string fileName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    [Theory]
    [InlineData("İstanbul'da şoförlük: ğüşöçı ĞÜŞÖÇİ")]                  // Turkish
    [InlineData("Съешь ещё этих мягких французских булок")]              // Cyrillic (Russian pangram)
    [InlineData("Γειά σου Κόσμε — αλφάβητο")]                            // Greek
    [InlineData("Zażółć gęślą jaźń")]                                    // Polish
    [InlineData("Đường Việt Nam ơi")]                                    // Vietnamese (precomposed)
    public void SimpleScripts_EveryGlyphMapped_AndWidthPositive(string text)
    {
        var data = LoadWindowsFont("arial.ttf");
        if (data is null)
        {
            return;
        }

        var font = PdfFont.Parse(data);
        var shaped = font.Shape(text);

        var unmapped = new List<string>();
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (shaped.Glyphs[index].GlyphId == 0)
            {
                unmapped.Add(rune.ToString());
            }

            index++;
        }

        Assert.True(unmapped.Count == 0, $"Unmapped codepoints in Arial: {string.Join(", ", unmapped)}");
        Assert.True(shaped.Width > 0);
    }

    [Theory]
    [InlineData("İstanbul'da şoförlük ĞÜŞÖÇİ", "arial.ttf")]
    [InlineData("Съешь ещё этих мягких булок", "arial.ttf")]
    [InlineData("Γειά σου Κόσμε", "arial.ttf")]
    public void SimpleScripts_SurviveFullPipeline_WithReadableToUnicode(string text, string fontFile)
    {
        var data = LoadWindowsFont(fontFile);
        if (data is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        FontSampleDocumentProxy.Write(buffer, data, text);
        var pdf = Encoding.ASCII.GetString(buffer.ToArray());

        // Every distinct non-space character must appear as a bfchar target in the ToUnicode CMap.
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == ' ' || rune.Value == '\'')
            {
                continue;
            }

            var utf16 = ((ushort)rune.Value).ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains($"<{utf16}>", pdf, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TurkishDottedAndDotlessI_MapToDistinctGlyphs()
    {
        var data = LoadWindowsFont("arial.ttf");
        if (data is null)
        {
            return;
        }

        var font = SfntFont.Parse(data);
        char[] variants = ['I', 'ı', 'İ', 'i'];
        var gids = variants.Select(c => font.MapCodepoint(c)).ToArray();

        Assert.All(gids, gid => Assert.NotEqual(0, gid));
        Assert.Equal(4, gids.Distinct().Count()); // I/ı/İ/i are four different glyphs
    }

    [Fact]
    public void Cjk_TtcFontLoads_AndIdeographsWrapAnywhere()
    {
        var data = LoadWindowsFont("msgothic.ttc");
        if (data is null)
        {
            return;
        }

        var font = SfntFont.Parse(data); // first face of the collection
        Assert.NotEqual(0, font.MapCodepoint('日'));
        Assert.NotEqual(0, font.MapCodepoint('本'));

        // UAX#14: ideographs (class ID) allow breaks between each other — no spaces needed.
        var breaks = LineBreaker.FindBreaks("日本語のテキスト");
        Assert.True(breaks.Count >= 6, $"Expected break opportunities between ideographs, got {breaks.Count}.");
    }

    [Fact]
    public void ComplexScripts_AreDetected()
    {
        Assert.True(ScriptSupport.ContainsComplexScript("مرحبا بالعالم"));   // Arabic: joining pending
        Assert.True(ScriptSupport.ContainsComplexScript("नमस्ते दुनिया"));       // Devanagari
        Assert.False(ScriptSupport.ContainsComplexScript("שלום עולם"));       // Hebrew: bidi suffices
        Assert.False(ScriptSupport.ContainsComplexScript("İstanbul ğüşöç"));
        Assert.False(ScriptSupport.ContainsComplexScript("Привет мир"));
        Assert.False(ScriptSupport.ContainsComplexScript("Γειά σου"));
        Assert.False(ScriptSupport.ContainsComplexScript("日本語"));
    }

    /// <summary>Bridges to the internal sample-document builder living in the smoke project.</summary>
    private static class FontSampleDocumentProxy
    {
        public static void Write(Stream output, byte[] fontData, string text) =>
            Charta.Smoke.FontSampleDocument.Write(output, fontData, text, new PdfWriterOptions
            {
                XrefMode = XrefMode.Classic,
                CompressStreams = false,
            });
    }
}
