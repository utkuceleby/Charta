using System.Text;
using Charta.Cos;
using Charta.Imaging;
using Xunit;

namespace Charta.Tests.Imaging;

public class PdfImageTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    private static string WriteImageObject(PdfImage image)
    {
        using var buffer = new MemoryStream();
        using (var writer = new PdfWriter(buffer, ClassicUncompressed))
        {
            writer.WriteHeader();
            var root = writer.Allocate();
            var imageRef = writer.Allocate();
            image.Write(writer, imageRef);
            writer.WriteObject(root, new CosDictionary { [CosNames.Type] = CosNames.Catalog });
            writer.WriteTrailer(root);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    [Fact]
    public void RgbaPng_EmitsImageXObjectWithSMask()
    {
        byte[] rgba =
        [
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 0, 255, 255, 255, 64,
        ];
        var image = PdfImage.FromBytes(PngFixtures.Build(2, 2, 8, colorType: 6, rgba));

        var text = WriteImageObject(image);

        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.Contains("/Width 2", text, StringComparison.Ordinal);
        Assert.Contains("/Height 2", text, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace /DeviceRGB", text, StringComparison.Ordinal);
        Assert.Contains("/SMask", text, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace /DeviceGray", text, StringComparison.Ordinal); // the SMask itself
    }

    [Fact]
    public void IndexedPng_EmitsIndexedColorSpace()
    {
        byte[] indices = [0x01, 0x20];
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        var image = PdfImage.FromBytes(PngFixtures.Build(3, 1, 4, colorType: 3, indices, palette: palette));

        var text = WriteImageObject(image);

        Assert.Contains("/ColorSpace [/Indexed /DeviceRGB 2", text, StringComparison.Ordinal);
        Assert.Contains("/BitsPerComponent 4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Jpeg_PassesThroughWithDctDecode()
    {
        var jpegBytes = PngFixtures.BuildJpegHeader(320, 240);
        var image = PdfImage.FromBytes(jpegBytes);

        var text = WriteImageObject(image);

        Assert.Contains("/Filter /DCTDecode", text, StringComparison.Ordinal);
        Assert.Contains("/Width 320", text, StringComparison.Ordinal);
        Assert.Contains($"/Length {jpegBytes.Length}", text, StringComparison.Ordinal); // untouched bytes
    }

    [Fact]
    public void FromBytes_RejectsUnknownFormats()
    {
        Assert.Throws<ImageFormatException>(() => PdfImage.FromBytes(new byte[] { 0x42, 0x4D, 1, 2, 3 })); // BMP
    }
}
