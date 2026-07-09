using Charta.Fonts;

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
/// A paragraph block. Lines are built greedily at word boundaries for the measured width ('\n' forces
/// a break); the pagination cursor is the next undrawn line. The word-boundary breaker is a
/// placeholder until the UAX#14 line breaker lands. A word wider than the line gets a line of its own
/// and may overflow horizontally (diagnostic planned with UAX#14 work).
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

    public override void Draw(DrawingContext context, in LayoutRect bounds)
    {
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

    private List<TextLine> BuildLines(double availableWidth)
    {
        if (_lines is not null && _linesWidth.Equals(availableWidth))
        {
            return _lines;
        }

        var lines = new List<TextLine>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(TextLine.Empty);
                continue;
            }

            var current = new List<string>();
            var currentWidth = 0.0;
            foreach (var word in words)
            {
                var wordWidth = MeasureWidth(word);
                var spaceWidth = current.Count > 0 ? MeasureWidth(" ") : 0;
                if (current.Count > 0 && currentWidth + spaceWidth + wordWidth > availableWidth)
                {
                    lines.Add(ShapeLine(current));
                    current = [];
                    currentWidth = 0;
                    spaceWidth = 0;
                }

                current.Add(word);
                currentWidth += spaceWidth + wordWidth;
            }

            if (current.Count > 0)
            {
                lines.Add(ShapeLine(current));
            }
        }

        _lines = lines;
        _linesWidth = availableWidth;
        _nextLine = 0;
        return lines;
    }

    private double MeasureWidth(string fragment)
    {
        var width = 0.0;
        foreach (var run in style.Fonts.Shape(fragment))
        {
            width += run.Text.Width;
        }

        return width * style.FontSize / 1000;
    }

    private TextLine ShapeLine(List<string> words)
    {
        var runs = style.Fonts.Shape(string.Join(' ', words));
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
