using System.Runtime.CompilerServices;
using Charta.Text;

namespace Charta.Fonts;

/// <summary>
/// The built-in managed shaper: one glyph per codepoint via the cmap, advances from hmtx, and GPOS
/// pair kerning between adjacent glyphs. No cursive joining or reordering — enough for Latin,
/// Cyrillic, Greek, and CJK. For right-to-left runs it mirrors codepoints and reverses clusters
/// (base plus following combining marks stay together), which is correct for non-joining scripts
/// like Hebrew. GposKerning instances are cached per font.
/// </summary>
internal sealed class SimpleTextShaper : ITextShaper
{
    private readonly ConditionalWeakTable<SfntFont, GposKerning?> _kerningCache = [];

    public bool SupportsComplexScript => false;

    public IReadOnlyList<ShaperGlyph> Shape(SfntFont font, string text, ShaperDirection direction)
    {
        var rtl = direction == ShaperDirection.RightToLeft;

        // Decode runes into glyphs, grouping base + following combining marks into clusters.
        var clusters = new List<List<ShaperGlyph>>();
        List<ShaperGlyph>? current = null;
        var charIndex = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var isMark = UnicodeBidi.GetClass(rune.Value) == BidiClass.NSM;
            var codepoint = rtl ? UnicodeBidi.GetMirror(rune.Value) : rune.Value;
            var gid = font.MapCodepoint(codepoint);
            var glyph = new ShaperGlyph(gid, 0, 0, 0, charIndex, rune.Utf16SequenceLength);

            if (current is null || !isMark)
            {
                current = [glyph];
                clusters.Add(current);
            }
            else
            {
                current.Add(glyph); // combining mark joins the current cluster
            }

            charIndex += rune.Utf16SequenceLength;
        }

        if (rtl)
        {
            clusters.Reverse(); // reverse cluster order, keep base-then-marks within each cluster
        }

        var glyphs = new List<ShaperGlyph>(text.Length);
        foreach (var cluster in clusters)
        {
            glyphs.AddRange(cluster);
        }

        // GPOS kerning between visually adjacent glyphs (delta applied to the left glyph).
        var kerning = GetKerning(font);
        if (kerning is not null)
        {
            for (var i = 0; i + 1 < glyphs.Count; i++)
            {
                var adjustment = kerning.GetAdjustment(glyphs[i].GlyphId, glyphs[i + 1].GlyphId);
                if (adjustment != 0)
                {
                    glyphs[i] = glyphs[i] with { AdvanceDelta = adjustment };
                }
            }
        }

        return glyphs;
    }

    private GposKerning? GetKerning(SfntFont font)
    {
        if (_kerningCache.TryGetValue(font, out var cached))
        {
            return cached;
        }

        var kerning = GposKerning.TryCreate(font);
        _kerningCache.AddOrUpdate(font, kerning);
        return kerning;
    }
}
