using System.Buffers.Binary;

namespace Charta.Fonts;

/// <summary>
/// Serializes a set of tables into a valid SFNT file: sorted directory, per-table checksums,
/// 4-byte alignment, and the whole-file checkSumAdjustment written into 'head' (if present).
/// </summary>
internal static class SfntAssembler
{
    private const uint ChecksumMagic = 0xB1B0AFBA;

    public static byte[] Assemble(IReadOnlyList<(string Tag, byte[] Data)> tables)
    {
        var sorted = tables.OrderBy(t => t.Tag, StringComparer.Ordinal).ToList();
        var numTables = sorted.Count;

        var entrySelector = 0;
        while (1 << (entrySelector + 1) <= numTables)
        {
            entrySelector++;
        }

        var searchRange = (1 << entrySelector) * 16;
        var rangeShift = numTables * 16 - searchRange;

        var headerSize = 12 + numTables * 16;
        var totalSize = headerSize;
        foreach (var (_, data) in sorted)
        {
            totalSize += Align4(data.Length);
        }

        var file = new byte[totalSize];
        var span = file.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)numTables);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)rangeShift);

        var offset = headerSize;
        var headOffset = -1;
        for (var i = 0; i < numTables; i++)
        {
            var (tag, data) = sorted[i];
            data.CopyTo(span[offset..]);

            var record = span[(12 + i * 16)..];
            for (var c = 0; c < 4; c++)
            {
                record[c] = (byte)tag[c];
            }

            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(span.Slice(offset, Align4(data.Length))));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);

            if (tag == "head")
            {
                headOffset = offset;
            }

            offset += Align4(data.Length);
        }

        if (headOffset >= 0)
        {
            var adjustment = ChecksumMagic - Checksum(span);
            BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + 8)..], adjustment);
        }

        return file;
    }

    /// <summary>Sum of big-endian uint32 words, zero-padded at the tail (OpenType spec).</summary>
    public static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var whole = data.Length & ~3;
        for (var i = 0; i < whole; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
        }

        if (whole < data.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            data[whole..].CopyTo(tail);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(tail));
        }

        return sum;
    }

    private static int Align4(int length) => (length + 3) & ~3;
}
