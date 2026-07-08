using System.Globalization;
using System.Text;
using Charta.Cos;

namespace Charta.Fonts;

/// <summary>
/// M1 scaffolding document: one A4 page of text set in an embedded, subsetted TrueType font.
/// Exercises the full parse → shape → subset → embed pipeline end to end until the layout
/// engine (M2) replaces it.
/// </summary>
internal static class FontSampleDocument
{
    public static void Write(Stream output, ReadOnlyMemory<byte> fontData, string text, PdfWriterOptions? options = null)
    {
        using var writer = new PdfWriter(output, options);
        writer.WriteHeader();

        var catalog = writer.Allocate();
        var pages = writer.Allocate();
        var page = writer.Allocate();
        var content = writer.Allocate();
        var font = writer.Allocate();

        var pdfFont = PdfFont.Parse(fontData);
        var shaped = pdfFont.Shape(text);
        pdfFont.Write(writer, font);

        var contentText = string.Create(
            CultureInfo.InvariantCulture,
            $"BT\n/F1 24 Tf\n72 770 Td\n{shaped.ToHexString()} Tj\nET\n");
        writer.WriteObject(content, new CosStream(Encoding.ASCII.GetBytes(contentText)));

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

        var pagesDict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Pages,
            [CosNames.Kids] = new CosArray(page),
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
