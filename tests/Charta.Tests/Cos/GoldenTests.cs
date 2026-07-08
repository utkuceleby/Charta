using Charta.Cos;
using Xunit;

namespace Charta.Tests.Cos;

/// <summary>
/// Byte-level golden tests: any writer change that alters output shows up as a reviewable file diff.
/// Golden files are uncompressed so the diff is human-readable. To regenerate after an intentional
/// change, run the Charta.Smoke project with the target directory as its argument.
/// </summary>
public class GoldenTests
{
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
