using System.Buffers.Binary;
using System.IO.Compression;

namespace Charta.Imaging;

/// <summary>How the decoded samples should be interpreted in PDF terms.</summary>
internal enum PngColorSpace
{
    Gray,
    Rgb,
    Indexed,
}

/// <summary>
/// A decoded PNG ready for PDF embedding. <see cref="PixelData"/> is laid out exactly as a PDF image
/// stream expects (big-endian 16-bit samples, sub-byte rows padded to byte boundaries) — PNG's raw
/// scanline format and PDF's sample format agree, so unfiltering is the only transformation needed.
/// </summary>
internal sealed class DecodedPng
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BitsPerComponent { get; init; }

    public required PngColorSpace ColorSpace { get; init; }

    public required byte[] PixelData { get; init; }

    /// <summary>RGB triplets for <see cref="PngColorSpace.Indexed"/>; null otherwise.</summary>
    public byte[]? Palette { get; init; }

    /// <summary>8-bit alpha per pixel when the source had transparency; becomes an SMask.</summary>
    public byte[]? Alpha { get; init; }
}

/// <summary>Managed PNG decoder (no native codecs): all five filter predictors, color types 0/2/3/4/6.</summary>
internal static class PngDecoder
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static DecodedPng Decode(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 8 || !span[..8].SequenceEqual(Signature))
        {
            throw new ImageFormatException("Not a PNG file (bad signature).");
        }

        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var sawHeader = false;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();

        var pos = 8;
        while (pos + 8 <= span.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(span[pos..]);
            if (length > int.MaxValue - 12 || pos + 12 + (long)length > span.Length)
            {
                throw new ImageFormatException("PNG chunk overruns the file.");
            }

            var type = span.Slice(pos + 4, 4);
            var chunk = span.Slice(pos + 8, (int)length);
            pos += 12 + (int)length; // length + type + data + CRC (not verified)

            if (type.SequenceEqual("IHDR"u8))
            {
                if (chunk.Length < 13)
                {
                    throw new ImageFormatException("IHDR chunk is too short.");
                }

                var rawWidth = BinaryPrimitives.ReadUInt32BigEndian(chunk);
                var rawHeight = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                if (rawWidth is 0 or > int.MaxValue || rawHeight is 0 or > int.MaxValue)
                {
                    throw new ImageFormatException($"Unreasonable PNG dimensions {rawWidth}x{rawHeight}.");
                }

                width = (int)rawWidth;
                height = (int)rawHeight;
                bitDepth = chunk[8];
                colorType = chunk[9];
                if (chunk[10] != 0 || chunk[11] != 0)
                {
                    throw new ImageFormatException("Unsupported PNG compression or filter method.");
                }

                if (chunk[12] != 0)
                {
                    throw new ImageFormatException("Interlaced (Adam7) PNGs are not supported yet.");
                }

                if (width <= 0 || height <= 0 || (long)width * height > 268_435_456)
                {
                    throw new ImageFormatException($"Unreasonable PNG dimensions {width}x{height}.");
                }

                sawHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (chunk.Length % 3 != 0 || chunk.Length > 768)
                {
                    throw new ImageFormatException("Invalid PLTE chunk length.");
                }

                palette = chunk.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = chunk.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (!sawHeader)
        {
            throw new ImageFormatException("PNG has no IHDR chunk.");
        }

        var channels = colorType switch
        {
            0 => 1, // grayscale
            2 => 3, // truecolor
            3 => 1, // indexed
            4 => 2, // grayscale + alpha
            6 => 4, // truecolor + alpha
            _ => throw new ImageFormatException($"Unknown PNG color type {colorType}."),
        };

        var validDepth = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            _ => bitDepth is 8 or 16,
        };
        if (!validDepth)
        {
            throw new ImageFormatException($"Invalid bit depth {bitDepth} for color type {colorType}.");
        }

        if (colorType == 3 && palette is null)
        {
            throw new ImageFormatException("Indexed PNG has no PLTE chunk.");
        }

        var bitsPerPixel = bitDepth * channels;
        var rowBytes = (width * bitsPerPixel + 7) / 8;
        var raw = Inflate(idat, height * (rowBytes + 1L));
        Unfilter(raw, rowBytes, height, Math.Max(1, bitsPerPixel / 8));

        // Strip the per-row filter bytes into a contiguous sample buffer.
        if ((long)rowBytes * height > int.MaxValue)
        {
            throw new ImageFormatException("PNG pixel data is too large.");
        }

        var samples = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            raw.AsSpan(y * (rowBytes + 1) + 1, rowBytes).CopyTo(samples.AsSpan(y * rowBytes));
        }

        return colorType switch
        {
            0 => new DecodedPng
            {
                Width = width,
                Height = height,
                BitsPerComponent = bitDepth,
                ColorSpace = PngColorSpace.Gray,
                PixelData = samples,
            },
            2 => new DecodedPng
            {
                Width = width,
                Height = height,
                BitsPerComponent = bitDepth,
                ColorSpace = PngColorSpace.Rgb,
                PixelData = samples,
            },
            3 => new DecodedPng
            {
                Width = width,
                Height = height,
                BitsPerComponent = bitDepth,
                ColorSpace = PngColorSpace.Indexed,
                PixelData = samples,
                Palette = palette,
                Alpha = transparency is null ? null : IndexedAlpha(samples, width, height, bitDepth, rowBytes, transparency),
            },
            4 => SplitAlpha(samples, width, height, bitDepth, colorChannels: 1),
            6 => SplitAlpha(samples, width, height, bitDepth, colorChannels: 3),
            _ => throw new ImageFormatException($"Unknown PNG color type {colorType}."),
        };
    }

    private static byte[] Inflate(MemoryStream idat, long expectedLength)
    {
        if (expectedLength is <= 0 or > 1_073_741_824)
        {
            throw new ImageFormatException("PNG pixel data size is out of range.");
        }

        idat.Position = 0;
        var output = new byte[expectedLength];
        try
        {
            using var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true);
            var read = 0;
            while (read < output.Length)
            {
                var n = zlib.Read(output, read, output.Length - read);
                if (n == 0)
                {
                    throw new ImageFormatException("PNG pixel data ended prematurely.");
                }

                read += n;
            }
        }
        catch (InvalidDataException e)
        {
            throw new ImageFormatException("PNG pixel data is not valid zlib.", e);
        }

        return output;
    }

    /// <summary>Reverses the five PNG filter predictors in place (RFC 2083 §6).</summary>
    private static void Unfilter(byte[] raw, int rowBytes, int height, int bytesPerPixel)
    {
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (rowBytes + 1);
            var filter = raw[rowStart];
            var row = raw.AsSpan(rowStart + 1, rowBytes);
            var previous = y > 0 ? raw.AsSpan(rowStart - rowBytes, rowBytes) : default;

            switch (filter)
            {
                case 0:
                    break;
                case 1: // Sub
                    for (var x = bytesPerPixel; x < rowBytes; x++)
                    {
                        row[x] = unchecked((byte)(row[x] + row[x - bytesPerPixel]));
                    }

                    break;
                case 2: // Up
                    if (y > 0)
                    {
                        for (var x = 0; x < rowBytes; x++)
                        {
                            row[x] = unchecked((byte)(row[x] + previous[x]));
                        }
                    }

                    break;
                case 3: // Average
                    for (var x = 0; x < rowBytes; x++)
                    {
                        var left = x >= bytesPerPixel ? row[x - bytesPerPixel] : 0;
                        var up = y > 0 ? previous[x] : 0;
                        row[x] = unchecked((byte)(row[x] + (left + up) / 2));
                    }

                    break;
                case 4: // Paeth
                    for (var x = 0; x < rowBytes; x++)
                    {
                        int left = x >= bytesPerPixel ? row[x - bytesPerPixel] : 0;
                        int up = y > 0 ? previous[x] : 0;
                        int upLeft = y > 0 && x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
                        row[x] = unchecked((byte)(row[x] + Paeth(left, up, upLeft)));
                    }

                    break;
                default:
                    throw new ImageFormatException($"Unknown PNG filter type {filter}.");
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Splits interleaved color+alpha samples; 16-bit alpha keeps its high byte (SMask is 8-bit).</summary>
    private static DecodedPng SplitAlpha(byte[] samples, int width, int height, int bitDepth, int colorChannels)
    {
        var bytesPerSample = bitDepth / 8;
        var pixels = width * height;
        var color = new byte[pixels * colorChannels * bytesPerSample];
        var alpha = new byte[pixels];

        var stride = (colorChannels + 1) * bytesPerSample;
        for (var i = 0; i < pixels; i++)
        {
            var source = i * stride;
            samples.AsSpan(source, colorChannels * bytesPerSample)
                .CopyTo(color.AsSpan(i * colorChannels * bytesPerSample));
            alpha[i] = samples[source + colorChannels * bytesPerSample];
        }

        return new DecodedPng
        {
            Width = width,
            Height = height,
            BitsPerComponent = bitDepth,
            ColorSpace = colorChannels == 1 ? PngColorSpace.Gray : PngColorSpace.Rgb,
            PixelData = color,
            Alpha = alpha,
        };
    }

    /// <summary>Builds the per-pixel alpha map for an indexed PNG with a tRNS chunk.</summary>
    private static byte[] IndexedAlpha(byte[] samples, int width, int height, int bitDepth, int rowBytes, byte[] transparency)
    {
        var alpha = new byte[width * height];
        var pixelsPerByte = 8 / bitDepth;
        var mask = (1 << bitDepth) - 1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var byteIndex = y * rowBytes + x / pixelsPerByte;
                var shift = (pixelsPerByte - 1 - x % pixelsPerByte) * bitDepth;
                var paletteIndex = (samples[byteIndex] >> shift) & mask;
                alpha[y * width + x] = paletteIndex < transparency.Length ? transparency[paletteIndex] : (byte)255;
            }
        }

        return alpha;
    }
}
