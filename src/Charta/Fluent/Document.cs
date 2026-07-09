using Charta.Cos;
using Charta.Fluent;
using Charta.Layout;

namespace Charta;

/// <summary>
/// A PDF document described with the fluent API. The description is captured as a lambda and
/// executed per generation, so one <see cref="Document"/> can be generated multiple times.
/// </summary>
/// <example>
/// <code>
/// var result = Document.Create(doc =>
/// {
///     doc.Page(page =>
///     {
///         page.Size(PageSizes.A4);
///         page.Margin(2, Unit.Centimeter);
///         page.Header().Text("Invoice #1042").FontSize(20).Bold();
///         page.Content().Column(col =>
///         {
///             col.Item().Text("Hello from Charta.");
///             col.Item().LineHorizontal(1);
///         });
///     });
/// }).GeneratePdf("invoice.pdf");
/// </code>
/// </example>
public sealed class Document
{
    private readonly Action<IDocumentDescriptor> _describe;

    private Document(Action<IDocumentDescriptor> describe) => _describe = describe;

    /// <summary>Creates a document from a description.</summary>
    public static Document Create(Action<IDocumentDescriptor> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        return new Document(describe);
    }

    /// <summary>Generates the PDF into a stream. Pages are flushed as they finish; memory stays flat.</summary>
    public GenerationResult GeneratePdf(Stream output, PdfSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Generate(
            output,
            new PdfWriterOptions { CompressStreams = options?.Compress ?? true },
            options?.Overflow ?? OverflowBehavior.Clip);
    }

    /// <summary>Generates the PDF into a file.</summary>
    public GenerationResult GeneratePdf(string filePath, PdfSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var file = File.Create(filePath);
        return GeneratePdf(file, options);
    }

    /// <summary>Test seam: full control over writer options (xref mode, compression).</summary>
    internal GenerationResult Generate(Stream output, PdfWriterOptions writerOptions, OverflowBehavior overflow)
    {
        var descriptor = new DocumentDescriptor();
        _describe(descriptor);
        if (descriptor.Pages.Count == 0)
        {
            throw new InvalidOperationException("The document has no pages. Add at least one with doc.Page(...).");
        }

        var context = new BuildContext();
        var sections = descriptor.Pages.Select(page => page.Build(context)).ToList();
        return LayoutDocument.Generate(output, sections, overflow, writerOptions);
    }
}
