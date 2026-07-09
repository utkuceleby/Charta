using System.Security.Cryptography;
using System.Text;

namespace Charta.Cos;

/// <summary>
/// Streaming PDF serializer. Objects are written and flushed as they arrive; only their byte offsets are
/// retained, so memory stays proportional to the largest single object, not the document.
/// All output goes through <see cref="WriteRaw"/>, which tracks the byte offset (the target stream does not
/// need to be seekable) and feeds a running hash used to derive the file identifier deterministically.
/// </summary>
internal sealed class PdfWriter : IDisposable
{
    private const long UnwrittenOffset = -1;

    private readonly Stream _stream;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly List<long> _offsets = [UnwrittenOffset]; // index 0 is the reserved free entry
    private long _position;
    private bool _disposed;

    public PdfWriterOptions Options { get; }

    public PdfWriter(Stream stream, PdfWriterOptions? options = null)
    {
        _stream = stream;
        Options = options ?? PdfWriterOptions.Default;
    }

    public void WriteRaw(ReadOnlySpan<byte> bytes)
    {
        _stream.Write(bytes);
        _hash.AppendData(bytes);
        _position += bytes.Length;
    }

    public void WriteAscii(string text)
    {
        Span<byte> buffer = text.Length <= 256 ? stackalloc byte[text.Length] : new byte[text.Length];
        var written = Encoding.ASCII.GetBytes(text, buffer);
        WriteRaw(buffer[..written]);
    }

    /// <summary>Reserves an object number so objects can reference each other before being written.</summary>
    public CosReference Allocate()
    {
        _offsets.Add(UnwrittenOffset);
        return new CosReference(_offsets.Count - 1);
    }

    public void WriteHeader()
    {
        WriteAscii("%PDF-1.7\n%");
        // Four bytes above 127 mark the file as binary for transfer tools (ISO 32000-2 §7.5.2).
        WriteRaw([0xC2, 0xA9, 0xC2, 0xA9]);
        WriteAscii("\n");
    }

    public void WriteObject(CosReference reference, CosValue value)
    {
        if (_offsets[reference.ObjectNumber] != UnwrittenOffset)
        {
            throw new InvalidOperationException($"Object {reference.ObjectNumber} has already been written.");
        }

        _offsets[reference.ObjectNumber] = _position;
        WriteAscii($"{reference.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)} 0 obj\n");
        value.Write(this);
        WriteAscii("\nendobj\n");
    }

    /// <summary>Writes the cross-reference data, trailer, and end-of-file marker, then flushes.</summary>
    public void WriteTrailer(CosReference root, CosReference? info = null)
    {
        var fileId = ComputeFileId();
        switch (Options.XrefMode)
        {
            case XrefMode.Classic:
                WriteClassicXref(root, fileId, info);
                break;
            case XrefMode.Stream:
                WriteXrefStream(root, fileId, info);
                break;
            default:
                throw new InvalidOperationException($"Unknown xref mode {Options.XrefMode}.");
        }

        _stream.Flush();
    }

    private void WriteClassicXref(CosReference root, byte[] fileId, CosReference? info)
    {
        EnsureAllObjectsWritten();

        var xrefOffset = _position;
        var count = _offsets.Count;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append(count).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i < count; i++)
        {
            sb.Append(_offsets[i].ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        WriteAscii(sb.ToString());

        var trailer = new CosDictionary
        {
            [CosNames.Size] = new CosInteger(count),
            [CosNames.Root] = root,
            [CosNames.Id] = new CosArray(new CosString(fileId), new CosString(fileId)),
        };
        if (info is not null)
        {
            trailer[CosNames.Info] = info;
        }

        WriteAscii("trailer\n");
        trailer.Write(this);
        WriteAscii($"\nstartxref\n{xrefOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private void WriteXrefStream(CosReference root, byte[] fileId, CosReference? info)
    {
        var xrefRef = Allocate();
        EnsureAllObjectsWritten(skipObjectNumber: xrefRef.ObjectNumber);

        var xrefOffset = _position;
        _offsets[xrefRef.ObjectNumber] = xrefOffset;

        // Field widths: 1 byte type, 4 bytes offset, 2 bytes generation (ISO 32000-2 §7.5.8.2).
        var count = _offsets.Count;
        var data = new byte[count * 7];
        for (var i = 1; i < count; i++)
        {
            var entry = data.AsSpan(i * 7, 7);
            entry[0] = 1;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry[1..5], checked((uint)_offsets[i]));
        }

        // Entry 0: the free-list head with generation 65535.
        data[5] = 0xFF;
        data[6] = 0xFF;

        var xref = new CosStream(data) { AllowCompression = false };
        xref.Dictionary[CosNames.Type] = CosNames.XRef;
        xref.Dictionary[CosNames.Size] = new CosInteger(count);
        xref.Dictionary[CosNames.W] = CosArray.OfIntegers(1, 4, 2);
        xref.Dictionary[CosNames.Root] = root;
        xref.Dictionary[CosNames.Id] = new CosArray(new CosString(fileId), new CosString(fileId));
        if (info is not null)
        {
            xref.Dictionary[CosNames.Info] = info;
        }

        _offsets[xrefRef.ObjectNumber] = UnwrittenOffset; // WriteObject records the real offset
        WriteObject(xrefRef, xref);
        WriteAscii($"startxref\n{xrefOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private void EnsureAllObjectsWritten(int skipObjectNumber = 0)
    {
        for (var i = 1; i < _offsets.Count; i++)
        {
            if (i != skipObjectNumber && _offsets[i] == UnwrittenOffset)
            {
                throw new InvalidOperationException($"Object {i} was allocated but never written.");
            }
        }
    }

    /// <summary>First 16 bytes of the SHA-256 over everything written so far. Deterministic for identical content.</summary>
    private byte[] ComputeFileId() => _hash.GetCurrentHash().AsSpan(0, 16).ToArray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hash.Dispose();
        _stream.Flush();
    }
}
