using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Charta.Svg;
using Xunit;

namespace Charta.Tests.Layout;

public class CanvasAndSvgTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static CanvasAndSvgTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string Generate(Document document)
    {
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    [Fact]
    public void Canvas_EmitsPathOperatorsInFlippedSpace()
    {
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Size(new PageSize(200, 200));
            page.Margin(0);
            page.Content().Canvas(100, 100, canvas =>
            {
                canvas.Rectangle(10, 10, 50, 50).Fill(Color.FromHex(0xFF0000));
                canvas.MoveTo(0, 0).LineTo(100, 100).Stroke(Color.Black, 2);
            });
        }));

        var pdf = Generate(document);

        // Canvas sets up a Y-flip transform at the element's top-left.
        Assert.Contains("1 0 0 -1", pdf, StringComparison.Ordinal);
        Assert.Contains("10 10 50 50 re", pdf, StringComparison.Ordinal);
        Assert.Contains("1 0 0 rg", pdf, StringComparison.Ordinal);      // red fill
        Assert.Contains("f\n", pdf, StringComparison.Ordinal);
        Assert.Contains("2 w", pdf, StringComparison.Ordinal);          // stroke width
        Assert.Contains("S\n", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Canvas_ClipsToBounds()
    {
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Canvas(50, 50, c => c.Rectangle(0, 0, 50, 50).Fill(Color.Black))));

        var pdf = Generate(document);

        Assert.Contains("0 0 50 50 re W n", pdf, StringComparison.Ordinal); // clip path
    }

    [Fact]
    public void Svg_ParsesViewBoxAndAspect()
    {
        var svg = SvgImage.Parse("""<svg viewBox="0 0 200 100"><rect x="0" y="0" width="200" height="100" fill="red"/></svg>""");

        Assert.Equal(200, svg.ViewWidth);
        Assert.Equal(100, svg.ViewHeight);
        Assert.Equal(2.0, svg.Aspect, 3);
    }

    [Fact]
    public void Svg_RendersShapes()
    {
        var svg = """
            <svg viewBox="0 0 100 100">
              <rect x="10" y="10" width="30" height="30" fill="#00FF00"/>
              <circle cx="70" cy="70" r="20" fill="blue" stroke="black" stroke-width="2"/>
              <path d="M 0 0 L 100 100" stroke="red" fill="none"/>
            </svg>
            """;
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Width(100).Svg(svg)));

        var pdf = Generate(document);

        Assert.Contains("0 1 0 rg", pdf, StringComparison.Ordinal);   // green rect fill
        Assert.Contains("0 0 1 rg", pdf, StringComparison.Ordinal);   // blue circle fill
        Assert.Contains("B\n", pdf, StringComparison.Ordinal);        // circle fill+stroke
        Assert.Contains("1 0 0 RG", pdf, StringComparison.Ordinal);   // red path stroke
        Assert.Contains(" c\n", pdf, StringComparison.Ordinal);       // circle Béziers
    }

    [Fact]
    public void Svg_HonorsGroupTransform()
    {
        // Render directly into a 100x100 canvas (scale 1) so coordinates are deterministic.
        var svg = SvgImage.Parse("""<svg viewBox="0 0 100 100"><g transform="translate(50 0)"><rect x="0" y="0" width="10" height="10" fill="black"/></g></svg>""");
        var canvas = new Charta.Layout.Elements.CanvasWriter(100, 100);
        svg.Render(canvas);

        // The rect's first corner (0,0) is translated to (50,0).
        Assert.Contains("50 0 m", canvas.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Svg_RejectsMalformedContent()
    {
        Assert.Throws<SvgFormatException>(() => SvgImage.Parse("<svg><rect"));
        Assert.Throws<SvgFormatException>(() => SvgImage.Parse("<notsvg/>"));
        Assert.Throws<SvgFormatException>(() => SvgImage.Parse("""<svg width="0" height="0"/>"""));
    }

    [Fact]
    public void Svg_PathData_RelativeAndCurves()
    {
        // Relative moveto/lineto and a cubic curve must all emit.
        var svg = """<svg viewBox="0 0 100 100"><path d="m 10 10 l 20 0 c 5 5 10 5 15 0 z" fill="black"/></svg>""";
        var document = Document.Create(doc => doc.Page(page => page.Content().Width(100).Svg(svg)));

        var pdf = Generate(document);

        Assert.Contains(" c\n", pdf, StringComparison.Ordinal);
        Assert.Contains("h\n", pdf, StringComparison.Ordinal); // close (z)
    }
}
