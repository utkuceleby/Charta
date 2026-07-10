using System.Buffers.Binary;

namespace Charta.Imaging;

/// <summary>Dimensions and color layout of a JPEG, read from its SOF header.</summary>
internal sealed class JpegInfo
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Components { get; init; }

    public required int BitsPerComponent { get; init; }
}

/// <summary>
/// Reads just enough of a JPEG to embed it: PDF supports DCTDecode natively, so the compressed
/// file bytes pass through untouched and only the frame header needs parsing.
/// </summary>
internal static class JpegParser
{
    public static JpegInfo Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 4 || span[0] != 0xFF || span[1] != 0xD8)
        {
            throw new ImageFormatException("Not a JPEG file (missing SOI marker).");
        }

        var pos = 2;
        while (pos + 4 <= span.Length)
        {
            if (span[pos] != 0xFF)
            {
                throw new ImageFormatException($"Corrupt JPEG marker stream at offset {pos}.");
            }

            // Skip fill bytes.
            while (pos + 1 < span.Length && span[pos + 1] == 0xFF)
            {
                pos++;
            }

            if (pos + 1 >= span.Length)
            {
                break; // fill bytes ran to the end with no marker following
            }

            var marker = span[pos + 1];
            pos += 2;

            // Standalone markers carry no length.
            if (marker is 0x01 or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (pos + 2 > span.Length)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(span[pos..]);
            if (length < 2 || pos + length > span.Length)
            {
                throw new ImageFormatException("JPEG segment overruns the file.");
            }

            // SOF0..SOF15 excluding DHT/JPG/DAC (C4, C8, CC) carry the frame header.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 8)
                {
                    throw new ImageFormatException("JPEG frame header is too short.");
                }

                var precision = span[pos + 2];
                var height = BinaryPrimitives.ReadUInt16BigEndian(span[(pos + 3)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(span[(pos + 5)..]);
                var components = span[pos + 7];
                if (width == 0 || height == 0 || components is not (1 or 3 or 4))
                {
                    throw new ImageFormatException($"Unsupported JPEG layout: {width}x{height}, {components} components.");
                }

                return new JpegInfo
                {
                    Width = width,
                    Height = height,
                    Components = components,
                    BitsPerComponent = precision,
                };
            }

            if (marker == 0xDA)
            {
                break; // Start of scan: no SOF seen before pixel data.
            }

            pos += length;
        }

        throw new ImageFormatException("JPEG has no frame header (SOF marker).");
    }
}
