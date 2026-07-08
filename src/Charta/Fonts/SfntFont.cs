using System.Text;

namespace Charta.Fonts;

/// <summary>
/// A parsed TrueType-flavored SFNT font (ISO/IEC 14496-22). Table data stays as slices of the original
/// buffer; only small header tables are decoded eagerly. CFF-flavored ('OTTO') fonts are not supported yet.
/// </summary>
internal sealed class SfntFont
{
    private const uint TrueTypeVersion = 0x00010000;
    private const uint TrueTag = 0x74727565;     // 'true' (legacy Apple)
    private const uint OttoTag = 0x4F54544F;     // 'OTTO' (CFF outlines)
    private const uint TtcTag = 0x74746366;      // 'ttcf'
    private const uint HeadMagic = 0x5F0F3CF5;

    private readonly ReadOnlyMemory<byte> _file;
    private readonly Dictionary<string, (int Offset, int Length)> _tables;
    private readonly ushort _numberOfHMetrics;
    private readonly CmapSubtable? _cmap;

    public int UnitsPerEm { get; }

    public short IndexToLocFormat { get; }

    public short XMin { get; }

    public short YMin { get; }

    public short XMax { get; }

    public short YMax { get; }

    public short Ascender { get; }

    public short Descender { get; }

    public ushort NumGlyphs { get; }

    public string PostScriptName { get; }

    public double ItalicAngle { get; }

    public ushort WeightClass { get; }

    public short CapHeight { get; }

    private SfntFont(
        ReadOnlyMemory<byte> file,
        Dictionary<string, (int, int)> tables,
        ushort numberOfHMetrics,
        CmapSubtable? cmap,
        int unitsPerEm,
        short indexToLocFormat,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        short ascender,
        short descender,
        ushort numGlyphs,
        string postScriptName,
        double italicAngle,
        ushort weightClass,
        short capHeight)
    {
        _file = file;
        _tables = tables;
        _numberOfHMetrics = numberOfHMetrics;
        _cmap = cmap;
        UnitsPerEm = unitsPerEm;
        IndexToLocFormat = indexToLocFormat;
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
        Ascender = ascender;
        Descender = descender;
        NumGlyphs = numGlyphs;
        PostScriptName = postScriptName;
        ItalicAngle = italicAngle;
        WeightClass = weightClass;
        CapHeight = capHeight;
    }

    public static SfntFont Parse(ReadOnlyMemory<byte> data, int ttcIndex = 0)
    {
        var reader = new SfntReader(data.Span);
        var version = reader.ReadUInt32();

        if (version == TtcTag)
        {
            reader.Skip(4); // TTC version
            var numFonts = ReadOffset(ref reader);
            if (ttcIndex < 0 || ttcIndex >= numFonts)
            {
                throw new FontFormatException($"Collection index {ttcIndex} is out of range; the collection has {numFonts} fonts.");
            }

            reader.Skip(ttcIndex * 4);
            var directoryOffset = ReadOffset(ref reader);
            if (directoryOffset > data.Length)
            {
                throw new FontFormatException("Collection directory offset points outside the font data.");
            }

            reader.Position = directoryOffset;
            version = reader.ReadUInt32();
        }

        if (version == OttoTag)
        {
            throw new FontFormatException("CFF-flavored OpenType fonts are not supported yet; use a TrueType-flavored font.");
        }

        if (version is not (TrueTypeVersion or TrueTag))
        {
            throw new FontFormatException($"Not an SFNT font (version 0x{version:X8}).");
        }

        var numTables = reader.ReadUInt16();
        reader.Skip(6); // searchRange, entrySelector, rangeShift

        var tables = new Dictionary<string, (int Offset, int Length)>(numTables, StringComparer.Ordinal);
        for (var i = 0; i < numTables; i++)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            reader.Skip(4); // checksum
            var offset = ReadOffset(ref reader);
            var length = ReadOffset(ref reader);
            if ((long)offset + length > data.Length)
            {
                throw new FontFormatException($"Table '{tag}' points outside the font data.");
            }

            tables[tag] = (offset, length);
        }

        var head = Require(data, tables, "head");
        var headReader = new SfntReader(head.Span);
        headReader.Skip(12);
        if (headReader.ReadUInt32() != HeadMagic)
        {
            throw new FontFormatException("Invalid magic number in 'head' table.");
        }

        headReader.Skip(2); // flags
        int unitsPerEm = headReader.ReadUInt16();
        if (unitsPerEm == 0)
        {
            throw new FontFormatException("unitsPerEm must be non-zero.");
        }

        headReader.Skip(16); // created, modified
        var xMin = headReader.ReadInt16();
        var yMin = headReader.ReadInt16();
        var xMax = headReader.ReadInt16();
        var yMax = headReader.ReadInt16();
        headReader.Skip(6); // macStyle, lowestRecPPEM, fontDirectionHint
        var indexToLocFormat = headReader.ReadInt16();
        if (indexToLocFormat is not (0 or 1))
        {
            throw new FontFormatException($"Unsupported indexToLocFormat {indexToLocFormat}.");
        }

        var hhea = Require(data, tables, "hhea");
        var hheaReader = new SfntReader(hhea.Span) { Position = 4 };
        var ascender = hheaReader.ReadInt16();
        var descender = hheaReader.ReadInt16();
        hheaReader.Position = 34;
        var numberOfHMetrics = hheaReader.ReadUInt16();

        var maxp = Require(data, tables, "maxp");
        var maxpReader = new SfntReader(maxp.Span) { Position = 4 };
        var numGlyphs = maxpReader.ReadUInt16();

        if (numberOfHMetrics == 0 || numberOfHMetrics > numGlyphs)
        {
            throw new FontFormatException($"numberOfHMetrics {numberOfHMetrics} is invalid for {numGlyphs} glyphs.");
        }

        _ = Require(data, tables, "hmtx");
        _ = Require(data, tables, "loca");
        _ = Require(data, tables, "glyf");

        // Optional tables: absence degrades gracefully instead of failing the parse.
        var cmap = tables.TryGetValue("cmap", out var cmapEntry)
            ? CmapSubtable.SelectBest(data.Slice(cmapEntry.Offset, cmapEntry.Length))
            : null;

        var postScriptName = tables.TryGetValue("name", out var nameEntry)
            ? ReadPostScriptName(data.Slice(nameEntry.Offset, nameEntry.Length))
            : "EmbeddedFont";

        double italicAngle = 0;
        if (tables.TryGetValue("post", out var postEntry) && postEntry.Item2 >= 8)
        {
            var postReader = new SfntReader(data.Span.Slice(postEntry.Item1, postEntry.Item2)) { Position = 4 };
            italicAngle = postReader.ReadInt32() / 65536.0;
        }

        ushort weightClass = 400;
        short capHeight = 0;
        if (tables.TryGetValue("OS/2", out var os2Entry) && os2Entry.Item2 >= 6)
        {
            var os2Reader = new SfntReader(data.Span.Slice(os2Entry.Item1, os2Entry.Item2));
            var os2Version = os2Reader.ReadUInt16();
            os2Reader.Skip(2); // xAvgCharWidth
            weightClass = os2Reader.ReadUInt16();
            if (os2Version >= 2 && os2Entry.Item2 >= 90)
            {
                os2Reader.Position = 88;
                capHeight = os2Reader.ReadInt16();
            }
        }

        if (capHeight == 0)
        {
            capHeight = ascender;
        }

        return new SfntFont(
            data,
            tables,
            numberOfHMetrics,
            cmap,
            unitsPerEm,
            indexToLocFormat,
            xMin,
            yMin,
            xMax,
            yMax,
            ascender,
            descender,
            numGlyphs,
            postScriptName,
            italicAngle,
            weightClass,
            capHeight);
    }

    public bool TryGetTable(string tag, out ReadOnlyMemory<byte> table)
    {
        if (_tables.TryGetValue(tag, out var entry))
        {
            table = _file.Slice(entry.Offset, entry.Length);
            return true;
        }

        table = default;
        return false;
    }

    /// <summary>Maps a Unicode codepoint to a glyph ID; 0 (.notdef) when unmapped or when the font has no cmap.</summary>
    public ushort MapCodepoint(int codepoint) => _cmap?.Map(codepoint) ?? 0;

    /// <summary>Advance width in font units.</summary>
    public ushort AdvanceWidth(ushort glyphId)
    {
        _ = TryGetTable("hmtx", out var hmtx);
        var index = glyphId < _numberOfHMetrics ? glyphId : _numberOfHMetrics - 1;
        var reader = new SfntReader(hmtx.Span) { Position = index * 4 };
        return reader.ReadUInt16();
    }

    /// <summary>Raw 'glyf' entry for a glyph; empty for glyphs with no outline.</summary>
    public ReadOnlyMemory<byte> GetGlyphData(ushort glyphId)
    {
        if (glyphId >= NumGlyphs)
        {
            throw new FontFormatException($"Glyph {glyphId} is out of range; the font has {NumGlyphs} glyphs.");
        }

        _ = TryGetTable("loca", out var loca);
        _ = TryGetTable("glyf", out var glyf);

        long start, end;
        if (IndexToLocFormat == 0)
        {
            var reader = new SfntReader(loca.Span) { Position = glyphId * 2 };
            start = reader.ReadUInt16() * 2L;
            end = reader.ReadUInt16() * 2L;
        }
        else
        {
            var reader = new SfntReader(loca.Span) { Position = glyphId * 4 };
            start = reader.ReadUInt32();
            end = reader.ReadUInt32();
        }

        if (start > end || end > glyf.Length)
        {
            throw new FontFormatException($"Glyph {glyphId} has an invalid 'loca' range [{start}, {end}).");
        }

        return glyf.Slice(checked((int)start), checked((int)(end - start)));
    }

    /// <summary>Reads a 32-bit offset/length, rejecting values that cannot index a managed buffer.</summary>
    private static int ReadOffset(ref SfntReader reader)
    {
        var value = reader.ReadUInt32();
        if (value > int.MaxValue)
        {
            throw new FontFormatException($"Offset 0x{value:X8} exceeds the supported font size.");
        }

        return (int)value;
    }

    private static ReadOnlyMemory<byte> Require(
        ReadOnlyMemory<byte> data,
        Dictionary<string, (int Offset, int Length)> tables,
        string tag)
    {
        if (!tables.TryGetValue(tag, out var entry))
        {
            throw new FontFormatException($"Required table '{tag}' is missing.");
        }

        return data.Slice(entry.Offset, entry.Length);
    }

    private static string ReadPostScriptName(ReadOnlyMemory<byte> name)
    {
        var reader = new SfntReader(name.Span);
        reader.Skip(2); // format
        var count = reader.ReadUInt16();
        var stringOffset = reader.ReadUInt16();

        for (var i = 0; i < count; i++)
        {
            var platformId = reader.ReadUInt16();
            var encodingId = reader.ReadUInt16();
            reader.Skip(2); // languageID
            var nameId = reader.ReadUInt16();
            var length = reader.ReadUInt16();
            var offset = reader.ReadUInt16();

            if (nameId != 6)
            {
                continue;
            }

            if (stringOffset + offset + length > name.Length)
            {
                continue;
            }

            var bytes = name.Span.Slice(stringOffset + offset, length);
            var decoded = (platformId, encodingId) switch
            {
                (3, 1) or (3, 10) or (0, _) => Encoding.BigEndianUnicode.GetString(bytes),
                (1, 0) => Encoding.Latin1.GetString(bytes),
                _ => null,
            };

            if (decoded is not null)
            {
                var sanitized = Sanitize(decoded);
                if (sanitized.Length > 0)
                {
                    return sanitized;
                }
            }
        }

        return "EmbeddedFont";
    }

    /// <summary>PostScript names must be printable ASCII without delimiters (used inside PDF name objects).</summary>
    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is > ' ' and < (char)0x7F and not ('[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or '/' or '%' or '#'))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
