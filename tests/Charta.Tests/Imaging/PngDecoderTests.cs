using Charta.Imaging;
using Xunit;

namespace Charta.Tests.Imaging;

public class PngDecoderTests
{
    // 2x2 RGB8: red, green / blue, white.
    private static readonly byte[] Rgb2X2 =
    [
        255, 0, 0, 0, 255, 0,
        0, 0, 255, 255, 255, 255,
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Decode_RecoversPixels_ThroughEveryFilterType(byte filterType)
    {
        var png = PngFixtures.Build(2, 2, 8, colorType: 2, Rgb2X2, filterType);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(8, decoded.BitsPerComponent);
        Assert.Equal(PngColorSpace.Rgb, decoded.ColorSpace);
        Assert.Equal(Rgb2X2, decoded.PixelData);
        Assert.Null(decoded.Alpha);
    }

    [Fact]
    public void Decode_SplitsRgbaIntoColorAndSMask()
    {
        byte[] rgba =
        [
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 0, 255, 255, 255, 64,
        ];
        var png = PngFixtures.Build(2, 2, 8, colorType: 6, rgba, filterType: 4);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(Rgb2X2, decoded.PixelData);
        Assert.Equal(new byte[] { 255, 128, 0, 64 }, decoded.Alpha);
    }

    [Fact]
    public void Decode_GrayscaleAlpha_SplitsChannels()
    {
        byte[] grayAlpha = [10, 255, 20, 0];
        var png = PngFixtures.Build(2, 1, 8, colorType: 4, grayAlpha);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(PngColorSpace.Gray, decoded.ColorSpace);
        Assert.Equal(new byte[] { 10, 20 }, decoded.PixelData);
        Assert.Equal(new byte[] { 255, 0 }, decoded.Alpha);
    }

    [Fact]
    public void Decode_SixteenBit_KeepsBigEndianSamples()
    {
        byte[] gray16 = [0x12, 0x34, 0xAB, 0xCD];
        var png = PngFixtures.Build(2, 1, 16, colorType: 0, gray16);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(16, decoded.BitsPerComponent);
        Assert.Equal(gray16, decoded.PixelData);
    }

    [Fact]
    public void Decode_IndexedWithTransparency_BuildsAlphaFromPalette()
    {
        // 4-bit indexed, 3x1: indices 0, 1, 2 packed into two bytes.
        byte[] indices = [0x01, 0x20];
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] trns = [255, 0]; // index 0 opaque, index 1 transparent, index 2 defaults to opaque
        var png = PngFixtures.Build(3, 1, 4, colorType: 3, indices, palette: palette, transparency: trns);

        var decoded = PngDecoder.Decode(png);

        Assert.Equal(PngColorSpace.Indexed, decoded.ColorSpace);
        Assert.Equal(indices, decoded.PixelData); // stays packed for PDF
        Assert.Equal(palette, decoded.Palette);
        Assert.Equal(new byte[] { 255, 0, 255 }, decoded.Alpha);
    }

    [Fact]
    public void Decode_RejectsInterlacedPngs_WithClearMessage()
    {
        var png = PngFixtures.Build(2, 2, 8, colorType: 2, Rgb2X2, interlace: 1);

        var ex = Assert.Throws<ImageFormatException>(() => PngDecoder.Decode(png));
        Assert.Contains("Interlaced", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_GarbageInput_ThrowsOnlyImageFormatException()
    {
        var valid = PngFixtures.Build(2, 2, 8, colorType: 2, Rgb2X2);
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            byte[] data;
            if (seed % 2 == 0)
            {
                data = (byte[])valid.Clone();
                for (var i = 0; i < 6; i++)
                {
                    data[random.Next(8, data.Length)] = (byte)random.Next(256); // keep the signature
                }
            }
            else
            {
                data = new byte[random.Next(0, 200)];
                random.NextBytes(data);
            }

            try
            {
                _ = PngDecoder.Decode(data);
            }
            catch (ImageFormatException)
            {
            }
        }
    }
}

public class JpegParserTests
{
    [Fact]
    public void Parse_ReadsFrameHeader()
    {
        var jpeg = PngFixtures.BuildJpegHeader(640, 480);

        var info = JpegParser.Parse(jpeg);

        Assert.Equal(640, info.Width);
        Assert.Equal(480, info.Height);
        Assert.Equal(3, info.Components);
        Assert.Equal(8, info.BitsPerComponent);
    }

    [Fact]
    public void Parse_GrayscaleSingleComponent()
    {
        var info = JpegParser.Parse(PngFixtures.BuildJpegHeader(10, 20, components: 1));

        Assert.Equal(1, info.Components);
    }

    [Fact]
    public void Parse_RejectsNonJpeg()
    {
        Assert.Throws<ImageFormatException>(() => JpegParser.Parse(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Parse_GarbageInput_ThrowsOnlyImageFormatException()
    {
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var data = new byte[random.Next(0, 100)];
            random.NextBytes(data);
            if (data.Length >= 2)
            {
                data[0] = 0xFF;
                data[1] = 0xD8; // force past the signature check half the time
            }

            try
            {
                _ = JpegParser.Parse(data);
            }
            catch (ImageFormatException)
            {
            }
        }
    }
}
