using System.IO.Compression;

namespace Charta.Smoke;

/// <summary>
/// Builds PNG files in code (like <see cref="SyntheticFont"/> for fonts). The decoder ignores CRCs,
/// so fixtures write zero CRCs. Forward filtering here mirrors RFC 2083 so unfiltering is verified
/// against independently computed input.
/// </summary>
internal static class PngFixtures
{
    public static byte[] Build(
        int width,
        int height,
        byte bitDepth,
        byte colorType,
        byte[] rawRows,
        byte filterType = 0,
        byte[]? palette = null,
        byte[]? transparency = null,
        byte interlace = 0)
    {
        var channels = colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        var bytesPerPixel = Math.Max(1, bitDepth * channels / 8);
        var rowBytes = (width * bitDepth * channels + 7) / 8;

        // Apply the forward filter so the decoder has real work to do.
        var filtered = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            filtered.Add(filterType);
            for (var x = 0; x < rowBytes; x++)
            {
                int current = rawRows[y * rowBytes + x];
                int left = x >= bytesPerPixel ? rawRows[y * rowBytes + x - bytesPerPixel] : 0;
                int up = y > 0 ? rawRows[(y - 1) * rowBytes + x] : 0;
                int upLeft = y > 0 && x >= bytesPerPixel ? rawRows[(y - 1) * rowBytes + x - bytesPerPixel] : 0;
                var encoded = filterType switch
                {
                    0 => current,
                    1 => current - left,
                    2 => current - up,
                    3 => current - (left + up) / 2,
                    4 => current - Paeth(left, up, upLeft),
                    _ => throw new ArgumentOutOfRangeException(nameof(filterType)),
                };
                filtered.Add(unchecked((byte)encoded));
            }
        }

        using var idat = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(filtered.ToArray());
        }

        var builder = new BigEndianBuilder()
            .Bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new BigEndianBuilder()
            .U32((uint)width).U32((uint)height)
            .U8(bitDepth).U8(colorType).U8(0).U8(0).U8(interlace)
            .ToArray();
        Chunk(builder, "IHDR", ihdr);
        if (palette is not null)
        {
            Chunk(builder, "PLTE", palette);
        }

        if (transparency is not null)
        {
            Chunk(builder, "tRNS", transparency);
        }

        Chunk(builder, "IDAT", idat.ToArray());
        Chunk(builder, "IEND", []);
        return builder.ToArray();
    }

    private static void Chunk(BigEndianBuilder builder, string type, byte[] data)
    {
        builder.U32((uint)data.Length);
        foreach (var c in type)
        {
            builder.U8((byte)c);
        }

        builder.Bytes(data);
        builder.U32(0); // CRC — not verified by the decoder
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Minimal JPEG: SOI, JFIF APP0, SOF0 frame header, EOI. Enough for header parsing.</summary>
    public static byte[] BuildJpegHeader(int width, int height, byte components = 3, byte precision = 8)
    {
        var builder = new BigEndianBuilder()
            .U8(0xFF).U8(0xD8)                             // SOI
            .U8(0xFF).U8(0xE0).U16(16)                     // APP0
            .Bytes("JFIF\0"u8.ToArray())
            .U8(1).U8(1).U8(0).U16(72).U16(72).U8(0).U8(0)
            .U8(0xFF).U8(0xC0).U16(8 + components * 3)     // SOF0
            .U8(precision).U16(height).U16(width).U8(components);
        for (byte i = 1; i <= components; i++)
        {
            builder.U8(i).U8(0x11).U8(0);
        }

        return builder.U8(0xFF).U8(0xD9).ToArray();        // EOI
    }
}
