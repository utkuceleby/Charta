using System.Text;

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

    /// <summary>Shapes the text into sequential segments, one per font switch.</summary>
    public IReadOnlyList<FontRun> Shape(string text)
    {
        var runs = new List<FontRun>();
        var segment = new StringBuilder();
        PdfFont? segmentFont = null;

        foreach (var rune in text.EnumerateRunes())
        {
            var font = SelectFont(rune.Value);
            if (!ReferenceEquals(font, segmentFont) && segment.Length > 0)
            {
                runs.Add(new FontRun(segmentFont!, segmentFont!.Shape(segment.ToString())));
                segment.Clear();
            }

            segmentFont = font;
            segment.Append(rune.ToString());
        }

        if (segment.Length > 0)
        {
            runs.Add(new FontRun(segmentFont!, segmentFont!.Shape(segment.ToString())));
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
