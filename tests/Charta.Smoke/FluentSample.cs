namespace Charta.Smoke;

/// <summary>
/// The M3 showcase: the public fluent API end to end, using only synthetic fixtures so the output
/// is deterministic and can serve as a golden file. Requires the synthetic font to be registered.
/// </summary>
internal static class FluentSample
{
    public static Document Build()
    {
        byte[] rgbaPixels =
        [
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 255, 255, 255, 255, 64,
        ];
        var logo = PngFixtures.Build(2, 2, 8, colorType: 6, rgbaPixels, filterType: 4);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimeter);

                page.Header().Row(row =>
                {
                    row.Spacing(12);
                    row.RelativeItem().Text("CAB ABC").FontSize(20).Bold();
                    row.ConstantItem(40).Image(logo);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text("AB AB AB AB AB AB AB AB");
                    col.Item().LineHorizontal(1.5);
                    col.Item()
                        .Background(Color.FromHex(0xE6E6E6))
                        .Padding(6)
                        .Text("BAC CAB")
                        .FontSize(10)
                        .FontColor(Color.FromHex(0x333333));
                    col.Item().Border(1).Padding(8).AlignCenter().Text("CCC").FontSize(14);
                    col.Item().PageBreak();
                    col.Item().Text("BBB").LineSpacing(1.5);
                });

                page.Footer().AlignCenter().Text("ABC").FontSize(9);
            });
        });
    }
}
