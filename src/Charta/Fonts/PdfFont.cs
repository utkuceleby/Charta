using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charta.Cos;

namespace Charta.Fonts;

/// <summary>
/// A font prepared for PDF embedding. Tracks which glyphs the document uses, then writes a subset
/// Type0/Identity-H composite font (ISO 32000-2 §9.7): fonts are always embedded and always composite —
/// a single code path that PDF/A and PDF/UA both accept.
/// </summary>
internal sealed class PdfFont
{
    private readonly SfntFont _font;
    private readonly SortedSet<ushort> _usedGlyphs = [0];
    private readonly SortedDictionary<ushort, string> _glyphText = [];
    private GposKerning? _kerning;
    private bool _kerningResolved;
    private bool _written;

    private PdfFont(SfntFont font) => _font = font;

    public static PdfFont Parse(ReadOnlyMemory<byte> fontData, int ttcIndex = 0) =>
        new(SfntFont.Parse(fontData, ttcIndex));

    /// <summary>Wraps an already-parsed font: the parse is shared, the usage tracking is per document.</summary>
    public static PdfFont FromParsed(SfntFont font) => new(font);

    public int UnitsPerEm => _font.UnitsPerEm;

    /// <summary>Typographic ascent as a fraction of the em size.</summary>
    public double AscentRatio => (double)_font.Ascender / _font.UnitsPerEm;

    /// <summary>Typographic descent as a fraction of the em size (negative below the baseline).</summary>
    public double DescentRatio => (double)_font.Descender / _font.UnitsPerEm;

    /// <summary>True when the font's cmap covers the codepoint (used by fallback chains).</summary>
    public bool CanMap(int codepoint) => _font.MapCodepoint(codepoint) != 0;

    /// <summary>Glyph ID of the space character (0 when unmapped) — the stretch point for justification.</summary>
    public ushort SpaceGlyphId => _font.MapCodepoint(' ');

    /// <summary>
    /// Simple shaping: one glyph per codepoint via cmap, advances from hmtx, pair kerning from GPOS.
    /// No substitution or bidi — those arrive with the shaping layer. Records glyph usage for subsetting.
    /// </summary>
    public ShapedText Shape(string text)
    {
        if (_written)
        {
            throw new InvalidOperationException("The font has already been written; no further text can be shaped with it.");
        }

        if (!_kerningResolved)
        {
            _kerning = GposKerning.TryCreate(_font);
            _kerningResolved = true;
        }

        var glyphs = new List<ShapedGlyph>(text.Length);
        long widthFontUnits = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var gid = _font.MapCodepoint(rune.Value);
            if (glyphs.Count > 0 && _kerning is not null)
            {
                var kern = _kerning.GetAdjustment(glyphs[^1].GlyphId, gid);
                if (kern != 0)
                {
                    glyphs[^1] = glyphs[^1] with { KernAfter = kern };
                    widthFontUnits += kern;
                }
            }

            glyphs.Add(new ShapedGlyph(gid, 0));
            widthFontUnits += _font.AdvanceWidth(gid);
            _usedGlyphs.Add(gid);
            if (gid != 0)
            {
                _glyphText.TryAdd(gid, rune.ToString());
            }
        }

        return new ShapedText(glyphs, widthFontUnits * 1000.0 / _font.UnitsPerEm, _font.UnitsPerEm);
    }

    /// <summary>Writes the full font object graph; <paramref name="fontReference"/> becomes the Type0 font.</summary>
    public void Write(PdfWriter writer, CosReference fontReference)
    {
        _written = true;

        var subset = TrueTypeSubsetter.CreateSubset(_font, GlyphClosure.Compute(_font, _usedGlyphs));
        var baseFont = CosName.Get(SubsetTag(subset) + "+" + _font.PostScriptName);

        var cidFontRef = writer.Allocate();
        var descriptorRef = writer.Allocate();
        var fontFileRef = writer.Allocate();
        var toUnicodeRef = writer.Allocate();

        var fontFile = new CosStream(subset);
        fontFile.Dictionary[CosNames.Length1] = new CosInteger(subset.Length);
        writer.WriteObject(fontFileRef, fontFile);

        var scale = 1000.0 / _font.UnitsPerEm;
        var descriptor = new CosDictionary
        {
            [CosNames.Type] = CosNames.FontDescriptor,
            [CosNames.FontName] = baseFont,
            // Symbolic: the font's built-in encoding is used (standard for Identity-H CID fonts).
            [CosNames.Flags] = new CosInteger(4),
            [CosNames.FontBBox] = CosArray.OfIntegers(
                Scaled(_font.XMin), Scaled(_font.YMin), Scaled(_font.XMax), Scaled(_font.YMax)),
            [CosNames.ItalicAngle] = new CosReal(_font.ItalicAngle),
            [CosNames.Ascent] = new CosInteger(Scaled(_font.Ascender)),
            [CosNames.Descent] = new CosInteger(Scaled(_font.Descender)),
            [CosNames.CapHeight] = new CosInteger(Scaled(_font.CapHeight)),
            [CosNames.StemV] = new CosInteger(EstimateStemV(_font.WeightClass)),
            [CosNames.FontFile2] = fontFileRef,
        };
        writer.WriteObject(descriptorRef, descriptor);

        var cidSystemInfo = new CosDictionary
        {
            [CosNames.Registry] = CosString.FromAscii("Adobe"),
            [CosNames.Ordering] = CosString.FromAscii("Identity"),
            [CosNames.Supplement] = new CosInteger(0),
        };
        var cidFont = new CosDictionary
        {
            [CosNames.Type] = CosNames.Font,
            [CosNames.Subtype] = CosNames.CidFontType2,
            [CosNames.BaseFont] = baseFont,
            [CosNames.CidSystemInfo] = cidSystemInfo,
            [CosNames.FontDescriptor] = descriptorRef,
            [CosNames.DW] = new CosInteger(1000),
            [CosNames.W] = BuildWidths(scale),
            [CosNames.CidToGidMap] = CosNames.Identity,
        };
        writer.WriteObject(cidFontRef, cidFont);

        writer.WriteObject(toUnicodeRef, new CosStream(ToUnicodeCmap.Build(_glyphText)));

        var type0 = new CosDictionary
        {
            [CosNames.Type] = CosNames.Font,
            [CosNames.Subtype] = CosNames.Type0,
            [CosNames.BaseFont] = baseFont,
            [CosNames.Encoding] = CosNames.IdentityH,
            [CosNames.DescendantFonts] = new CosArray(cidFontRef),
            [CosNames.ToUnicode] = toUnicodeRef,
        };
        writer.WriteObject(fontReference, type0);

        int Scaled(int fontUnits) => (int)Math.Round(fontUnits * scale);
    }

    /// <summary>W array with runs of consecutive glyph IDs: [ first [w w ...] first [w] ... ].</summary>
    private CosArray BuildWidths(double scale)
    {
        var w = new CosArray();
        var gids = _usedGlyphs.ToList();
        var i = 0;
        while (i < gids.Count)
        {
            var runStart = i;
            while (i + 1 < gids.Count && gids[i + 1] == gids[i] + 1)
            {
                i++;
            }

            w.Add(new CosInteger(gids[runStart]));
            var widths = new CosArray();
            for (var j = runStart; j <= i; j++)
            {
                widths.Add(new CosInteger((int)Math.Round(_font.AdvanceWidth(gids[j]) * scale)));
            }

            w.Add(widths);
            i++;
        }

        return w;
    }

    /// <summary>Six uppercase letters derived from the subset bytes — unique per subset yet deterministic.</summary>
    private static string SubsetTag(byte[] subset)
    {
        Span<byte> hash = stackalloc byte[32];
        _ = SHA256.HashData(subset, hash);
        Span<char> tag = stackalloc char[6];
        for (var i = 0; i < 6; i++)
        {
            tag[i] = (char)('A' + hash[i] % 26);
        }

        return new string(tag);
    }

    /// <summary>Common heuristic: no free font exposes real stem widths, so estimate from the weight class.</summary>
    private static int EstimateStemV(ushort weightClass) => 10 + 220 * (Math.Clamp(weightClass, (ushort)100, (ushort)900) - 50) / 900;
}

/// <summary>One positioned glyph: its ID and the kerning adjustment (font units) applied after it.</summary>
internal readonly record struct ShapedGlyph(ushort GlyphId, int KernAfter);

/// <summary>Result of shaping a text run: glyphs in visual order and the total advance in 1/1000 em.</summary>
internal sealed class ShapedText(IReadOnlyList<ShapedGlyph> glyphs, double width, int unitsPerEm)
{
    public IReadOnlyList<ShapedGlyph> Glyphs { get; } = glyphs;

    public double Width { get; } = width;

    /// <summary>Number of occurrences of the given glyph (used to count stretchable spaces).</summary>
    public int CountGlyph(ushort glyphId)
    {
        var count = 0;
        foreach (var glyph in Glyphs)
        {
            if (glyph.GlyphId == glyphId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// A copy with extra advance after every occurrence of <paramref name="spaceGlyph"/> —
    /// justification for composite fonts, where the PDF word-spacing operator does not apply.
    /// </summary>
    public ShapedText WithExtraSpaceAdvance(ushort spaceGlyph, double extraFontUnitsPerSpace)
    {
        var adjusted = new ShapedGlyph[Glyphs.Count];
        var added = 0.0;
        for (var i = 0; i < Glyphs.Count; i++)
        {
            var glyph = Glyphs[i];
            if (glyph.GlyphId == spaceGlyph)
            {
                var extra = (int)Math.Round(extraFontUnitsPerSpace);
                glyph = glyph with { KernAfter = glyph.KernAfter + extra };
                added += extra;
            }

            adjusted[i] = glyph;
        }

        return new ShapedText(adjusted, Width + added * 1000.0 / unitsPerEm, unitsPerEm);
    }

    /// <summary>Identity-H character codes as a PDF hex string, e.g. &lt;00010002&gt;.</summary>
    public string ToHexString()
    {
        var sb = new StringBuilder(Glyphs.Count * 4 + 2);
        sb.Append('<');
        foreach (var glyph in Glyphs)
        {
            sb.Append(glyph.GlyphId.ToString("X4", CultureInfo.InvariantCulture));
        }

        sb.Append('>');
        return sb.ToString();
    }

    /// <summary>
    /// The text-showing operator: plain Tj when unkerned, otherwise a TJ array whose numbers are the
    /// kerning adjustments in thousandths of an em (TJ subtracts, so the sign flips).
    /// </summary>
    public string ToTextOperator()
    {
        var kerned = false;
        foreach (var glyph in Glyphs)
        {
            if (glyph.KernAfter != 0)
            {
                kerned = true;
                break;
            }
        }

        if (!kerned)
        {
            return ToHexString() + " Tj";
        }

        var sb = new StringBuilder(Glyphs.Count * 5 + 8);
        sb.Append("[<");
        foreach (var glyph in Glyphs)
        {
            sb.Append(glyph.GlyphId.ToString("X4", CultureInfo.InvariantCulture));
            if (glyph.KernAfter != 0)
            {
                var adjustment = (int)Math.Round(-glyph.KernAfter * 1000.0 / unitsPerEm);
                sb.Append("> ").Append(adjustment.ToString(CultureInfo.InvariantCulture)).Append(" <");
            }
        }

        sb.Append(">] TJ");
        return sb.ToString();
    }
}
