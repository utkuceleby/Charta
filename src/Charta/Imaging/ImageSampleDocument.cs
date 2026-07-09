using System.Globalization;
using System.Text;
using Charta.Cos;

namespace Charta.Imaging;

/// <summary>
/// M1 scaffolding document: one A4 page with an image placed via the cm/Do content operators.
/// Exercises decode → XObject → placement end to end until the layout engine (M2) replaces it.
/// </summary>
internal static class ImageSampleDocument
{
    public static void Write(Stream output, ReadOnlyMemory<byte> imageData, PdfWriterOptions? options = null)
    {
        using var writer = new PdfWriter(output, options);
        writer.WriteHeader();

        var catalog = writer.Allocate();
        var pages = writer.Allocate();
        var page = writer.Allocate();
        var content = writer.Allocate();
        var image = writer.Allocate();

        var pdfImage = PdfImage.FromBytes(imageData);
        pdfImage.Write(writer, image);

        // Scale the image to 200 points wide, preserving aspect ratio, at (72, 560).
        var height = 200.0 * pdfImage.Height / pdfImage.Width;
        var contentText = string.Create(
            CultureInfo.InvariantCulture,
            $"q\n200 0 0 {CosReal.Format(height)} 72 560 cm\n/Im1 Do\nQ\n");
        writer.WriteObject(content, new CosStream(Encoding.ASCII.GetBytes(contentText)));

        var xObjects = new CosDictionary
        {
            [CosName.Get("Im1")] = image,
        };
        var resources = new CosDictionary
        {
            [CosNames.XObject] = xObjects,
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
