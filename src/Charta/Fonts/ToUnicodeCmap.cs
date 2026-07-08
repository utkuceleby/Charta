using System.Globalization;
using System.Text;

namespace Charta.Fonts;

/// <summary>
/// Builds the ToUnicode CMap (ISO 32000-2 §9.10.3) mapping glyph IDs (the Identity-H character codes)
/// back to Unicode text so copy/paste and extraction work. One-to-many entries support ligatures later.
/// </summary>
internal static class ToUnicodeCmap
{
    private const int MaxEntriesPerBlock = 100; // CMap spec limit per begin/endbfchar block

    public static byte[] Build(IReadOnlyDictionary<ushort, string> glyphToText)
    {
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        var entries = glyphToText.OrderBy(p => p.Key).ToList();
        for (var start = 0; start < entries.Count; start += MaxEntriesPerBlock)
        {
            var block = entries.Skip(start).Take(MaxEntriesPerBlock).ToList();
            sb.Append(block.Count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            foreach (var (gid, text) in block)
            {
                sb.Append('<').Append(gid.ToString("X4", CultureInfo.InvariantCulture)).Append("> <");
                foreach (var unit in text)
                {
                    sb.Append(((ushort)unit).ToString("X4", CultureInfo.InvariantCulture));
                }

                sb.Append(">\n");
            }

            sb.Append("endbfchar\n");
        }

        sb.Append("endcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\n");
        sb.Append("end\nend\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
