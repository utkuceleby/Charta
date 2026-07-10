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

    /// <summary>
    /// Generates the PDF into a stream. Pages are flushed as they finish; memory stays flat.
    /// Cancellation is checked before each page — a canceled generation leaves a truncated,
    /// invalid file in the stream.
    /// </summary>
    public GenerationResult GeneratePdf(Stream output, PdfSaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Generate(
            output,
            new PdfWriterOptions { CompressStreams = options?.Compress ?? true },
            options?.Overflow ?? OverflowBehavior.Clip,
            debugOverflow: options?.DebugLayout ?? false,
            conformance: options?.Conformance ?? PdfConformance.None,
            language: options?.Language,
            cancellationToken: cancellationToken);
    }

    /// <summary>Generates the PDF into a file.</summary>
    public GenerationResult GeneratePdf(string filePath, PdfSaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var file = File.Create(filePath);
        return GeneratePdf(file, options, cancellationToken);
    }

    /// <summary>
    /// Generates a digitally signed PDF (PAdES). The document is built into memory, the signature's
    /// byte range is computed, the <paramref name="signer"/> produces the CMS container, and it is
    /// embedded — an invisible signature over the whole document. Use a signer from the
    /// <c>Charta.Signing</c> add-on.
    /// </summary>
    public GenerationResult GenerateSignedPdf(
        Stream output,
        IPdfSigner signer,
        SignatureInfo? info = null,
        PdfSaveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(signer);

        using var buffer = new MemoryStream();
        var result = Generate(
            buffer,
            new PdfWriterOptions { CompressStreams = options?.Compress ?? true },
            options?.Overflow ?? OverflowBehavior.Clip,
            new Charta.Signing.SigningRequest(signer, info ?? new SignatureInfo()),
            debugOverflow: options?.DebugLayout ?? false,
            cancellationToken: cancellationToken);

        var bytes = buffer.ToArray();
        Charta.Signing.PdfSignature.PatchSignature(bytes, signer);
        output.Write(bytes);
        return result;
    }

    /// <summary>Signs and writes to a file.</summary>
    public GenerationResult GenerateSignedPdf(
        string filePath,
        IPdfSigner signer,
        SignatureInfo? info = null,
        PdfSaveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var file = File.Create(filePath);
        return GenerateSignedPdf(file, signer, info, options, cancellationToken);
    }

    /// <summary>Test seam: full control over writer options (xref mode, compression).</summary>
    internal GenerationResult Generate(
        Stream output,
        PdfWriterOptions writerOptions,
        OverflowBehavior overflow,
        Charta.Signing.SigningRequest? signing = null,
        bool debugOverflow = false,
        PdfConformance conformance = PdfConformance.None,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = new DocumentDescriptor();
        using (DescriptionScope.Begin(descriptor))
        {
            _describe(descriptor);
        }

        if (descriptor.Pages.Count == 0)
        {
            throw new InvalidOperationException("The document has no pages. Add at least one with doc.Page(...).");
        }

        int? totalPages = null;
        if (descriptor.UsesTotalPages)
        {
            // Counting pre-pass: lay the document out once (to a null sink) to learn the page count.
            var countingContext = new BuildContext();
            var countingSections = descriptor.Pages.Select(page => page.Build(countingContext)).ToList();
            totalPages = LayoutDocument
                .Generate(Stream.Null, countingSections, overflow, writerOptions, metadata: null, cancellationToken: cancellationToken)
                .PageCount;
        }

        var context = new BuildContext { TotalPages = totalPages };
        var sections = descriptor.Pages.Select(page => page.Build(context)).ToList();
        return LayoutDocument.Generate(output, sections, overflow, writerOptions, descriptor.DocumentMetadata, signing, debugOverflow, conformance, language, cancellationToken);
    }
}
