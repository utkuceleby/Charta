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
            doc.Metadata(m => m
                .Title("Charta Fluent Sample")
                .Author("Charta Tests")
                .CreationDate(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

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
                    col.Item().Bookmark("Intro").Text("AB AB AB AB AB AB AB AB");
                    col.Item().Hyperlink("https://example.com/charta").Text("CAB");
                    col.Item().SectionLink("end").Text("ABC");
                    col.Item().LineHorizontal(1.5);
                    col.Item()
                        .Background(Color.FromHex(0xE6E6E6))
                        .Padding(6)
                        .Text("BAC CAB")
                        .FontSize(10)
                        .FontColor(Color.FromHex(0x333333));
                    col.Item().Border(1).Padding(8).AlignCenter().Text("CCC").FontSize(14);
                    col.Item().PageBreak();
                    col.Item().Section("end").Bookmark("End").Text("BBB").LineSpacing(1.5);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(120);
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                        });
                        table.Header(header =>
                        {
                            header.Cell().Background(Color.FromHex(0xDDDDDD)).Padding(4).Text("CA").FontSize(10);
                            header.Cell().ColumnSpan(2).Background(Color.FromHex(0xDDDDDD)).Padding(4).Text("CB").FontSize(10);
                        });
                        table.Cell().RowSpan(2).Padding(4).Text("AC").FontSize(10);
                        table.Cell().Padding(4).Text("BA").FontSize(10);
                        table.Cell().Padding(4).Text("BC").FontSize(10);
                        table.Cell().ColumnSpan(2).Background(Color.FromHex(0xF5F5F5)).Padding(4).Text("CC").FontSize(10);
                    });
                });

                page.Footer().AlignCenter().Text("ABC").FontSize(9);
            });
        });
    }
}
