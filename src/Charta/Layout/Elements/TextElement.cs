using Charta.Fonts;
using Charta.Text;

namespace Charta.Layout.Elements;

/// <summary>Visual styling for a text block.</summary>
internal sealed class TextStyle
{
    public required FontChain Fonts { get; init; }

    public double FontSize { get; init; } = 12;

    public LayoutColor Color { get; init; } = LayoutColor.Black;

    /// <summary>Multiplier over the font's natural line height (ascent − descent).</summary>
    public double LineSpacing { get; init; } = 1.0;
}

/// <summary>
/// A paragraph block. Break opportunities come from the UAX#14 line breaker (mandatory breaks — LF,
/// paragraph separators — force new lines); lines are filled greedily, and spaces before a chosen
/// break carry no width, per UAX#14. The pagination cursor is the next undrawn line. A segment wider
/// than the line gets a line of its own and may overflow horizontally.
/// </summary>
internal sealed class TextElement(string text, TextStyle style) : Element
{
    private List<TextLine>? _lines;
    private double _linesWidth = double.NaN;
    private int _nextLine;

    private double LineHeight
    {
        get
        {
            var primary = style.Fonts.Fonts[0];
            return (primary.AscentRatio - primary.DescentRatio) * style.FontSize * style.LineSpacing;
        }
    }

    private double Ascent => style.Fonts.Fonts[0].AscentRatio * style.FontSize;

    public override MeasureResult Measure(in LayoutConstraints constraints)
    {
        var lines = BuildLines(constraints.AvailableWidth);
        var remaining = lines.Count - _nextLine;
        if (remaining == 0)
        {
            return MeasureResult.Complete(0, 0);
        }

        var lineHeight = LineHeight;
        var fit = double.IsInfinity(constraints.AvailableHeight)
            ? remaining
            : Math.Min(remaining, (int)(constraints.AvailableHeight / lineHeight));
        if (fit == 0)
        {
            return MeasureResult.Empty;
        }

        var width = 0.0;
        for (var i = _nextLine; i < _nextLine + fit; i++)
        {
            width = Math.Max(width, lines[i].Width);
        }

        return fit == remaining
            ? MeasureResult.Complete(width, fit * lineHeight)
            : MeasureResult.Partial(width, fit * lineHeight);
    }

    private bool _complexScriptReported;

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
        if (!_complexScriptReported && ScriptSupport.ContainsComplexScript(text))
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
        var lineHeight = LineHeight;
        var fit = Math.Min(lines.Count - _nextLine, Math.Max(0, (int)((bounds.Height + 0.01) / lineHeight)));

        for (var i = 0; i < fit; i++)
        {
            var line = lines[_nextLine + i];
            var x = bounds.X;
            var baseline = bounds.Y + i * lineHeight + Ascent;
            foreach (var run in line.Runs)
            {
                context.DrawText(run.Font, run.Text, x, baseline, style.FontSize, style.Color);
                x += run.Text.Width * style.FontSize / 1000;
            }
        }

        _nextLine += fit;
    }

    private static readonly char[] LineTerminators = ['\n', '\r', '\v', '\f', '\u0085', '\u2028', '\u2029'];

    private List<TextLine> BuildLines(double availableWidth)
    {
        if (_lines is not null && _linesWidth.Equals(availableWidth))
        {
            return _lines;
        }

        var lines = new List<TextLine>();
        var lineStart = 0;
        var lineEnd = 0;       // exclusive end of accepted segments
        var lineFullWidth = 0.0; // width including trailing spaces of accepted segments
        var segmentStart = 0;
        var spaceWidth = MeasureWidth(" ");

        foreach (var (position, mandatory) in LineBreaker.FindBreaks(text))
        {
            var segment = text[segmentStart..position];
            var visible = segment.TrimEnd(LineTerminators);
            var trimmed = visible.TrimEnd(' ');
            var fittingWidth = MeasureWidth(trimmed); // spaces before a break are free
            var fullWidth = fittingWidth + (visible.Length - trimmed.Length) * spaceWidth;

            if (lineEnd > lineStart && lineFullWidth + fittingWidth > availableWidth)
            {
                lines.Add(ShapeLine(lineStart, lineEnd));
                lineStart = segmentStart;
                lineFullWidth = 0;
            }

            lineEnd = position;
            lineFullWidth += fullWidth;
            segmentStart = position;

            if (mandatory)
            {
                lines.Add(ShapeLine(lineStart, lineEnd));
                lineStart = position;
                lineFullWidth = 0;
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(TextLine.Empty);
        }

        _lines = lines;
        _linesWidth = availableWidth;
        _nextLine = 0;
        return lines;
    }

    private double MeasureWidth(string fragment)
    {
        if (fragment.Length == 0)
        {
            return 0;
        }

        var width = 0.0;
        foreach (var run in style.Fonts.Shape(fragment))
        {
            width += run.Text.Width;
        }

        return width * style.FontSize / 1000;
    }

    private TextLine ShapeLine(int start, int end)
    {
        var content = text[start..end].TrimEnd(LineTerminators).TrimEnd(' ');
        if (content.Length == 0)
        {
            return TextLine.Empty;
        }

        var runs = style.Fonts.Shape(content);
        var width = 0.0;
        foreach (var run in runs)
        {
            width += run.Text.Width;
        }

        return new TextLine(runs, width * style.FontSize / 1000);
    }

    private sealed class TextLine(IReadOnlyList<FontRun> runs, double width)
    {
        public static readonly TextLine Empty = new([], 0);

        public IReadOnlyList<FontRun> Runs { get; } = runs;

        public double Width { get; } = width;
    }
}
