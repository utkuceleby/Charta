namespace Charta.Fonts;

/// <summary>
/// Computes the transitive closure of glyph IDs over composite-glyph components. Skipping this traversal
/// is the classic subsetting bug: a subset that keeps a composite but drops its components renders blanks.
/// </summary>
internal static class GlyphClosure
{
    private const ushort ArgsAreWords = 0x0001;
    private const ushort WeHaveAScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveXAndYScale = 0x0040;
    private const ushort WeHaveATwoByTwo = 0x0080;

    public static SortedSet<ushort> Compute(SfntFont font, IEnumerable<ushort> glyphIds)
    {
        var closure = new SortedSet<ushort>();
        var pending = new Stack<ushort>();
        pending.Push(0); // .notdef is always retained
        foreach (var gid in glyphIds)
        {
            pending.Push(gid);
        }

        while (pending.Count > 0)
        {
            var gid = pending.Pop();
            if (gid >= font.NumGlyphs || !closure.Add(gid))
            {
                continue;
            }

            var data = font.GetGlyphData(gid);
            if (data.Length == 0)
            {
                continue;
            }

            var reader = new SfntReader(data.Span);
            var numberOfContours = reader.ReadInt16();
            if (numberOfContours >= 0)
            {
                continue;
            }

            reader.Skip(8); // bounding box
            while (true)
            {
                var flags = reader.ReadUInt16();
                pending.Push(reader.ReadUInt16());

                reader.Skip((flags & ArgsAreWords) != 0 ? 4 : 2);
                if ((flags & WeHaveAScale) != 0)
                {
                    reader.Skip(2);
                }
                else if ((flags & WeHaveXAndYScale) != 0)
                {
                    reader.Skip(4);
                }
                else if ((flags & WeHaveATwoByTwo) != 0)
                {
                    reader.Skip(8);
                }

                if ((flags & MoreComponents) == 0)
                {
                    break;
                }
            }
        }

        return closure;
    }
}
