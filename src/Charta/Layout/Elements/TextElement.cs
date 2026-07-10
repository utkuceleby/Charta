using Charta.Fonts;
using Charta.Text;

namespace Charta.Layout.Elements;

/// <summary>Horizontal alignment of lines within a text block.</summary>
internal enum TextAlignment
{
    Left,
    Center,
    Right,

    /// <summary>Stretch spaces so lines fill the width; the last line of each paragraph stays left-aligned.</summary>
    Justify,
}

/// <summary>Visual styling for a span of text.</summary>
internal sealed class TextStyle
{
    public required FontChain Fonts { get; init; }

    public double FontSize { get; init; } = 12;

    public LayoutColor Color { get; init; } = LayoutColor.Black;

    /// <summary>Multiplier over the font's natural line height (ascent − descent).</summary>
    public double LineSpacing { get; init; } = 1.0;

    public bool Underline { get; init; }

    public bool Strikethrough { get; init; }

    /// <summary>Extra space between glyphs, in points (letter-spacing / tracking).</summary>
    public double LetterSpacing { get; init; }

    /// <summary>Baseline shift in points (positive raises the run — superscript; negative lowers it).</summary>
    public double BaselineShift { get; init; }
}

/// <summary>One styled fragment of a paragraph.</summary>
internal sealed class StyledSpan(string text, TextStyle style)
{
    public string Text { get; } = text;

    public TextStyle Style { get; } = style;
}

/// <summary>
/// A paragraph block of styled spans. Break opportunities come from the UAX#14 line breaker running
/// over the concatenated text (so breaks are correct across span boundaries); lines are filled
/// greedily, spaces before a chosen break carry no width, and the pagination cursor is the next
/// undrawn line. Justification stretches space glyphs via TJ adjustments — the PDF word-spacing
/// operator does not apply to composite fonts.
/// </summary>
internal sealed class TextElement : Element
{
    private static readonly char[] LineTerminators = ['\n', '\r', '\v', '\f', '\u0085', '\u2028', '\u2029'];

    private readonly IReadOnlyList<StyledSpan> _spans;
    private readonly TextAlignment _alignment;
    private readonly string _fullText;

    private List<TextLine>? _lines;
    private double _linesWidth = double.NaN;
    private int _nextLine;
    private bool _complexScriptReported;
    private Charta.Compliance.StructElement? _struct;

    /// <summary>The structure tag for accessibility: "P" (default) or "H1".."H6" for headings.</summary>
    public string TagRole { get; init; } = "P";

    // Bidi state, computed once (width-independent). Null when the text is purely left-to-right,
    // which keeps the fast path byte-identical to the pre-bidi implementation.
    private bool _bidiComputed;
    private int[] _codepoints = [];
    private int[] _charToCodepoint = [];   // length = chars + 1
    private byte[] _bidiLevels = [];
    private int[] _charStyleIndex = [];    // style span index per char
    private bool _hasRtl;

    public TextElement(string text, TextStyle style, TextAlignment alignment = TextAlignment.Left)
        : this([new StyledSpan(text, style)], alignment)
    {
    }

    public TextElement(IReadOnlyList<StyledSpan> spans, TextAlignment alignment = TextAlignment.Left)
    {
        _spans = spans;
        _alignment = alignment;
        _fullText = string.Concat(spans.Select(s => s.Text));
    }

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var lines = BuildLines(constraints.AvailableWidth);
        var remaining = lines.Count - _nextLine;
        if (remaining == 0)
        {
            return MeasureResult.Complete(0, 0);
        }

        var fit = 0;
        var height = 0.0;
        var width = 0.0;
        for (var i = _nextLine; i < lines.Count; i++)
        {
            var lineHeight = lines[i].Height;
            if (!double.IsInfinity(constraints.AvailableHeight) && height + lineHeight > constraints.AvailableHeight + 0.01)
            {
                break;
            }

            height += lineHeight;
            width = Math.Max(width, lines[i].Width);
            fit++;
        }

        if (fit == 0)
        {
            return MeasureResult.Empty;
        }

        return fit == remaining
            ? MeasureResult.Complete(width, height)
            : MeasureResult.Partial(width, height);
    }

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        if (!_complexScriptReported &&
            !Charta.Fonts.TextShaperRegistry.Current.SupportsComplexScript &&
            ScriptSupport.ContainsComplexScript(_fullText))
        {
            _complexScriptReported = true;
            context.AddDiagnostic(
                nameof(TextElement),
                "The text contains a script that needs glyph shaping (Arabic, Indic, …). Reading " +
                "order is handled (UAX#9 bidi), but letterforms render unjoined until the shaping " +
                "add-on is available.");
        }

        _struct ??= context.AddStructElement(TagRole);
        var captured = bounds;
        context.Tagged(TagRole, _struct, () =>
        {
            var lines = BuildLines(captured.Width);
            var y = captured.Y;
            while (_nextLine < lines.Count)
            {
                var line = lines[_nextLine];
                if (y + line.Height > captured.Y + captured.Height + 0.01)
                {
                    break;
                }

                DrawLine(context, line, captured, y);
                y += line.Height;
                _nextLine++;
            }
        });
    }

    private void DrawLine(DrawingContext context, TextLine line, in LayoutRect bounds, double y)
    {
        var runs = line.Runs;
        var lineWidth = line.Width;

        if (_alignment == TextAlignment.Justify && !line.EndsParagraph && bounds.Width > lineWidth)
        {
            (runs, lineWidth) = JustifyRuns(line, bounds.Width);
        }

        var x = bounds.X + _alignment switch
        {
            TextAlignment.Center => Math.Max(0, (bounds.Width - lineWidth) / 2),
            TextAlignment.Right => Math.Max(0, bounds.Width - lineWidth),
            _ => 0,
        };

        var baseline = y + line.Ascent;
        foreach (var run in runs)
        {
            var runWidth = RunWidth(run);
            var runBaseline = baseline - run.Style.BaselineShift;
            context.DrawText(run.Font, run.Text, x, runBaseline, run.Style.FontSize, run.Style.Color, run.Style.LetterSpacing);

            if (run.Style.Underline)
            {
                var thickness = run.Style.FontSize * 0.06;
                context.FillRect(new LayoutRect(x, runBaseline + run.Style.FontSize * 0.08, runWidth, thickness), run.Style.Color);
            }

            if (run.Style.Strikethrough)
            {
                var thickness = run.Style.FontSize * 0.06;
                context.FillRect(new LayoutRect(x, runBaseline - run.Style.FontSize * 0.30, runWidth, thickness), run.Style.Color);
            }

            x += runWidth;
        }
    }

    /// <summary>Rendered width of a run: shaped advance plus letter-spacing after each glyph.</summary>
    private static double RunWidth(StyledRun run) =>
        run.Text.Width * run.Style.FontSize / 1000 + run.Style.LetterSpacing * run.Text.Glyphs.Count;

    /// <summary>Distributes the missing width across the line's space glyphs.</summary>
    private static (IReadOnlyList<StyledRun> Runs, double Width) JustifyRuns(TextLine line, double targetWidth)
    {
        var spaceCount = 0;
        foreach (var run in line.Runs)
        {
            spaceCount += run.Text.CountGlyph(run.Font.SpaceGlyphId);
        }

        if (spaceCount == 0)
        {
            return (line.Runs, line.Width);
        }

        var extraPointsPerSpace = (targetWidth - line.Width) / spaceCount;
        var justified = new List<StyledRun>(line.Runs.Count);
        var width = 0.0;
        foreach (var run in line.Runs)
        {
            var extraFontUnits = extraPointsPerSpace / run.Style.FontSize * run.Font.UnitsPerEm;
            var text = run.Text.WithExtraSpaceAdvance(run.Font.SpaceGlyphId, extraFontUnits);
            justified.Add(new StyledRun(run.Font, text, run.Style));
            width += text.Width * run.Style.FontSize / 1000;
        }

        return (justified, width);
    }

    private List<TextLine> BuildLines(double availableWidth)
    {
        if (_lines is not null && _linesWidth.Equals(availableWidth))
        {
            return _lines;
        }

        var lines = new List<TextLine>();
        var lineStart = 0;
        var lineEnd = 0;
        var lineFullWidth = 0.0;
        var segmentStart = 0;

        foreach (var (position, mandatory) in LineBreaker.FindBreaks(_fullText))
        {
            var visibleEnd = TrimEnd(segmentStart, position, trimSpaces: false);
            var fittingEnd = TrimEnd(segmentStart, position, trimSpaces: true);
            var fittingWidth = MeasureRange(segmentStart, fittingEnd);
            var fullWidth = fittingWidth + MeasureRange(fittingEnd, visibleEnd);

            if (lineEnd > lineStart && lineFullWidth + fittingWidth > availableWidth)
            {
                lines.Add(ShapeLine(lineStart, lineEnd, endsParagraph: false));
                lineStart = segmentStart;
                lineFullWidth = 0;
            }

            lineEnd = position;
            lineFullWidth += fullWidth;
            segmentStart = position;

            if (mandatory)
            {
                lines.Add(ShapeLine(lineStart, lineEnd, endsParagraph: true));
                lineStart = position;
                lineFullWidth = 0;
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(ShapeLine(0, 0, endsParagraph: true));
        }

        _lines = lines;
        _linesWidth = availableWidth;
        _nextLine = 0;
        return lines;
    }

    private int TrimEnd(int start, int end, bool trimSpaces)
    {
        while (end > start)
        {
            var c = _fullText[end - 1];
            if (Array.IndexOf(LineTerminators, c) >= 0 || (trimSpaces && c == ' '))
            {
                end--;
                continue;
            }

            break;
        }

        return end;
    }

    /// <summary>Width of a character range, shaping each span slice with its own style.</summary>
    private double MeasureRange(int start, int end)
    {
        var width = 0.0;
        foreach (var (span, style, sliceStart, sliceEnd) in Slices(start, end))
        {
            foreach (var run in style.Fonts.Shape(span[sliceStart..sliceEnd]))
            {
                width += run.Text.Width * style.FontSize / 1000 + style.LetterSpacing * run.Text.Glyphs.Count;
            }
        }

        return width;
    }

    private TextLine ShapeLine(int start, int end, bool endsParagraph)
    {
        end = TrimEnd(start, end, trimSpaces: true);
        EnsureBidi();

        var runs = new List<StyledRun>();
        var ascent = 0.0;
        var height = 0.0;

        if (_hasRtl)
        {
            foreach (var (font, shaped, style) in VisualRuns(start, end))
            {
                runs.Add(new StyledRun(font, shaped, style));
            }
        }
        else
        {
            foreach (var (span, style, sliceStart, sliceEnd) in Slices(start, end))
            {
                foreach (var fontRun in style.Fonts.Shape(span[sliceStart..sliceEnd]))
                {
                    runs.Add(new StyledRun(fontRun.Font, fontRun.Text, style));
                }
            }
        }

        var width = 0.0;
        foreach (var run in runs)
        {
            width += RunWidth(run);
        }

        // Line metrics: tallest span wins. Empty lines use the first span's style.
        var metricCount = runs.Count > 0 ? runs.Count : Math.Min(1, _spans.Count);
        for (var i = 0; i < metricCount; i++)
        {
            var style = runs.Count > 0 ? runs[i].Style : _spans[i].Style;
            var primary = style.Fonts.Fonts[0];
            ascent = Math.Max(ascent, primary.AscentRatio * style.FontSize);
            height = Math.Max(height, (primary.AscentRatio - primary.DescentRatio) * style.FontSize * style.LineSpacing);
        }

        return new TextLine(runs, width, ascent, height, endsParagraph);
    }

    // ---------------------------------------------------------------- bidi (UAX#9)

    /// <summary>Computes codepoints, per-paragraph bidi levels, and per-char style indices — once.</summary>
    private void EnsureBidi()
    {
        if (_bidiComputed)
        {
            return;
        }

        _bidiComputed = true;

        var codepoints = new List<int>(_fullText.Length);
        var charToCp = new int[_fullText.Length + 1];
        var charIndex = 0;
        foreach (var rune in _fullText.EnumerateRunes())
        {
            for (var i = 0; i < rune.Utf16SequenceLength; i++)
            {
                charToCp[charIndex++] = codepoints.Count;
            }

            codepoints.Add(rune.Value);
        }

        charToCp[_fullText.Length] = codepoints.Count;

        _codepoints = [.. codepoints];
        _charToCodepoint = charToCp;

        var classes = new BidiClass[_codepoints.Length];
        _hasRtl = false;
        for (var i = 0; i < _codepoints.Length; i++)
        {
            classes[i] = UnicodeBidi.GetClass(_codepoints[i]);
            if (classes[i] is BidiClass.R or BidiClass.AL or BidiClass.RLI or BidiClass.RLO or BidiClass.RLE or BidiClass.AN)
            {
                _hasRtl = true;
            }
        }

        // Per-char style index (which span owns each char).
        _charStyleIndex = new int[_fullText.Length];
        var offset = 0;
        for (var s = 0; s < _spans.Count; s++)
        {
            for (var i = 0; i < _spans[s].Text.Length; i++)
            {
                _charStyleIndex[offset + i] = s;
            }

            offset += _spans[s].Text.Length;
        }

        if (!_hasRtl)
        {
            return;
        }

        // Levels per UBA paragraph (split at B separators), each with auto-detected direction.
        _bidiLevels = new byte[_codepoints.Length];
        var paragraphStart = 0;
        for (var i = 0; i <= _codepoints.Length; i++)
        {
            if (i < _codepoints.Length && classes[i] != BidiClass.B)
            {
                continue;
            }

            var end = Math.Min(i + 1, _codepoints.Length); // include the separator in its paragraph
            if (end > paragraphStart)
            {
                var slice = classes.AsSpan(paragraphStart, end - paragraphStart);
                var level = BidiAlgorithm.ResolveParagraphLevel(slice);
                var levels = BidiAlgorithm.ResolveLevels(slice, _codepoints.AsSpan(paragraphStart, end - paragraphStart), level);
                levels.CopyTo(_bidiLevels.AsSpan(paragraphStart));
            }

            paragraphStart = end;
        }
    }

    /// <summary>
    /// Shaped runs for one line in visual order. Text is split into maximal runs sharing a bidi level
    /// and a style (logical order), the runs are ordered visually by rule L2, and each is shaped in
    /// its direction — the shaper reverses and mirrors right-to-left runs (and joins them when the
    /// HarfBuzz add-on is present). Formatting characters (removed levels) do not render.
    /// </summary>
    private IEnumerable<(PdfFont Font, ShapedText Shaped, TextStyle Style)> VisualRuns(int start, int end)
    {
        var cpStart = _charToCodepoint[start];
        var cpEnd = _charToCodepoint[end];
        if (cpEnd <= cpStart)
        {
            yield break;
        }

        // Build logical runs: consecutive rendered codepoints sharing a level and a style.
        var runTexts = new List<string>();
        var runLevels = new List<byte>();
        var runStyles = new List<int>();
        var builder = new System.Text.StringBuilder();
        var currentLevel = (byte)0;
        var currentStyle = -1;

        void Flush()
        {
            if (builder.Length > 0)
            {
                runTexts.Add(builder.ToString());
                runLevels.Add(currentLevel);
                runStyles.Add(currentStyle);
                builder.Clear();
            }
        }

        for (var cp = cpStart; cp < cpEnd; cp++)
        {
            var level = _bidiLevels[cp];
            if (level == BidiAlgorithm.RemovedLevel)
            {
                continue;
            }

            var style = _charStyleIndex[CharIndexOf(cp)];
            if (builder.Length > 0 && (level != currentLevel || style != currentStyle))
            {
                Flush();
            }

            currentLevel = level;
            currentStyle = style;
            builder.Append(char.ConvertFromUtf32(_codepoints[cp]));
        }

        Flush();

        // L2: order the runs visually by their levels.
        var visual = BidiAlgorithm.ReorderLine([.. runLevels], 0, runLevels.Count);
        foreach (var runIndex in visual)
        {
            var style = _spans[runStyles[runIndex]].Style;
            var direction = runLevels[runIndex] % 2 == 1 ? ShaperDirection.RightToLeft : ShaperDirection.LeftToRight;
            foreach (var fontRun in style.Fonts.ShapeRun(runTexts[runIndex], direction))
            {
                yield return (fontRun.Font, fontRun.Text, style);
            }
        }
    }

    /// <summary>First char index of a codepoint (inverse of _charToCodepoint).</summary>
    private int CharIndexOf(int codepointIndex)
    {
        // Codepoints map to 1–2 chars; walk from the codepoint's position estimate.
        // _charToCodepoint is monotonic, so binary search the first char with that codepoint.
        int lo = 0, hi = _fullText.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_charToCodepoint[mid] < codepointIndex)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>Enumerates (spanText, style, sliceStart, sliceEnd) intersections of [start, end) with the spans.</summary>
    private IEnumerable<(string Text, TextStyle Style, int SliceStart, int SliceEnd)> Slices(int start, int end)
    {
        var offset = 0;
        foreach (var span in _spans)
        {
            var spanStart = offset;
            var spanEnd = offset + span.Text.Length;
            offset = spanEnd;

            var from = Math.Max(start, spanStart);
            var to = Math.Min(end, spanEnd);
            if (from < to)
            {
                yield return (span.Text, span.Style, from - spanStart, to - spanStart);
            }
        }
    }

    private sealed class TextLine(IReadOnlyList<StyledRun> runs, double width, double ascent, double height, bool endsParagraph)
    {
        public IReadOnlyList<StyledRun> Runs { get; } = runs;

        public double Width { get; } = width;

        public double Ascent { get; } = ascent;

        public double Height { get; } = height;

        public bool EndsParagraph { get; } = endsParagraph;
    }

    private sealed class StyledRun(PdfFont font, ShapedText text, TextStyle style)
    {
        public PdfFont Font { get; } = font;

        public ShapedText Text { get; } = text;

        public TextStyle Style { get; } = style;
    }
}
