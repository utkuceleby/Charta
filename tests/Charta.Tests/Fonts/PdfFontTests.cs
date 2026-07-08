using System.Text;
using Charta.Cos;
using Charta.Fonts;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

public class PdfFontTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    private static byte[] GenerateSample(string text)
    {
        using var buffer = new MemoryStream();
        FontSampleDocument.Write(buffer, SyntheticFont.Build(), text, ClassicUncompressed);
        return buffer.ToArray();
    }

    [Fact]
    public void Shape_MapsCodepointsAndAccumulatesWidth()
    {
        var font = PdfFont.Parse(SyntheticFont.Build());

        var shaped = font.Shape("CAB");

        Assert.Equal(new ushort[] { 3, 1, 2 }, shaped.GlyphIds);
        Assert.Equal(1800, shaped.Width); // 3 × 600 font units at unitsPerEm 1000
        Assert.Equal("<000300010002>", shaped.ToHexString());
    }

    [Fact]
    public void Shape_UnmappedCodepointBecomesNotdef()
    {
        var font = PdfFont.Parse(SyntheticFont.Build());

        var shaped = font.Shape("AZ");

        Assert.Equal(new ushort[] { 1, 0 }, shaped.GlyphIds);
    }

    [Fact]
    public void SampleDocument_ContainsCompositeFontStructure()
    {
        var text = Encoding.ASCII.GetString(GenerateSample("CAB"));

        Assert.Contains("/Subtype /Type0", text, StringComparison.Ordinal);
        Assert.Contains("/Encoding /Identity-H", text, StringComparison.Ordinal);
        Assert.Contains("/Subtype /CIDFontType2", text, StringComparison.Ordinal);
        Assert.Contains("/CIDToGIDMap /Identity", text, StringComparison.Ordinal);
        Assert.Matches(@"/BaseFont /[A-Z]{6}\+ChartaTest", text);
        Assert.Contains("/FontFile2", text, StringComparison.Ordinal);
        Assert.Contains("/Length1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleDocument_WidthsArrayCoversUsedGlyphRun()
    {
        var text = Encoding.ASCII.GetString(GenerateSample("CAB"));

        // Used gids 0..3 form one consecutive run: 0 [500 600 600 600].
        Assert.Contains("/W [0 [500 600 600 600]]", text, StringComparison.Ordinal);
        Assert.Contains("/DW 1000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleDocument_ToUnicodeMapsGlyphsBackToText()
    {
        var text = Encoding.ASCII.GetString(GenerateSample("CAB"));

        Assert.Contains("3 beginbfchar", text, StringComparison.Ordinal);
        Assert.Contains("<0001> <0041>", text, StringComparison.Ordinal); // gid 1 → 'A'
        Assert.Contains("<0002> <0042>", text, StringComparison.Ordinal);
        Assert.Contains("<0003> <0043>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleDocument_IsDeterministic()
    {
        Assert.Equal(GenerateSample("CAB"), GenerateSample("CAB"));
    }

    [Fact]
    public void SampleDocument_EmbeddedSubsetParsesAndKeepsComponents()
    {
        var bytes = GenerateSample("CA");
        var text = Encoding.ASCII.GetString(bytes);

        // Extract the embedded font program (first stream after /Length1, uncompressed output).
        var fontFileStart = text.IndexOf("/Length1", StringComparison.Ordinal);
        var streamStart = text.IndexOf("stream\n", fontFileStart, StringComparison.Ordinal) + "stream\n".Length;
        var streamEnd = text.IndexOf("\nendstream", streamStart, StringComparison.Ordinal);

        var subsetBytes = bytes.AsMemory(streamStart, streamEnd - streamStart);
        var subset = SfntFont.Parse(subsetBytes);

        Assert.Equal(4, subset.NumGlyphs);
        Assert.True(subset.GetGlyphData(1).Length > 0); // 'A' — also the component of 'C'
        Assert.Equal(0, subset.GetGlyphData(2).Length);  // 'B' unused
        Assert.True(subset.GetGlyphData(3).Length > 0); // 'C'
        Assert.Equal(0xB1B0AFBAu, SfntAssembler.Checksum(subsetBytes.Span));
    }

    [Fact]
    public void GoldenSample_MatchesCommittedBytes()
    {
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "font-sample.pdf"));

        Assert.Equal(expected, GenerateSample("CAB"));
    }
}
