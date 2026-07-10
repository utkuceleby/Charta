using System.Text;
using Charta;
using Charta.Html;
using Charta.Smoke;
using Xunit;

namespace Charta.Html.Tests;

public class HtmlRenderingTests
{
    static HtmlRenderingTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static (string Pdf, GenerationResult Result, List<string> Unsupported) Render(string html, double? contentWidth = null)
    {
        var unsupported = new List<string>();
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Html(html, new HtmlRenderOptions { OnUnsupported = unsupported.Add, ContentWidth = contentWidth })));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer, new PdfSaveOptions { Compress = false });
        return (Encoding.Latin1.GetString(buffer.ToArray()), result, unsupported);
    }

    [Fact]
    public void RendersBlocksAndInlineWithoutThrowing()
    {
        var (pdf, result, unsupported) = Render(
            "<h1>Title</h1><p>A <b>bold</b> and <i>italic</i> line.</p><hr><ul><li>one</li><li>two</li></ul>");

        Assert.True(result.PageCount >= 1);
        Assert.Empty(unsupported);
        Assert.Contains("BT", pdf, StringComparison.Ordinal); // text was drawn
        Assert.Contains(" Tf", pdf, StringComparison.Ordinal); // a font was selected
    }

    [Fact]
    public void RendersTableWithHeaderAndSpans()
    {
        var (_, result, unsupported) = Render(
            "<table><thead><tr><th>A</th><th>B</th></tr></thead>" +
            "<tbody><tr><td colspan=\"2\">wide</td></tr><tr><td>x</td><td>y</td></tr></tbody></table>");

        Assert.True(result.PageCount >= 1);
        Assert.Empty(unsupported);
    }

    [Fact]
    public void ReportsUnsupportedFeaturesInsteadOfThrowing()
    {
        var (_, _, unsupported) = Render(
            "<style>div > p { color: red } @media print { p { color: blue } }</style>" +
            "<div style=\"display:flex\">x</div>");

        Assert.Contains(unsupported, m => m.Contains("selector", StringComparison.Ordinal));
        Assert.Contains(unsupported, m => m.Contains("at-rule", StringComparison.Ordinal));
    }

    [Fact]
    public void AppliesStylesheetAndInlineCascade()
    {
        // The inline style must win over the stylesheet rule (higher priority).
        var (pdf, _, _) = Render(
            "<style>p { color: #ff0000 }</style><p style=\"color:#00ff00\">CAB</p>");

        // The green fill (0 1 0 rg) from the inline style should appear, not the red one.
        Assert.Contains("0 1 0 rg", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("1 0 0 rg", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyBodyProducesAValidDocument()
    {
        var (_, result, unsupported) = Render("<p></p>");
        Assert.Equal(1, result.PageCount);
        Assert.Empty(unsupported);
    }

    [Fact]
    public void FlexRowAndColumnRenderWithoutUnsupported()
    {
        var (_, rowResult, rowUnsupported) = Render(
            "<div style=\"display:flex;gap:8px\"><span style=\"flex:2\">CAB</span><span style=\"flex:1\">BAC</span></div>");
        Assert.True(rowResult.PageCount >= 1);
        Assert.Empty(rowUnsupported);

        var (_, colResult, colUnsupported) = Render(
            "<div style=\"display:flex;flex-direction:column\"><div>CAB</div><div>ABC</div></div>");
        Assert.True(colResult.PageCount >= 1);
        Assert.Empty(colUnsupported);
    }

    [Fact]
    public void PrePreservesNewlines()
    {
        // Each drawn text line emits one Td. A <pre> keeps the newline as a hard break (two lines);
        // normal white-space collapses it to a space (one line).
        static int Lines(string pdf) => System.Text.RegularExpressions.Regex.Count(pdf, @"\bTd\b");

        var (pre, _, _) = Render("<pre>AB\nBA</pre>");
        var (normal, _, _) = Render("<p>AB\nBA</p>");

        Assert.Equal(2, Lines(pre));
        Assert.Equal(1, Lines(normal));
    }

    [Fact]
    public void PercentWidthResolvesAgainstContentWidth()
    {
        // With a content width, a percentage width resolves to points instead of being reported.
        var (_, result, unsupported) = Render("<div style=\"width:25%\">CAB</div>", contentWidth: 400);
        Assert.True(result.PageCount >= 1);
        Assert.DoesNotContain(unsupported, m => m.Contains("percentage width", StringComparison.Ordinal));
    }

    [Fact]
    public void PercentWidthWithoutContentWidthIsReported()
    {
        var (_, _, unsupported) = Render("<div style=\"width:50%\">CAB</div>");
        Assert.Contains(unsupported, m => m.Contains("percentage width", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedPercentWidthResolves()
    {
        // Inner 50% of the outer 50% of 400pt = 100pt; both resolve, so nothing is reported.
        var (_, _, unsupported) = Render(
            "<div style=\"width:50%\"><div style=\"width:50%;background:#00ff00\">CAB</div></div>", contentWidth: 400);
        Assert.Empty(unsupported);
    }

    [Fact]
    public void FlexJustifyAndGridAndPageAndAlignItemsDiagnostics()
    {
        var (_, flexResult, flexUnsupported) = Render(
            "<div style=\"display:flex;justify-content:space-between\"><span style=\"width:40pt\">A</span><span style=\"width:40pt\">B</span></div>");
        Assert.True(flexResult.PageCount >= 1);
        Assert.Empty(flexUnsupported); // justify-content is supported, not reported

        Assert.Contains(Render("<div style=\"display:grid\">x</div>").Unsupported, m => m.Contains("grid", StringComparison.Ordinal));
        Assert.Contains(Render("<div style=\"display:flex;align-items:center\">x</div>").Unsupported, m => m.Contains("cross-axis", StringComparison.Ordinal));
        Assert.Contains(Render("<style>@page { margin: 1cm }</style><p>x</p>").Unsupported, m => m.Contains("@page", StringComparison.Ordinal));
    }

    [Fact]
    public void TextTransformUppercasesContent()
    {
        // Lowercase letters are unmapped in the synthetic font; uppercasing them makes A/B/C map to
        // real glyphs, so no .notdef diagnostic is raised under a conformance check.
        var unsupported = new List<string>();
        var document = Document.Create(doc =>
        {
            doc.Metadata(m => m.Title("t"));
            doc.Page(page => page.Content().Html(
                "<p style=\"text-transform:uppercase\">cab</p>",
                new HtmlRenderOptions { OnUnsupported = unsupported.Add }));
        });
        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer, new PdfSaveOptions { Conformance = PdfConformance.PdfA2b });

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains(".notdef", StringComparison.Ordinal));
    }
}
