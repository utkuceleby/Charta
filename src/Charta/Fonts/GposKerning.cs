using System.Buffers.Binary;

namespace Charta.Fonts;

/// <summary>
/// Pair-adjustment kerning from the GPOS table (OpenType 'kern' feature, lookup type 2, both
/// glyph-pair and class-based subtables, including type-9 extension wrappers). Script and language
/// systems are intentionally ignored — every 'kern' feature applies, the right default for the
/// simple shaper. Kerning is decoration: malformed GPOS data disables it instead of failing.
/// </summary>
internal sealed class GposKerning
{
    private const ushort PairAdjustmentLookup = 2;
    private const ushort ExtensionLookup = 9;
    private const ushort XAdvanceFlag = 0x0004;

    private readonly ReadOnlyMemory<byte> _gpos;
    private readonly List<int> _pairPosSubtables;

    private GposKerning(ReadOnlyMemory<byte> gpos, List<int> pairPosSubtables)
    {
        _gpos = gpos;
        _pairPosSubtables = pairPosSubtables;
    }

    /// <summary>Null when the font has no GPOS 'kern' feature (or the table is unreadable).</summary>
    public static GposKerning? TryCreate(SfntFont font)
    {
        if (!font.TryGetTable("GPOS", out var gpos))
        {
            return null;
        }

        try
        {
            var subtables = CollectPairPosSubtables(gpos);
            return subtables.Count == 0 ? null : new GposKerning(gpos, subtables);
        }
        catch (FontFormatException)
        {
            return null;
        }
    }

    private static List<int> CollectPairPosSubtables(ReadOnlyMemory<byte> gpos)
    {
        var span = gpos.Span;
        var reader = new SfntReader(span) { Position = 6 };
        var featureListOffset = reader.ReadUInt16();
        var lookupListOffset = reader.ReadUInt16();

        // Collect the lookup indices of every 'kern' feature.
        var lookupIndices = new SortedSet<ushort>();
        reader.Position = featureListOffset;
        var featureCount = reader.ReadUInt16();
        for (var i = 0; i < featureCount; i++)
        {
            var tag = reader.ReadUInt32();
            var featureOffset = reader.ReadUInt16();
            if (tag != 0x6B65726E) // 'kern'
            {
                continue;
            }

            var feature = new SfntReader(span) { Position = featureListOffset + featureOffset + 2 };
            var indexCount = feature.ReadUInt16();
            for (var j = 0; j < indexCount; j++)
            {
                lookupIndices.Add(feature.ReadUInt16());
            }
        }

        var subtables = new List<int>();
        reader.Position = lookupListOffset;
        var lookupCount = reader.ReadUInt16();
        foreach (var index in lookupIndices)
        {
            if (index >= lookupCount)
            {
                continue;
            }

            reader.Position = lookupListOffset + 2 + index * 2;
            var lookupOffset = lookupListOffset + reader.ReadUInt16();

            var lookup = new SfntReader(span) { Position = lookupOffset };
            var lookupType = lookup.ReadUInt16();
            lookup.Skip(2); // lookupFlag
            var subtableCount = lookup.ReadUInt16();
            for (var s = 0; s < subtableCount; s++)
            {
                var subtableOffset = lookupOffset + lookup.ReadUInt16();
                if (lookupType == PairAdjustmentLookup)
                {
                    subtables.Add(subtableOffset);
                }
                else if (lookupType == ExtensionLookup)
                {
                    var extension = new SfntReader(span) { Position = subtableOffset + 2 };
                    var extensionType = extension.ReadUInt16();
                    var extensionOffset = extension.ReadUInt32();
                    if (extensionType == PairAdjustmentLookup && extensionOffset <= int.MaxValue)
                    {
                        subtables.Add(subtableOffset + (int)extensionOffset);
                    }
                }
            }
        }

        return subtables;
    }

    /// <summary>X-advance adjustment (font units) applied to <paramref name="left"/> when followed by <paramref name="right"/>.</summary>
    public int GetAdjustment(ushort left, ushort right)
    {
        foreach (var subtableOffset in _pairPosSubtables)
        {
            try
            {
                if (TryLookup(subtableOffset, left, right, out var adjustment))
                {
                    return adjustment;
                }
            }
            catch (FontFormatException)
            {
                // Malformed subtable: skip it; kerning silently degrades.
            }
        }

        return 0;
    }

    private bool TryLookup(int subtable, ushort left, ushort right, out int adjustment)
    {
        adjustment = 0;
        var span = _gpos.Span;
        var reader = new SfntReader(span) { Position = subtable };
        var posFormat = reader.ReadUInt16();
        var coverageOffset = reader.ReadUInt16();
        var valueFormat1 = reader.ReadUInt16();
        var valueFormat2 = reader.ReadUInt16();

        if ((valueFormat1 & XAdvanceFlag) == 0)
        {
            return false; // this subtable does not adjust the first glyph's advance
        }

        var coverageIndex = CoverageIndex(span, subtable + coverageOffset, left);
        if (coverageIndex < 0)
        {
            return false;
        }

        var value1Size = 2 * ushort.PopCount(valueFormat1);
        var value2Size = 2 * ushort.PopCount(valueFormat2);
        // Fields ahead of XAdvance inside ValueRecord: XPlacement (0x1), YPlacement (0x2).
        var xAdvancePosition = 2 * ushort.PopCount((ushort)(valueFormat1 & 0x0003));

        if (posFormat == 1)
        {
            var pairSetCount = reader.ReadUInt16();
            if (coverageIndex >= pairSetCount)
            {
                return false;
            }

            reader.Position = subtable + 10 + coverageIndex * 2;
            var pairSet = subtable + reader.ReadUInt16();

            var set = new SfntReader(span) { Position = pairSet };
            var pairCount = set.ReadUInt16();
            var recordSize = 2 + value1Size + value2Size;

            int lo = 0, hi = pairCount - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var recordStart = pairSet + 2 + mid * recordSize;
                var secondGlyph = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, recordStart, 2));
                if (secondGlyph < right)
                {
                    lo = mid + 1;
                }
                else if (secondGlyph > right)
                {
                    hi = mid - 1;
                }
                else
                {
                    adjustment = BinaryPrimitives.ReadInt16BigEndian(Slice(span, recordStart + 2 + xAdvancePosition, 2));
                    return true;
                }
            }

            return false;
        }

        if (posFormat == 2)
        {
            var classDef1Offset = reader.ReadUInt16();
            var classDef2Offset = reader.ReadUInt16();
            var class1Count = reader.ReadUInt16();
            var class2Count = reader.ReadUInt16();

            var class1 = ClassOf(span, subtable + classDef1Offset, left);
            var class2 = ClassOf(span, subtable + classDef2Offset, right);
            if (class1 >= class1Count || class2 >= class2Count)
            {
                return false;
            }

            var recordSize = value1Size + value2Size;
            var recordStart = subtable + 16 + (class1 * class2Count + class2) * recordSize;
            adjustment = BinaryPrimitives.ReadInt16BigEndian(Slice(span, recordStart + xAdvancePosition, 2));
            return adjustment != 0; // class 0 pairs are usually "no kerning" — let later subtables try
        }

        return false;
    }

    private static int CoverageIndex(ReadOnlySpan<byte> span, int coverage, ushort glyph)
    {
        var reader = new SfntReader(span) { Position = coverage };
        var format = reader.ReadUInt16();
        var count = reader.ReadUInt16();

        if (format == 1)
        {
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var candidate = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, coverage + 4 + mid * 2, 2));
                if (candidate < glyph)
                {
                    lo = mid + 1;
                }
                else if (candidate > glyph)
                {
                    hi = mid - 1;
                }
                else
                {
                    return mid;
                }
            }

            return -1;
        }

        if (format == 2)
        {
            for (var i = 0; i < count; i++)
            {
                var rangeStart = coverage + 4 + i * 6;
                var start = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, rangeStart, 2));
                var end = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, rangeStart + 2, 2));
                if (glyph >= start && glyph <= end)
                {
                    var startCoverageIndex = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, rangeStart + 4, 2));
                    return startCoverageIndex + glyph - start;
                }
            }

            return -1;
        }

        return -1;
    }

    private static int ClassOf(ReadOnlySpan<byte> span, int classDef, ushort glyph)
    {
        var reader = new SfntReader(span) { Position = classDef };
        var format = reader.ReadUInt16();

        if (format == 1)
        {
            var startGlyph = reader.ReadUInt16();
            var glyphCount = reader.ReadUInt16();
            if (glyph < startGlyph || glyph >= startGlyph + glyphCount)
            {
                return 0;
            }

            return BinaryPrimitives.ReadUInt16BigEndian(Slice(span, classDef + 6 + (glyph - startGlyph) * 2, 2));
        }

        if (format == 2)
        {
            var rangeCount = reader.ReadUInt16();
            for (var i = 0; i < rangeCount; i++)
            {
                var rangeStart = classDef + 4 + i * 6;
                var start = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, rangeStart, 2));
                var end = BinaryPrimitives.ReadUInt16BigEndian(Slice(span, rangeStart + 2, 2));
                if (glyph >= start && glyph <= end)
                {
                    return BinaryPrimitives.ReadUInt16BigEndian(Slice(span, rangeStart + 4, 2));
                }
            }
        }

        return 0;
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> span, int offset, int length)
    {
        if (offset < 0 || offset > span.Length - length)
        {
            throw new FontFormatException($"GPOS read at offset {offset} is out of bounds.");
        }

        return span.Slice(offset, length);
    }
}
