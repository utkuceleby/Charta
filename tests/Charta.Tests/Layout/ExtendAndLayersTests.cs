using Charta.Layout;
using Charta.Layout.Elements;
using Xunit;

namespace Charta.Tests.Layout;

public class ExtendAndLayersTests
{
    [Fact]
    public void Extend_FillsAvailableSpace()
    {
        var extend = new ExtendElement(new FixedElement(50, 20));

        var result = extend.Measure(LayoutTestHelpers.Constraints(300, 500));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(300, result.Size.Width);
        Assert.Equal(500, result.Size.Height);
    }

    [Fact]
    public void Extend_UnboundedConstraints_UseNaturalSize()
    {
        var extend = new ExtendElement(new FixedElement(50, 20));

        var result = extend.Measure(new LayoutConstraints(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(50, result.Size.Width);
        Assert.Equal(20, result.Size.Height);
    }

    [Fact]
    public void Extend_AsColumnItem_ConsumesRestOfPage()
    {
        var column = new ColumnElement([new FixedElement(100, 50), new ExtendElement(new FixedElement(10, 10))]);

        var result = column.Measure(LayoutTestHelpers.Constraints(200, 700));

        Assert.Equal(LayoutVerdict.Complete, result.Verdict);
        Assert.Equal(700, result.Size.Height, 3);
    }

    [Fact]
    public void Layers_SizeComesFromPrimary_AllLayersDraw()
    {
        var context = LayoutTestHelpers.CreateContext();
        var background = new BackgroundElement(new FixedElement(0, 0), LayoutColor.FromRgb(255, 0, 0));
        var primary = new FixedElement(120, 60);
        var stamp = new BackgroundElement(new FixedElement(0, 0), LayoutColor.FromRgb(0, 0, 255));
        var layers = new LayersElement(primary, [background], [stamp]);

        var measured = layers.Measure(LayoutTestHelpers.Constraints(400, 400));
        layers.Draw(context, new LayoutRect(0, 0, 120, 60));

        Assert.Equal(120, measured.Size.Width);
        Assert.Equal(60, measured.Size.Height);
        Assert.Equal(1, primary.DrawCount);
        var content = context.GetContent();
        // Below layer's red fill must precede the above layer's blue fill.
        Assert.True(
            content.IndexOf("1 0 0 rg", StringComparison.Ordinal) < content.IndexOf("0 0 1 rg", StringComparison.Ordinal),
            "Layer draw order is wrong.");
    }

    [Fact]
    public void FluentLayers_RequireExactlyOnePrimary()
    {
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Layers(l => l.Layer().Text("AB"))));

        var ex = Assert.Throws<InvalidOperationException>(() => document.GeneratePdf(Stream.Null));
        Assert.Contains("PrimaryLayer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FluentLayers_WatermarkBehindText()
    {
        FontManager.RegisterFont(Charta.Smoke.SyntheticFont.Build());
        var document = Document.Create(doc => doc.Page(page => page.Content().Layers(l =>
        {
            l.Layer().Background(Color.FromHex(0xFFEEEE)).Extend();
            l.PrimaryLayer().Text("AB");
        })));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Diagnostics);
    }
}
