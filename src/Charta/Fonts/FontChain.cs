namespace Charta.Fonts;

/// <summary>
/// Per-codepoint font fallback: text is split into maximal runs, each shaped with the first font
/// in the chain whose cmap covers the run's codepoints. Codepoints no font covers go to the primary
/// font as .notdef — visible tofu plus (later) a layout diagnostic, never silent data loss.
/// </summary>
internal sealed class FontChain
{
    private readonly PdfFont[] _fonts;

    public FontChain(params PdfFont[] fonts)
    {
        if (fonts.Length == 0)
        {
            throw new ArgumentException("A font chain needs at least one font.", nameof(fonts));
        }

        _fonts = fonts;
    }

    public IReadOnlyList<PdfFont> Fonts => _fonts;

    /// <summary>Shapes left-to-right text into sequential segments, one per font switch.</summary>
    public IReadOnlyList<FontRun> Shape(string text) => ShapeRun(text, ShaperDirection.LeftToRight);

    /// <summary>
    /// Shapes a directional run into font segments in visual order. Text is in logical order; each
    /// segment is shaped in the given direction (the shaper reverses right-to-left runs), and for
    /// right-to-left the segment order itself is reversed so the whole run reads visually.
    /// </summary>
    public IReadOnlyList<FontRun> ShapeRun(string text, ShaperDirection direction)
    {
        // Split into maximal same-font segments by character offset, slicing each once — no per-rune
        // string allocation. Boundaries are found by scanning runes; the text is cut with Substring.
        var segments = new List<(PdfFont Font, int Start, int Length)>();
        var segmentStart = 0;
        PdfFont? segmentFont = null;
        var index = 0;

        while (index < text.Length)
        {
            System.Text.Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            var font = SelectFont(rune.Value);
            if (segmentFont is not null && !ReferenceEquals(font, segmentFont))
            {
                segments.Add((segmentFont, segmentStart, index - segmentStart));
                segmentStart = index;
            }

            segmentFont = font;
            index += consumed;
        }

        if (segmentFont is not null)
        {
            segments.Add((segmentFont, segmentStart, index - segmentStart));
        }

        if (direction == ShaperDirection.RightToLeft)
        {
            segments.Reverse(); // visual order of font segments within a right-to-left run
        }

        var runs = new List<FontRun>(segments.Count);
        foreach (var (font, start, length) in segments)
        {
            // Avoid copying when the single segment already spans the whole run.
            var segmentText = start == 0 && length == text.Length ? text : text.Substring(start, length);
            runs.Add(new FontRun(font, font.ShapeRun(segmentText, direction)));
        }

        return runs;
    }

    private PdfFont SelectFont(int codepoint)
    {
        foreach (var font in _fonts)
        {
            if (font.CanMap(codepoint))
            {
                return font;
            }
        }

        return _fonts[0];
    }
}

/// <summary>A run of text shaped with a single font.</summary>
internal sealed class FontRun(PdfFont font, ShapedText text)
{
    public PdfFont Font { get; } = font;

    public ShapedText Text { get; } = text;
}
