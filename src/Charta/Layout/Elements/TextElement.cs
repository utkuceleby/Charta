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
        if (!_complexScriptReported && ScriptSupport.ContainsComplexScript(_fullText))
        {
            _complexScriptReported = true;
            context.AddDiagnostic(
                nameof(TextElement),
                "The text contains a script that needs complex shaping or right-to-left layout " +
                "(Arabic, Hebrew, Indic, …). Without the shaping add-on it renders unjoined and " +
                "without bidi reordering — both the visual output and text extraction will be " +
                "incorrect for this run.");
        }

        var lines = BuildLines(bounds.Width);
        var y = bounds.Y;
        while (_nextLine < lines.Count)
        {
            var line = lines[_nextLine];
            if (y + line.Height > bounds.Y + bounds.Height + 0.01)
            {
                break;
            }

            DrawLine(context, line, bounds, y);
            y += line.Height;
            _nextLine++;
        }
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
            var runWidth = run.Text.Width * run.Style.FontSize / 1000;
            context.DrawText(run.Font, run.Text, x, baseline, run.Style.FontSize, run.Style.Color);

            if (run.Style.Underline)
            {
                var thickness = run.Style.FontSize * 0.06;
                context.FillRect(new LayoutRect(x, baseline + run.Style.FontSize * 0.08, runWidth, thickness), run.Style.Color);
            }

            if (run.Style.Strikethrough)
            {
                var thickness = run.Style.FontSize * 0.06;
                context.FillRect(new LayoutRect(x, baseline - run.Style.FontSize * 0.30, runWidth, thickness), run.Style.Color);
            }

            x += runWidth;
        }
    }

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
                width += run.Text.Width * style.FontSize / 1000;
            }
        }

        return width;
    }

    private TextLine ShapeLine(int start, int end, bool endsParagraph)
    {
        end = TrimEnd(start, end, trimSpaces: true);

        var runs = new List<StyledRun>();
        var width = 0.0;
        var ascent = 0.0;
        var height = 0.0;

        foreach (var (span, style, sliceStart, sliceEnd) in Slices(start, end))
        {
            foreach (var fontRun in style.Fonts.Shape(span[sliceStart..sliceEnd]))
            {
                runs.Add(new StyledRun(fontRun.Font, fontRun.Text, style));
                width += fontRun.Text.Width * style.FontSize / 1000;
            }
        }

        // Line metrics: tallest span wins. Empty lines use the first span's style.
        var metricStyles = runs.Count > 0
            ? runs.Select(r => r.Style)
            : _spans.Take(1).Select(s => s.Style);
        foreach (var style in metricStyles)
        {
            var primary = style.Fonts.Fonts[0];
            ascent = Math.Max(ascent, primary.AscentRatio * style.FontSize);
            height = Math.Max(height, (primary.AscentRatio - primary.DescentRatio) * style.FontSize * style.LineSpacing);
        }

        return new TextLine(runs, width, ascent, height, endsParagraph);
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
