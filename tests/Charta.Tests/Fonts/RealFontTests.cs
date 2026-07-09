using System.Text;
using Charta.Cos;
using Charta.Fonts;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

/// <summary>
/// Smoke tests against real system fonts. They no-op quietly on machines without the font —
/// the synthetic-font suite carries the deterministic coverage; these catch real-world table quirks.
/// </summary>
public class RealFontTests
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

    [Fact]
    public void Arial_ParsesAndMapsBasicLatin()
    {
        var data = LoadWindowsFont("arial.ttf");
        if (data is null)
        {
            return;
        }

        var font = SfntFont.Parse(data);

        Assert.True(font.NumGlyphs > 1000);
        Assert.True(font.UnitsPerEm is 1000 or 2048);
        Assert.NotEqual(0, font.MapCodepoint('A'));
        Assert.NotEqual(0, font.MapCodepoint('ğ')); // Turkish coverage
        Assert.True(font.AdvanceWidth(font.MapCodepoint('W')) > font.AdvanceWidth(font.MapCodepoint('i')));
    }

    [Fact]
    public void Arial_SubsetSurvivesRoundTrip()
    {
        var data = LoadWindowsFont("arial.ttf");
        if (data is null)
        {
            return;
        }

        var font = SfntFont.Parse(data);
        var used = "Hello Charta ÂÊÎ".EnumerateRunes()
            .Select(r => font.MapCodepoint(r.Value))
            .Where(gid => gid != 0)
            .ToList();
        var closure = GlyphClosure.Compute(font, used);

        // Accented capitals are composites in most fonts; the closure must grow beyond the input.
        Assert.True(closure.Count > used.Count + 1);

        var subsetBytes = TrueTypeSubsetter.CreateSubset(font, closure);
        var subset = SfntFont.Parse(subsetBytes);

        Assert.Equal(font.NumGlyphs, subset.NumGlyphs);
        Assert.Equal(0xB1B0AFBAu, SfntAssembler.Checksum(subsetBytes));
        Assert.True(subsetBytes.Length < data.Length / 10); // the subset must actually shrink
        foreach (var gid in closure)
        {
            // Alignment may append zero padding inside the loca range; the outline bytes must match.
            var original = font.GetGlyphData(gid);
            var roundTripped = subset.GetGlyphData(gid);
            Assert.True(roundTripped.Length >= original.Length);
            Assert.Equal(original.ToArray(), roundTripped[..original.Length].ToArray());
            Assert.All(roundTripped[original.Length..].ToArray(), b => Assert.Equal(0, b));
        }
    }

    [Fact]
    public void Arial_FullDocumentPipelineProducesValidPdf()
    {
        var data = LoadWindowsFont("arial.ttf");
        if (data is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        FontSampleDocument.Write(buffer, data, "Merhaba Charta — İĞÜŞÖÇ", new PdfWriterOptions
        {
            XrefMode = XrefMode.Classic,
            CompressStreams = false,
        });
        var text = Encoding.ASCII.GetString(buffer.ToArray());

        Assert.Contains("/Subtype /Type0", text, StringComparison.Ordinal);
        Assert.Matches(@"/BaseFont /[A-Z]{6}\+Arial", text);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }
}
