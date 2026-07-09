using System.Collections.Concurrent;
using Charta.Fonts;
using HarfBuzzSharp;

namespace Charta.Shaping.HarfBuzz;

/// <summary>
/// Full OpenType shaping via HarfBuzz: cursive joining and contextual forms (Arabic, Syriac),
/// reordering (Indic), ligatures, and mark positioning. Fonts are set to their em scale so advances
/// and offsets come back in font units, matching Charta's glyph model. HarfBuzz objects are cached
/// per font since parsing and face creation are the expensive parts.
/// </summary>
internal sealed class HarfBuzzShaper : ITextShaper
{
    private readonly ConcurrentDictionary<SfntFont, HarfBuzzFont> _fonts = new();

    public bool SupportsComplexScript => true;

    public IReadOnlyList<ShaperGlyph> Shape(SfntFont font, string text, ShaperDirection direction)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var hb = _fonts.GetOrAdd(font, static f => new HarfBuzzFont(f));

        using var buffer = new global::HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties(); // script + language from the text
        buffer.Direction = direction == ShaperDirection.RightToLeft ? Direction.RightToLeft : Direction.LeftToRight;

        hb.Font.Shape(buffer);

        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var count = infos.Length;
        var glyphs = new ShaperGlyph[count];

        for (var i = 0; i < count; i++)
        {
            var gid = (ushort)infos[i].Codepoint; // after shaping, Codepoint is the glyph id
            var clusterStart = (int)infos[i].Cluster;
            var clusterLength = ClusterLength(infos, i, direction, text.Length, clusterStart);

            var advance = positions[i].XAdvance;
            var natural = font.AdvanceWidth(gid);
            glyphs[i] = new ShaperGlyph(
                gid,
                advance - natural,
                positions[i].XOffset,
                positions[i].YOffset,
                clusterStart,
                clusterLength);
        }

        return glyphs;
    }

    /// <summary>Source-text length of a glyph's cluster, derived from neighboring cluster values.</summary>
    private static int ClusterLength(GlyphInfo[] infos, int index, ShaperDirection direction, int textLength, int clusterStart)
    {
        var cluster = infos[index].Cluster;

        // Find the nearest different cluster value in the reading direction to bound this cluster.
        // Output is visual order: for RTL, cluster values decrease as the index increases.
        if (direction == ShaperDirection.RightToLeft)
        {
            for (var j = index - 1; j >= 0; j--)
            {
                if (infos[j].Cluster != cluster)
                {
                    return (int)infos[j].Cluster - clusterStart;
                }
            }
        }
        else
        {
            for (var j = index + 1; j < infos.Length; j++)
            {
                if (infos[j].Cluster != cluster)
                {
                    return (int)infos[j].Cluster - clusterStart;
                }
            }
        }

        return textLength - clusterStart;
    }

    /// <summary>
    /// A parsed HarfBuzz font scaled to font units. Cached for the (process-long) lifetime of its
    /// SfntFont; the native handles are disposed only when the cache entry is evicted or the process
    /// ends, which is why the cache is keyed by the already-shared SfntFont instance.
    /// </summary>
    private sealed class HarfBuzzFont : IDisposable
    {
        private readonly Blob _blob;
        private readonly Face _face;

        public global::HarfBuzzSharp.Font Font { get; }

        public HarfBuzzFont(SfntFont font)
        {
            var data = font.RawData.ToArray();
            _blob = Blob.FromStream(new MemoryStream(data, writable: false));
            _face = new Face(_blob, 0);
            _face.UnitsPerEm = font.UnitsPerEm;
            Font = new global::HarfBuzzSharp.Font(_face);
            Font.SetScale(font.UnitsPerEm, font.UnitsPerEm); // advances/offsets in font units
            Font.SetFunctionsOpenType();
        }

        public void Dispose()
        {
            Font.Dispose();
            _face.Dispose();
            _blob.Dispose();
        }
    }
}
