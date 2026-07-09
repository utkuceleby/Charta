using System.Text;
using Charta.Cos;

namespace Charta.Smoke;

/// <summary>
/// The M0 walking-skeleton document: one A4 page with a single line of text using an unembedded
/// standard font. Kept as a writer-level test fixture — its goldens pin the COS serializer's bytes.
/// </summary>
internal static class HelloPdf
{
    public static void Write(Stream output, PdfWriterOptions? options = null)
    {
        using var writer = new PdfWriter(output, options);
        writer.WriteHeader();

        var catalog = writer.Allocate();
        var pages = writer.Allocate();
        var page = writer.Allocate();
        var content = writer.Allocate();
        var font = writer.Allocate();

        var fontDict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Font,
            [CosNames.Subtype] = CosName.Get("Type1"),
            [CosNames.BaseFont] = CosName.Get("Helvetica"),
            [CosNames.Encoding] = CosName.Get("WinAnsiEncoding"),
        };
        writer.WriteObject(font, fontDict);

        var contentStream = new CosStream(Encoding.ASCII.GetBytes(
            "BT\n/F1 24 Tf\n72 770 Td\n(Hello from Charta) Tj\nET\n"));
        writer.WriteObject(content, contentStream);

        var fontResources = new CosDictionary
        {
            [CosName.Get("F1")] = font,
        };
        var resources = new CosDictionary
        {
            [CosNames.Font] = fontResources,
        };
        var pageDict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Page,
            [CosNames.Parent] = pages,
            [CosNames.MediaBox] = CosArray.OfReals(0, 0, 595.276, 841.89),
            [CosNames.Resources] = resources,
            [CosNames.Contents] = content,
        };
        writer.WriteObject(page, pageDict);

        var kids = new CosArray(page);
        var pagesDict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Pages,
            [CosNames.Kids] = kids,
            [CosNames.Count] = new CosInteger(1),
        };
        writer.WriteObject(pages, pagesDict);

        var catalogDict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Catalog,
            [CosNames.Pages] = pages,
        };
        writer.WriteObject(catalog, catalogDict);

        writer.WriteTrailer(catalog);
    }
}
