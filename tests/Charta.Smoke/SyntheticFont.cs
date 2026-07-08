using System.Buffers.Binary;
using System.Text;
using Charta.Fonts;

namespace Charta.Smoke;

/// <summary>
/// Builds a tiny, fully valid TrueType font entirely in code so font tests are deterministic and
/// carry no licensing baggage. Layout: 4 glyphs, unitsPerEm 1000, PostScript name "ChartaTest".
///   gid 0  .notdef   empty outline, advance 500
///   gid 1  'A'       triangle contour, advance 600
///   gid 2  'B'       square contour, advance 600
///   gid 3  'C'       composite glyph referencing gid 1, advance 600
/// </summary>
internal static class SyntheticFont
{
    public static byte[] Build()
    {
        var glyf = BuildGlyf(out var locaOffsets);

        var loca = new BigEndianBuilder();
        foreach (var offset in locaOffsets)
        {
            loca.U16((ushort)(offset / 2));
        }

        return SfntAssembler.Assemble(
        [
            ("head", BuildHead()),
            ("hhea", BuildHhea()),
            ("maxp", BuildMaxp()),
            ("hmtx", BuildHmtx()),
            ("cmap", BuildCmap()),
            ("loca", loca.ToArray()),
            ("glyf", glyf),
            ("name", BuildName()),
            ("post", BuildPost()),
        ]);
    }

    private static byte[] BuildGlyf(out int[] locaOffsets)
    {
        var glyf = new BigEndianBuilder();
        locaOffsets = new int[5];

        locaOffsets[0] = glyf.Length; // gid 0: empty
        locaOffsets[1] = glyf.Length;

        // gid 1 'A': triangle (50,0) (300,700) (550,0)
        glyf.S16(1);                       // numberOfContours
        glyf.S16(50).S16(0).S16(550).S16(700); // bbox
        glyf.U16(2);                       // endPtsOfContours
        glyf.U16(0);                       // instructionLength
        glyf.U8(0x01).U8(0x01).U8(0x01);   // flags: on-curve, 16-bit coords
        glyf.S16(50).S16(250).S16(250);    // x deltas
        glyf.S16(0).S16(700).S16(-700);    // y deltas
        glyf.Pad4();
        locaOffsets[2] = glyf.Length;

        // gid 2 'B': square (50,0) (550,0) (550,700) (50,700)
        glyf.S16(1);
        glyf.S16(50).S16(0).S16(550).S16(700);
        glyf.U16(3);
        glyf.U16(0);
        glyf.U8(0x01).U8(0x01).U8(0x01).U8(0x01);
        glyf.S16(50).S16(500).S16(0).S16(-500);
        glyf.S16(0).S16(0).S16(700).S16(0);
        glyf.Pad4();
        locaOffsets[3] = glyf.Length;

        // gid 3 'C': composite of gid 1 at (0,0)
        glyf.S16(-1);
        glyf.S16(50).S16(0).S16(550).S16(700);
        glyf.U16(0x0003);                  // ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES
        glyf.U16(1);                       // component glyph index
        glyf.S16(0).S16(0);                // dx, dy
        glyf.Pad4();
        locaOffsets[4] = glyf.Length;

        return glyf.ToArray();
    }

    private static byte[] BuildHead() => new BigEndianBuilder()
        .U32(0x00010000)   // version
        .U32(0x00010000)   // fontRevision
        .U32(0)            // checkSumAdjustment (patched by assembler)
        .U32(0x5F0F3CF5)   // magicNumber
        .U16(0)            // flags
        .U16(1000)         // unitsPerEm
        .U32(0).U32(0)     // created
        .U32(0).U32(0)     // modified
        .S16(50).S16(0).S16(550).S16(700) // xMin yMin xMax yMax
        .U16(0)            // macStyle
        .U16(8)            // lowestRecPPEM
        .S16(2)            // fontDirectionHint
        .S16(0)            // indexToLocFormat (short)
        .S16(0)            // glyphDataFormat
        .ToArray();

    private static byte[] BuildHhea() => new BigEndianBuilder()
        .U32(0x00010000)
        .S16(800)          // ascender
        .S16(-200)         // descender
        .S16(0)            // lineGap
        .U16(600)          // advanceWidthMax
        .S16(0).S16(0)     // minLeft/RightSideBearing
        .S16(550)          // xMaxExtent
        .S16(1).S16(0)     // caretSlope rise/run
        .S16(0)            // caretOffset
        .S16(0).S16(0).S16(0).S16(0) // reserved
        .S16(0)            // metricDataFormat
        .U16(4)            // numberOfHMetrics
        .ToArray();

    private static byte[] BuildMaxp() => new BigEndianBuilder()
        .U32(0x00010000)
        .U16(4)            // numGlyphs
        .U16(4).U16(1)     // maxPoints, maxContours
        .U16(3).U16(1)     // maxCompositePoints, maxCompositeContours
        .U16(2)            // maxZones
        .U16(0).U16(0).U16(0).U16(0).U16(0).U16(0) // twilight..sizeOfInstructions
        .U16(1).U16(1)     // maxComponentElements, maxComponentDepth
        .ToArray();

    private static byte[] BuildHmtx() => new BigEndianBuilder()
        .U16(500).S16(0)
        .U16(600).S16(50)
        .U16(600).S16(50)
        .U16(600).S16(50)
        .ToArray();

    private static byte[] BuildCmap()
    {
        // Format 4, one Windows (3,1) subtable: 0x41..0x43 → gids 1..3.
        var subtable = new BigEndianBuilder()
            .U16(4)            // format
            .U16(32)           // length
            .U16(0)            // language
            .U16(4)            // segCountX2
            .U16(4).U16(1).U16(0) // searchRange, entrySelector, rangeShift
            .U16(0x43).U16(0xFFFF) // endCode
            .U16(0)            // reservedPad
            .U16(0x41).U16(0xFFFF) // startCode
            .S16(1 - 0x41).S16(1)  // idDelta
            .U16(0).U16(0)     // idRangeOffset
            .ToArray();

        return new BigEndianBuilder()
            .U16(0)            // version
            .U16(1)            // numTables
            .U16(3).U16(1)     // platform, encoding
            .U32(12)           // subtable offset
            .Bytes(subtable)
            .ToArray();
    }

    private static byte[] BuildName()
    {
        var value = Encoding.BigEndianUnicode.GetBytes("ChartaTest");
        return new BigEndianBuilder()
            .U16(0)                    // format
            .U16(1)                    // count
            .U16(18)                   // stringOffset
            .U16(3).U16(1).U16(0x409)  // platform, encoding, language
            .U16(6)                    // nameID: PostScript name
            .U16((ushort)value.Length)
            .U16(0)                    // string offset within storage
            .Bytes(value)
            .ToArray();
    }

    private static byte[] BuildPost() => new BigEndianBuilder()
        .U32(0x00030000)
        .U32(0)            // italicAngle
        .S16(0).S16(0)     // underlinePosition, underlineThickness
        .U32(0)            // isFixedPitch
        .U32(0).U32(0).U32(0).U32(0) // memory hints
        .ToArray();
}

/// <summary>Chainable big-endian byte builder for constructing font tables in tests.</summary>
internal sealed class BigEndianBuilder
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public BigEndianBuilder U8(byte value)
    {
        _bytes.Add(value);
        return this;
    }

    public BigEndianBuilder U16(int value)
    {
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
        return this;
    }

    public BigEndianBuilder S16(int value) => U16((ushort)(short)value);

    public BigEndianBuilder U32(uint value)
    {
        _bytes.Add((byte)(value >> 24));
        _bytes.Add((byte)(value >> 16));
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
        return this;
    }

    public BigEndianBuilder Bytes(byte[] value)
    {
        _bytes.AddRange(value);
        return this;
    }

    public BigEndianBuilder Pad4()
    {
        while (_bytes.Count % 4 != 0)
        {
            _bytes.Add(0);
        }

        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}
