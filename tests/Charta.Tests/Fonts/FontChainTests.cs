using Charta.Fonts;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

public class FontChainTests
{
    [Fact]
    public void Shape_SplitsRunsAtFontBoundaries()
    {
        var primary = PdfFont.Parse(SyntheticFont.Build('A'));   // covers A, B, C
        var fallback = PdfFont.Parse(SyntheticFont.Build('X'));  // covers X, Y, Z
        var chain = new FontChain(primary, fallback);

        var runs = chain.Shape("ABXYC");

        Assert.Equal(3, runs.Count);
        Assert.Same(primary, runs[0].Font);
        Assert.Equal("<00010002>", runs[0].Text.ToHexString()); // A, B
        Assert.Same(fallback, runs[1].Font);
        Assert.Equal("<00010002>", runs[1].Text.ToHexString()); // X, Y in the fallback font
        Assert.Same(primary, runs[2].Font);
        Assert.Equal("<0003>", runs[2].Text.ToHexString());     // C
    }

    [Fact]
    public void Shape_UncoveredCodepointFallsBackToPrimaryNotdef()
    {
        var chain = new FontChain(PdfFont.Parse(SyntheticFont.Build('A')), PdfFont.Parse(SyntheticFont.Build('X')));

        var runs = chain.Shape("A!B");

        var run = Assert.Single(runs); // '!' resolves to the primary font too, so no split occurs
        Assert.Equal("<000100000002>", run.Text.ToHexString()); // gid 0 tofu in the middle
    }

    [Fact]
    public void Shape_EmptyTextYieldsNoRuns()
    {
        var chain = new FontChain(PdfFont.Parse(SyntheticFont.Build()));

        Assert.Empty(chain.Shape(string.Empty));
    }

    [Fact]
    public void Constructor_RejectsEmptyChain()
    {
        Assert.Throws<ArgumentException>(() => new FontChain());
    }
}
