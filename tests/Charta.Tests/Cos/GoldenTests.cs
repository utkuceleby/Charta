using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Cos;

/// <summary>
/// Byte-level golden tests: any writer change that alters output shows up as a reviewable file diff.
/// Golden files are uncompressed so the diff is human-readable. To regenerate after an intentional
/// change, run the Charta.Smoke project with the target directory as its argument.
/// </summary>
public class GoldenTests
{
    [Fact]
    public void ImageSample_MatchesGoldenBytes()
    {
        byte[] rgbaPixels =
        [
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 255, 255, 255, 255, 64,
        ];
        var png = PngFixtures.Build(2, 2, 8, colorType: 6, rgbaPixels, filterType: 4);
        using var buffer = new MemoryStream();
        ImageSampleDocument.Write(buffer, png, new PdfWriterOptions
        {
            XrefMode = XrefMode.Classic,
            CompressStreams = false,
        });

        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "image-sample.pdf"));

        Assert.Equal(expected, buffer.ToArray());
    }

    [Theory]
    [InlineData(true, "hello-classic.pdf")]
    [InlineData(false, "hello-xrefstream.pdf")]
    public void HelloPdf_MatchesGoldenBytes(bool useClassicXref, string goldenFile)
    {
        using var buffer = new MemoryStream();
        HelloPdf.Write(buffer, new PdfWriterOptions
        {
            XrefMode = useClassicXref ? XrefMode.Classic : XrefMode.Stream,
            CompressStreams = false,
        });

        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", goldenFile));

        Assert.Equal(expected, buffer.ToArray());
    }
}
