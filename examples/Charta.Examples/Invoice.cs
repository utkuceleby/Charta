namespace Charta.Examples;

/// <summary>A realistic single-page invoice: header row, addresses, item table built from rows, totals.</summary>
public static class Invoice
{
    private sealed record Item(string Description, int Quantity, decimal UnitPrice)
    {
        public decimal Total => Quantity * UnitPrice;
    }

    public static void Generate(string path)
    {
        Item[] items =
        [
            new("Design consultation", 12, 85.00m),
            new("Implementation sprint", 3, 1_450.00m),
            new("Deployment support", 8, 95.00m),
            new("Documentation package", 1, 640.00m),
        ];
        var grandTotal = items.Sum(item => item.Total);

        var accent = Color.FromHex(0x1E5AA8);
        var lightGrey = Color.FromHex(0xF0F0F0);

        Document.Create(doc =>
        {
            doc.Metadata(m => m
                .Title("Invoice #2026-041")
                .Author("Charta Examples")
                .Subject("Sample invoice generated with Charta"));

            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimeter);

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("INVOICE").FontSize(26).Bold().FontColor(accent);
                        col.Item().Text("#2026-041 — 9 July 2026").FontSize(10).FontColor(Color.FromHex(0x666666));
                    });
                    row.ConstantItem(180).AlignRight().Text("Charta Examples Ltd.\nExample Street 12\n34000 Istanbul").FontSize(9);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(14);

                    col.Item().PaddingVertical(6).Text("Bill to: Acme Corporation, Rocket Road 1, 10115 Berlin").FontSize(10);

                    // The item table: a repeating header band and zebra-striped rows.
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(6);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(accent).Padding(6).Text("Description").FontColor(Color.White).Bold().FontSize(10);
                            header.Cell().Background(accent).Padding(6).AlignRight().Text("Qty").FontColor(Color.White).Bold().FontSize(10);
                            header.Cell().Background(accent).Padding(6).AlignRight().Text("Unit").FontColor(Color.White).Bold().FontSize(10);
                            header.Cell().Background(accent).Padding(6).AlignRight().Text("Total").FontColor(Color.White).Bold().FontSize(10);
                        });

                        for (var i = 0; i < items.Length; i++)
                        {
                            var item = items[i];
                            var zebra = i % 2 == 1 ? lightGrey : Color.White;
                            table.Cell().Background(zebra).Padding(6).Text(item.Description).FontSize(10);
                            table.Cell().Background(zebra).Padding(6).AlignRight().Text($"{item.Quantity}").FontSize(10);
                            table.Cell().Background(zebra).Padding(6).AlignRight().Text($"{item.UnitPrice:N2} €").FontSize(10);
                            table.Cell().Background(zebra).Padding(6).AlignRight().Text($"{item.Total:N2} €").FontSize(10);
                        }
                    });

                    col.Item().LineHorizontal(1, accent);

                    col.Item().AlignRight().Text($"Grand total: {grandTotal:N2} €").FontSize(14).Bold();

                    col.Item().PaddingVertical(10)
                        .Text("Payment within 14 days. Questions? Reach us any time — this document is a Charta example.")
                        .FontSize(9)
                        .FontColor(Color.FromHex(0x666666));
                });

                page.Footer().AlignCenter()
                    .Hyperlink("https://github.com/utkuceleby/Charta")
                    .Text("Generated with Charta")
                    .FontSize(8)
                    .FontColor(accent);
            });
        }).GeneratePdf(path);
    }
}
