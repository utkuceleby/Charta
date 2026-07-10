using AngleSharp.Html.Parser;
using Charta;
using Charta.Html.Css;
using Xunit;

namespace Charta.Html.Tests;

public class CssTests
{
    private static ComputedStyle Resolve(string html, string selector)
    {
        var document = new HtmlParser().ParseDocument(html);
        var unsupported = new List<string>();
        var rules = new List<CssRule>();
        foreach (var styleEl in document.QuerySelectorAll("style"))
        {
            rules.AddRange(CssParser.Parse(styleEl.TextContent, 0, unsupported));
        }

        var resolver = new StyleResolver(rules, unsupported.Add);
        return resolver.Resolve(document.QuerySelector(selector)!, new ComputedStyle { Display = DisplayKind.Block, FontSize = 12 });
    }

    [Fact]
    public void ResolvesFlexDisplayAndDirection()
    {
        Assert.Equal(DisplayKind.Flex, Resolve("<div style='display:flex'></div>", "div").Display);
        Assert.Equal(FlexDirection.Column, Resolve("<div style='display:flex;flex-direction:column'></div>", "div").FlexDirection);
    }

    [Fact]
    public void ResolvesFlexGrowFromShorthand()
    {
        Assert.Equal(2, Resolve("<div style='flex:2 1 0%'></div>", "div").FlexGrow);
        Assert.Equal(3, Resolve("<div style='flex-grow:3'></div>", "div").FlexGrow);
    }

    [Fact]
    public void ResolvesWhiteSpaceAndTextTransform()
    {
        Assert.Equal(WhiteSpaceKind.Pre, Resolve("<div style='white-space:pre'></div>", "div").WhiteSpace);
        Assert.Equal(TextTransformKind.Uppercase, Resolve("<div style='text-transform:uppercase'></div>", "div").TextTransform);
    }

    [Fact]
    public void PreElementDefaultsToPreservedWhitespace() =>
        Assert.Equal(WhiteSpaceKind.Pre, Resolve("<pre>x</pre>", "pre").WhiteSpace);

    [Fact]
    public void ResolvesGap() =>
        Assert.Equal(6.0, Resolve("<div style='gap:8px'></div>", "div").Gap, 3); // 8px -> 6pt

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
