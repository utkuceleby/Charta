namespace Charta.Examples;

/// <summary>A multi-page report: bookmarks, internal links, repeating bands, flowing text.</summary>
public static class Report
{
    private static readonly string[] SectionTitles =
    [
        "Introduction",
        "Architecture",
        "Layout Engine",
        "Text and Fonts",
        "Roadmap",
    ];

    private const string Body =
        "This section demonstrates automatic pagination: text flows across pages with UAX#14 line " +
        "breaking and kerned output, and headers and footers repeat on every page. Content that " +
        "cannot fit is clipped and reported through diagnostics rather than throwing exceptions. ";

    public static void Generate(string path)
    {
        Document.Create(doc =>
        {
            doc.Metadata(m => m
                .Title("Charta Sample Report")
                .Author("Charta Examples"));

            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2.2, Unit.Centimeter);

                page.Header().Column(col =>
                {
                    col.Item().Text("Charta Sample Report").FontSize(11).Bold();
                    col.Item().PaddingVertical(4).LineHorizontal(0.7);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // Table of contents with internal links.
                    col.Item().Bookmark("Contents").Text("Contents").FontSize(18).Bold();
                    foreach (var title in SectionTitles)
                    {
                        col.Item().SectionLink(title).Text("→ " + title).FontSize(11).FontColor(Color.FromHex(0x1E5AA8));
                    }

                    col.Item().PageBreak();

                    foreach (var title in SectionTitles)
                    {
                        col.Item().Section(title).Bookmark(title).Text(title).FontSize(16).Bold();
                        for (var i = 0; i < 6; i++)
                        {
                            col.Item().Text(Body + Body).FontSize(10).LineSpacing(1.15);
                        }
                    }
                });

                page.Footer().AlignCenter().Text("Charta Sample Report").FontSize(8).FontColor(Color.FromHex(0x888888));
            });
        }).GeneratePdf(path);
    }
}
