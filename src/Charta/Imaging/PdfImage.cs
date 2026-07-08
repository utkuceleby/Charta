using Charta.Cos;

namespace Charta.Imaging;

/// <summary>
/// An image prepared for PDF embedding as an Image XObject (ISO 32000-2 §8.9).
/// PNGs are decoded to raw samples (Flate-compressed by the writer); JPEGs pass through
/// untouched under DCTDecode. Transparency becomes a grayscale SMask XObject.
/// </summary>
internal sealed class PdfImage
{
    private readonly DecodedPng? _png;
    private readonly ReadOnlyMemory<byte> _jpegData;
    private readonly JpegInfo? _jpeg;

    public int Width { get; }

    public int Height { get; }

    private PdfImage(DecodedPng png)
    {
        _png = png;
        Width = png.Width;
        Height = png.Height;
    }

    private PdfImage(ReadOnlyMemory<byte> jpegData, JpegInfo info)
    {
        _jpegData = jpegData;
        _jpeg = info;
        Width = info.Width;
        Height = info.Height;
    }

    /// <summary>Sniffs the format from the file signature.</summary>
    public static PdfImage FromBytes(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length >= 2 && span[0] == 0xFF && span[1] == 0xD8)
        {
            return new PdfImage(data, JpegParser.Parse(data));
        }

        if (span.Length >= 8 && span[0] == 0x89 && span[1] == 0x50)
        {
            return new PdfImage(PngDecoder.Decode(data));
        }

        throw new ImageFormatException("Unrecognized image format; PNG and JPEG are supported.");
    }

    /// <summary>Writes the image (and its SMask, if any); <paramref name="imageReference"/> becomes the XObject.</summary>
    public void Write(PdfWriter writer, CosReference imageReference)
    {
        if (_jpeg is not null)
        {
            WriteJpeg(writer, imageReference);
        }
        else
        {
            WritePng(writer, imageReference);
        }
    }

    private void WriteJpeg(PdfWriter writer, CosReference imageReference)
    {
        var stream = new CosStream(_jpegData.ToArray()) { AllowCompression = false };
        FillCommon(stream.Dictionary);
        stream.Dictionary[CosNames.Filter] = CosNames.DctDecode;
        stream.Dictionary[CosNames.BitsPerComponent] = new CosInteger(_jpeg!.BitsPerComponent);
        stream.Dictionary[CosNames.ColorSpace] = _jpeg.Components switch
        {
            1 => CosNames.DeviceGray,
            3 => CosNames.DeviceRgb,
            _ => CosNames.DeviceCmyk,
        };
        writer.WriteObject(imageReference, stream);
    }

    private void WritePng(PdfWriter writer, CosReference imageReference)
    {
        var png = _png!;
        CosReference? smaskRef = null;
        if (png.Alpha is not null)
        {
            smaskRef = writer.Allocate();
            var smask = new CosStream(png.Alpha);
            smask.Dictionary[CosNames.Type] = CosNames.XObject;
            smask.Dictionary[CosNames.Subtype] = CosNames.Image;
            smask.Dictionary[CosNames.Width] = new CosInteger(png.Width);
            smask.Dictionary[CosNames.Height] = new CosInteger(png.Height);
            smask.Dictionary[CosNames.ColorSpace] = CosNames.DeviceGray;
            smask.Dictionary[CosNames.BitsPerComponent] = new CosInteger(8);
            writer.WriteObject(smaskRef, smask);
        }

        var stream = new CosStream(png.PixelData);
        FillCommon(stream.Dictionary);
        stream.Dictionary[CosNames.BitsPerComponent] = new CosInteger(png.BitsPerComponent);
        stream.Dictionary[CosNames.ColorSpace] = png.ColorSpace switch
        {
            PngColorSpace.Gray => CosNames.DeviceGray,
            PngColorSpace.Rgb => CosNames.DeviceRgb,
            PngColorSpace.Indexed => new CosArray(
                CosNames.Indexed,
                CosNames.DeviceRgb,
                new CosInteger(png.Palette!.Length / 3 - 1),
                new CosString(png.Palette)),
            _ => throw new ImageFormatException($"Unknown color space {png.ColorSpace}."),
        };
        if (smaskRef is not null)
        {
            stream.Dictionary[CosNames.SMask] = smaskRef;
        }

        writer.WriteObject(imageReference, stream);
    }

    private void FillCommon(CosDictionary dictionary)
    {
        dictionary[CosNames.Type] = CosNames.XObject;
        dictionary[CosNames.Subtype] = CosNames.Image;
        dictionary[CosNames.Width] = new CosInteger(Width);
        dictionary[CosNames.Height] = new CosInteger(Height);
    }
}
