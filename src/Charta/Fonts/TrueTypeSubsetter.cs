using System.Buffers.Binary;

namespace Charta.Fonts;

/// <summary>
/// Retain-GID TrueType subsetter: glyph IDs keep their values, unused glyphs become empty outlines
/// (they compress to almost nothing), so hhea/hmtx/maxp are copied verbatim and no GID remapping can
/// go wrong. The caller must pass a component-closed set (see <see cref="GlyphClosure"/>).
/// The subset omits 'cmap': the PDF embedding uses Identity-H, and text extraction uses ToUnicode.
/// </summary>
internal static class TrueTypeSubsetter
{
    private static readonly string[] CopiedTables = ["hhea", "maxp", "hmtx", "cvt ", "fpgm", "prep"];

    public static byte[] CreateSubset(SfntFont font, IReadOnlySet<ushort> glyphs)
    {
        var numGlyphs = font.NumGlyphs;

        // Lay out the new 'glyf': kept glyphs verbatim, everything else zero-length.
        var offsets = new int[numGlyphs + 1];
        var glyfSize = 0;
        for (ushort gid = 0; gid < numGlyphs; gid++)
        {
            offsets[gid] = glyfSize;
            if (glyphs.Contains(gid))
            {
                // 4-byte padding keeps offsets even (required by the short 'loca' format).
                glyfSize += (font.GetGlyphData(gid).Length + 3) & ~3;
            }
        }

        offsets[numGlyphs] = glyfSize;

        var glyf = new byte[glyfSize];
        foreach (var gid in glyphs)
        {
            if (gid < numGlyphs)
            {
                font.GetGlyphData(gid).Span.CopyTo(glyf.AsSpan(offsets[gid]));
            }
        }

        var useLongLoca = glyfSize > 0x1FFFE;
        var loca = new byte[(numGlyphs + 1) * (useLongLoca ? 4 : 2)];
        for (var i = 0; i <= numGlyphs; i++)
        {
            if (useLongLoca)
            {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)offsets[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(offsets[i] / 2));
            }
        }

        _ = font.TryGetTable("head", out var headOriginal);
        var head = headOriginal.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0); // checkSumAdjustment recomputed on assembly
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)(useLongLoca ? 1 : 0));

        var tables = new List<(string, byte[])>
        {
            ("head", head),
            ("loca", loca),
            ("glyf", glyf),
        };

        foreach (var tag in CopiedTables)
        {
            if (font.TryGetTable(tag, out var table))
            {
                tables.Add((tag, table.ToArray()));
            }
        }

        return SfntAssembler.Assemble(tables);
    }
}
