using Charta.Fonts;
using Charta.Imaging;
using Charta.Layout;
using Charta.Layout.Elements;

namespace Charta.Smoke;

/// <summary>
/// The M2 showcase document: paragraphs, a rule, a two-column row, an image, and a forced page
/// break — built from the deterministic synthetic fixtures so it can serve as a golden file.
/// </summary>
internal static class LayoutSample
{
    public static LayoutDocument Build()
    {
        var style = new TextStyle
        {
            Fonts = new FontChain(PdfFont.Parse(SyntheticFont.Build())),
            FontSize = 14,
        };
        var smallStyle = new TextStyle
        {
            Fonts = new FontChain(PdfFont.Parse(SyntheticFont.Build())),
            FontSize = 10,
        };

        byte[] rgbaPixels =
        [
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 255, 255, 255, 255, 64,
        ];
        var image = PdfImage.FromBytes(PngFixtures.Build(2, 2, 8, colorType: 6, rgbaPixels, filterType: 4));

        var content = new ColumnElement(
        [
            new TextElement("CAB ABC BCA CBA", style),
            new PaddingElement(new LineElement(1.5, LayoutColor.Black), 0, 4, 0, 4),
            new RowElement(
            [
                new RowItem { Element = new TextElement("AB AB AB AB AB AB", smallStyle) },
                new RowItem { Element = new PaddingElement(new ImageElement(image), 8, 0, 0, 0) },
            ], spacing: 12),
            new BackgroundElement(
                new PaddingElement(new TextElement("BAC", smallStyle), 6),
                LayoutColor.FromRgb(230, 230, 230)),
            new PageBreakElement(),
            new AlignElement(new TextElement("CCC", style), HorizontalAlignment.Center),
        ], spacing: 10);

        return new LayoutDocument { Content = content };
    }
}
