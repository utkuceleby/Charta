using Charta.Fonts;
using Charta.Layout;
using Charta.Layout.Elements;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Layout;

public class TextElementTests
{
    // Synthetic font: every mapped glyph advances 600/1000 em; space is unmapped (.notdef, 500/1000).
    // At size 10: mapped char = 6pt, space = 5pt. Line height = (800 + 200) / 1000 × 10 = 10pt.
    private static TextStyle Style(double size = 10) => new()
    {
        Fonts = new FontChain(PdfFont.Parse(SyntheticFont.Build())),
        FontSize = size,
    };

    [Fact]
    public void Measure_SingleLine_UsesNaturalWidth()
    {
        var text = new TextElement("ABC", Style());

        var result = text.Measure(LayoutTestHelpers.Constraints(200, 100));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(10, result.Size.Height, 3);
        Assert.Equal(17.6, result.Size.Width, 1); // 3 glyphs × 6pt minus the A→B kern (0.4pt at size 10)
    }

    [Fact]
    public void Measure_WrapsAtWordBoundaries()
    {
        // "AB AB AB": word 12pt (kerned: 11.6), space 5pt. Width 30 → two words per line.
        var text = new TextElement("AB AB AB", Style());

        var result = text.Measure(LayoutTestHelpers.Constraints(30, 100));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(20, result.Size.Height, 3); // two lines
    }

    [Fact]
    public void Measure_HardLineBreaks()
    {
        var text = new TextElement("AB\nAB\nAB", Style());

        var result = text.Measure(LayoutTestHelpers.Constraints(500, 100));

        Assert.Equal(30, result.Size.Height, 3);
    }

    [Fact]
    public void Pagination_DrawsLinesAcrossPages()
    {
        var text = new TextElement("AB\nAB\nAB\nAB\nAB", Style()); // 5 lines à 10pt
        var constraints = LayoutTestHelpers.Constraints(500, 25); // 2 lines per page
        var context = LayoutTestHelpers.CreateContext();

        var first = text.Measure(constraints);
        Assert.Equal(LayoutVerdict.Partial, first.Verdict);
        Assert.Equal(20, first.Size.Height, 3);
        text.Draw(context, new LayoutRect(0, 0, 500, 20));

        var second = text.Measure(constraints);
        Assert.Equal(LayoutVerdict.Partial, second.Verdict);
        text.Draw(context, new LayoutRect(0, 0, 500, 20));

        var third = text.Measure(constraints);
        Assert.Equal(LayoutVerdict.Complete, third.Verdict);
        Assert.Equal(10, third.Size.Height, 3); // one line left
        text.Draw(context, new LayoutRect(0, 0, 500, 10));

        Assert.Equal(LayoutVerdict.Complete, text.Measure(constraints).Verdict);
        Assert.Equal(0, text.Measure(constraints).Size.Height);
    }

    [Fact]
    public void Measure_NoRoomForOneLine_ReportsEmpty()
    {
        var text = new TextElement("AB", Style());

        Assert.Equal(LayoutVerdict.Empty, text.Measure(LayoutTestHelpers.Constraints(500, 5)).Verdict);
    }

    [Fact]
    public void Draw_EmitsTextOperators()
    {
        var context = LayoutTestHelpers.CreateContext();
        var text = new TextElement("AB", Style(size: 20));

        text.Measure(LayoutTestHelpers.Constraints(500, 100));
        text.Draw(context, new LayoutRect(10, 10, 500, 20));

        var content = context.GetContent();
        Assert.Contains("BT", content, StringComparison.Ordinal);
        Assert.Contains("/F1 20 Tf", content, StringComparison.Ordinal);
        // Baseline: y(10) + ascent(0.8 × 20 = 16) → PDF y = 800 - 26 = 774.
        Assert.Contains("10 774 Td", content, StringComparison.Ordinal);
        Assert.Contains("TJ", content, StringComparison.Ordinal); // A→B kern pair emits a TJ array
    }
}
