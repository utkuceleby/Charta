using System.Buffers.Binary;

namespace Charta.Fonts;

/// <summary>
/// A character-to-glyph subtable (formats 4 and 12). Lookups read the raw table span directly;
/// nothing is materialized up front.
/// </summary>
internal sealed class CmapSubtable
{
    private readonly ReadOnlyMemory<byte> _subtable;
    private readonly ushort _format;

    private CmapSubtable(ReadOnlyMemory<byte> subtable, ushort format)
    {
        _subtable = subtable;
        _format = format;
    }

    /// <summary>Picks the best subtable from a 'cmap' table: format 12 (full Unicode) over format 4 (BMP).</summary>
    public static CmapSubtable? SelectBest(ReadOnlyMemory<byte> cmap)
    {
        var reader = new SfntReader(cmap.Span);
        reader.Skip(2); // version
        var numTables = reader.ReadUInt16();

        CmapSubtable? best = null;
        var bestScore = 0;
        for (var i = 0; i < numTables; i++)
        {
            var platformId = reader.ReadUInt16();
            reader.Skip(2); // encodingID
            var rawOffset = reader.ReadUInt32();

            // Only Unicode-capable platforms (0 = Unicode, 3 = Windows).
            if (platformId is not (0 or 3))
            {
                continue;
            }

            if (rawOffset > int.MaxValue || rawOffset + 2 > (uint)cmap.Length)
            {
                throw new FontFormatException("cmap subtable offset points outside the table.");
            }

            var offset = (int)rawOffset;

            var format = BinaryPrimitives.ReadUInt16BigEndian(cmap.Span[offset..]);
            var score = format switch
            {
                12 => 2,
                4 => 1,
                _ => 0,
            };

            if (score > bestScore)
            {
                bestScore = score;
                best = new CmapSubtable(cmap[offset..], format);
            }
        }

        return best;
    }

    public ushort Map(int codepoint)
    {
        if (codepoint < 0)
        {
            return 0;
        }

        return _format switch
        {
            4 => codepoint <= 0xFFFF ? MapFormat4((ushort)codepoint) : (ushort)0,
            12 => MapFormat12((uint)codepoint),
            _ => 0,
        };
    }

    private ushort MapFormat4(ushort code)
    {
        var span = _subtable.Span;
        if (span.Length < 16)
        {
            return 0;
        }

        var segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
        var segCount = segCountX2 / 2;
        const int endCodesStart = 14;
        var startCodesStart = endCodesStart + segCountX2 + 2;
        var idDeltasStart = startCodesStart + segCountX2;
        var idRangeOffsetsStart = idDeltasStart + segCountX2;
        if (segCount == 0 || idRangeOffsetsStart + segCountX2 > span.Length)
        {
            return 0;
        }

        // Binary search for the first segment whose endCode >= code.
        int lo = 0, hi = segCount - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            var endCode = BinaryPrimitives.ReadUInt16BigEndian(span[(endCodesStart + mid * 2)..]);
            if (endCode < code)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        var startCode = BinaryPrimitives.ReadUInt16BigEndian(span[(startCodesStart + lo * 2)..]);
        if (code < startCode)
        {
            return 0;
        }

        var idDelta = BinaryPrimitives.ReadInt16BigEndian(span[(idDeltasStart + lo * 2)..]);
        var idRangeOffsetPos = idRangeOffsetsStart + lo * 2;
        var idRangeOffset = BinaryPrimitives.ReadUInt16BigEndian(span[idRangeOffsetPos..]);

        if (idRangeOffset == 0)
        {
            return (ushort)(code + idDelta);
        }

        // The offset is relative to its own position in the idRangeOffset array (§ cmap format 4).
        var glyphIdPos = idRangeOffsetPos + idRangeOffset + (code - startCode) * 2;
        if (glyphIdPos + 2 > span.Length)
        {
            return 0;
        }

        var glyphId = BinaryPrimitives.ReadUInt16BigEndian(span[glyphIdPos..]);
        return glyphId == 0 ? (ushort)0 : (ushort)(glyphId + idDelta);
    }

    private ushort MapFormat12(uint code)
    {
        var span = _subtable.Span;
        if (span.Length < 16)
        {
            return 0;
        }

        var rawGroups = BinaryPrimitives.ReadUInt32BigEndian(span[12..]);
        const int groupsStart = 16;
        if (groupsStart + rawGroups * 12L > span.Length)
        {
            return 0;
        }

        var numGroups = (int)rawGroups;

        int lo = 0, hi = numGroups - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var groupPos = groupsStart + mid * 12;
            var startChar = BinaryPrimitives.ReadUInt32BigEndian(span[groupPos..]);
            var endChar = BinaryPrimitives.ReadUInt32BigEndian(span[(groupPos + 4)..]);

            if (code < startChar)
            {
                hi = mid - 1;
            }
            else if (code > endChar)
            {
                lo = mid + 1;
            }
            else
            {
                var startGlyph = BinaryPrimitives.ReadUInt32BigEndian(span[(groupPos + 8)..]);
                return (ushort)(startGlyph + (code - startChar));
            }
        }

        return 0;
    }
}
