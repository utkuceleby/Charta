using System.IO.Compression;

namespace Charta.Cos;

/// <summary>A PDF stream object (ISO 32000-2 §7.3.8): a dictionary plus raw data.</summary>
internal sealed class CosStream(byte[] data) : CosValue
{
    public CosDictionary Dictionary { get; } = new();

    public byte[] Data { get; } = data;

    /// <summary>Set to false for structures that must stay readable regardless of writer options (e.g. during debugging).</summary>
    public bool AllowCompression { get; init; } = true;

    public override void Write(PdfWriter writer)
    {
        var payload = Data;
        if (AllowCompression && writer.Options.CompressStreams)
        {
            payload = Compress(Data);
            Dictionary[CosNames.Filter] = CosNames.FlateDecode;
        }

        Dictionary[CosNames.Length] = new CosInteger(payload.Length);
        Dictionary.Write(writer);
        writer.WriteAscii("\nstream\n");
        writer.WriteRaw(payload);
        writer.WriteAscii("\nendstream");
    }

    private static byte[] Compress(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return buffer.ToArray();
    }
}
