using Charta;
using Charta.Html.Css;
using Xunit;

namespace Charta.Html.Tests;

public class CssTests
{
    [Theory]
    [InlineData("#fff", 255, 255, 255)]
    [InlineData("#ff0000", 255, 0, 0)]
    [InlineData("rgb(0, 128, 0)", 0, 128, 0)]
    [InlineData("rgba(10, 20, 30, 0.5)", 10, 20, 30)]
    [InlineData("navy", 0, 0, 128)]
    public void ParsesColors(string input, byte r, byte g, byte b)
    {
        var color = CssValues.ParseColor(input);
        Assert.NotNull(color);
        Assert.Equal((r, g, b), (color!.Value.R, color.Value.G, color.Value.B));
    }

    [Fact]
    public void RejectsUnknownColor() => Assert.Null(CssValues.ParseColor("chartreuse-ish"));

    [Theory]
    [InlineData("12pt", 12, 12.0)]
    [InlineData("96px", 12, 72.0)] // 96 CSS px == 72 pt (1 inch)
    [InlineData("2em", 10, 20.0)]
    [InlineData("1in", 12, 72.0)]
    public void ParsesLengths(string input, double em, double expected)
    {
        var length = CssValues.ParseLength(input, em);
        Assert.NotNull(length);
        Assert.Equal(expected, length!.Value, 3);
    }

    [Fact]
    public void UnitlessNonZeroLengthIsRejected() => Assert.Null(CssValues.ParseLength("5", 12));

    [Fact]
    public void SelectorSpecificityOrders()
    {
        var unsupported = new List<string>();
        var rules = CssParser.Parse("p { color: red } p.big { color: green } #x { color: blue }", 0, unsupported);

        Assert.Equal(3, rules.Count);
        Assert.Empty(unsupported);
        Assert.True(rules[1].Selector.Specificity.CompareTo(rules[0].Selector.Specificity) > 0);
        Assert.True(rules[2].Selector.Specificity.CompareTo(rules[1].Selector.Specificity) > 0);
    }
}
