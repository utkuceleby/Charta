using System.Text;
using Charta.Cos;

namespace Charta.Layout;

/// <summary>Outcome of a generation run: how many pages and what did not go perfectly.</summary>
internal sealed class GenerationResult
{
    public required int PageCount { get; init; }

    public required IReadOnlyList<LayoutDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// The streaming page loop: measure the root against the content box, draw a page, flush it, repeat
/// until Complete. Only object numbers and diagnostics accumulate — memory stays flat regardless of
/// page count. Structurally cannot hang: a stalled layout (Empty on a fresh page) or the page cap
/// truncates generation with a diagnostic instead of looping.
/// </summary>
internal sealed class LayoutDocument
{
    private const int MaxPages = 10_000;

    public LayoutSize PageSize { get; init; } = new(595.276, 841.89); // A4

    public double Margin { get; init; } = 42.5; // 1.5 cm

    public OverflowBehavior OverflowBehavior { get; init; } = OverflowBehavior.Clip;

    public required Element Content { get; init; }

    public GenerationResult Generate(Stream output, PdfWriterOptions? options = null)
    {
        using var writer = new PdfWriter(output, options);
        writer.WriteHeader();

        var catalogRef = writer.Allocate();
        var pagesRef = writer.Allocate();
        var resourcesRef = writer.Allocate();
        var resources = new PageResources(writer);
        var diagnostics = new List<LayoutDiagnostic>();
        var pageRefs = new List<CosReference>();

        var contentBox = new LayoutRect(Margin, Margin, PageSize.Width - 2 * Margin, PageSize.Height - 2 * Margin);
        var constraints = new LayoutConstraints(contentBox.Width, contentBox.Height);

        while (true)
        {
            var pageNumber = pageRefs.Count + 1;
            var measured = Content.Measure(constraints);
            var context = new DrawingContext(resources, PageSize.Height, pageNumber, OverflowBehavior, diagnostics);
            var stalled = measured.Verdict == LayoutVerdict.Empty;

            if (stalled)
            {
                context.AddDiagnostic(
                    Content.GetType().Name,
                    $"Layout stalled: nothing fits a fresh page (page {pageNumber}); generation was truncated.");
            }
            else
            {
                Content.Draw(context, contentBox);
            }

            var contentRef = writer.Allocate();
            writer.WriteObject(contentRef, new CosStream(Encoding.ASCII.GetBytes(context.GetContent())));

            var pageRef = writer.Allocate();
            var pageDict = new CosDictionary
            {
                [CosNames.Type] = CosNames.Page,
                [CosNames.Parent] = pagesRef,
                [CosNames.MediaBox] = CosArray.OfReals(0, 0, PageSize.Width, PageSize.Height),
                [CosNames.Resources] = resourcesRef,
                [CosNames.Contents] = contentRef,
            };
            writer.WriteObject(pageRef, pageDict);
            pageRefs.Add(pageRef);
            resources.FlushPendingImages();

            // Pre-draw Complete means this page contained everything. Otherwise re-measure: cursors
            // advanced, and a zero-size Complete means a Partial verdict consumed the rest (e.g. a
            // clipped overflow at the end) — stop without emitting a blank page.
            if (stalled || measured.Verdict == LayoutVerdict.Complete)
            {
                break;
            }

            var remaining = Content.Measure(constraints);
            if (remaining.Verdict == LayoutVerdict.Complete && remaining.Size is { Width: 0, Height: 0 })
            {
                break;
            }

            if (pageRefs.Count >= MaxPages)
            {
                diagnostics.Add(new LayoutDiagnostic
                {
                    ElementPath = Content.GetType().Name,
                    Message = $"Page cap of {MaxPages} reached; generation was truncated.",
                    PageNumber = pageNumber,
                });
                break;
            }
        }

        resources.WriteFonts();
        writer.WriteObject(resourcesRef, resources.BuildResourceDictionary());

        var kids = new CosArray();
        foreach (var pageRef in pageRefs)
        {
            kids.Add(pageRef);
        }

        var pagesDict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Pages,
            [CosNames.Kids] = kids,
            [CosNames.Count] = new CosInteger(pageRefs.Count),
        };
        writer.WriteObject(pagesRef, pagesDict);

        var catalog = new CosDictionary
        {
            [CosNames.Type] = CosNames.Catalog,
            [CosNames.Pages] = pagesRef,
        };
        writer.WriteObject(catalogRef, catalog);

        writer.WriteTrailer(catalogRef);

        return new GenerationResult
        {
            PageCount = pageRefs.Count,
            Diagnostics = diagnostics,
        };
    }
}
