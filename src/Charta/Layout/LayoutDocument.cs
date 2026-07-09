using System.Text;
using Charta.Cos;

namespace Charta.Layout;

/// <summary>One run of pages sharing a size, margin, and repeating bands.</summary>
internal sealed class PageSection
{
    public LayoutSize PageSize { get; init; } = new(595.276, 841.89); // A4

    public double Margin { get; init; } = 42.5; // 1.5 cm

    public required Element Content { get; init; }

    /// <summary>
    /// Repeated at the top of every page. A factory, not an element: element trees carry pagination
    /// cursors, so each page needs a fresh instance.
    /// </summary>
    public Func<Element>? Header { get; init; }

    /// <summary>Repeated at the bottom of every page. Factory for the same reason as <see cref="Header"/>.</summary>
    public Func<Element>? Footer { get; init; }
}

/// <summary>
/// The streaming page loop: measure the section root against the content box, draw a page, flush it,
/// repeat until Complete, then move to the next section. Only object numbers and diagnostics
/// accumulate — memory stays flat regardless of page count. Structurally cannot hang: a stalled
/// layout (Empty on a fresh page) or the page cap truncates generation with a diagnostic.
/// </summary>
internal sealed class LayoutDocument
{
    private const int MaxPages = 10_000;

    public LayoutSize PageSize { get; init; } = new(595.276, 841.89);

    public double Margin { get; init; } = 42.5;

    public OverflowBehavior OverflowBehavior { get; init; } = OverflowBehavior.Clip;

    public required Element Content { get; init; }

    public Func<Element>? Header { get; init; }

    public Func<Element>? Footer { get; init; }

    public GenerationResult Generate(Stream output, PdfWriterOptions? options = null) =>
        Generate(
            output,
            [new PageSection { PageSize = PageSize, Margin = Margin, Content = Content, Header = Header, Footer = Footer }],
            OverflowBehavior,
            options);

    public static GenerationResult Generate(
        Stream output,
        IReadOnlyList<PageSection> sections,
        OverflowBehavior overflowBehavior,
        PdfWriterOptions? options = null)
    {
        using var writer = new PdfWriter(output, options);
        writer.WriteHeader();

        var catalogRef = writer.Allocate();
        var pagesRef = writer.Allocate();
        var resourcesRef = writer.Allocate();
        var resources = new PageResources(writer);
        var diagnostics = new List<LayoutDiagnostic>();
        var pageRefs = new List<CosReference>();

        foreach (var section in sections)
        {
            ComposeSection(writer, section, resources, resourcesRef, pagesRef, pageRefs, diagnostics, overflowBehavior);
            if (pageRefs.Count >= MaxPages)
            {
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

    private static void ComposeSection(
        PdfWriter writer,
        PageSection section,
        PageResources resources,
        CosReference resourcesRef,
        CosReference pagesRef,
        List<CosReference> pageRefs,
        List<LayoutDiagnostic> diagnostics,
        OverflowBehavior overflowBehavior)
    {
        var contentBox = new LayoutRect(
            section.Margin,
            section.Margin,
            section.PageSize.Width - 2 * section.Margin,
            section.PageSize.Height - 2 * section.Margin);

        while (true)
        {
            var pageNumber = pageRefs.Count + 1;
            var context = new DrawingContext(resources, section.PageSize.Height, pageNumber, overflowBehavior, diagnostics);

            // Header and footer carve their heights out of this page's body box.
            var bodyTop = contentBox.Y;
            var bodyBottom = contentBox.Y + contentBox.Height;
            if (section.Header?.Invoke() is { } header)
            {
                bodyTop += DrawRepeatedElement(context, section, header, contentBox.Y, "Header");
            }

            if (section.Footer?.Invoke() is { } footer)
            {
                var measuredFooter = footer.Measure(new LayoutConstraints(contentBox.Width, contentBox.Height));
                var footerHeight = Math.Min(measuredFooter.Size.Height, contentBox.Height / 2);
                bodyBottom -= footerHeight;
                _ = DrawRepeatedElement(context, section, footer, bodyBottom, "Footer");
            }

            var bodyBox = new LayoutRect(contentBox.X, bodyTop, contentBox.Width, Math.Max(0, bodyBottom - bodyTop));
            var bodyConstraints = new LayoutConstraints(bodyBox.Width, bodyBox.Height);

            var measured = section.Content.Measure(bodyConstraints);
            var stalled = measured.Verdict == LayoutVerdict.Empty || bodyBox.Height <= 0;

            if (stalled)
            {
                context.AddDiagnostic(
                    section.Content.GetType().Name,
                    $"Layout stalled: nothing fits a fresh page (page {pageNumber}); generation was truncated.");
            }
            else
            {
                section.Content.Draw(context, bodyBox);
            }

            var contentRef = writer.Allocate();
            writer.WriteObject(contentRef, new CosStream(Encoding.ASCII.GetBytes(context.GetContent())));

            var pageRef = writer.Allocate();
            var pageDict = new CosDictionary
            {
                [CosNames.Type] = CosNames.Page,
                [CosNames.Parent] = pagesRef,
                [CosNames.MediaBox] = CosArray.OfReals(0, 0, section.PageSize.Width, section.PageSize.Height),
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
                return;
            }

            var remaining = section.Content.Measure(bodyConstraints);
            if (remaining.Verdict == LayoutVerdict.Complete && remaining.Size is { Width: 0, Height: 0 })
            {
                return;
            }

            if (pageRefs.Count >= MaxPages)
            {
                diagnostics.Add(new LayoutDiagnostic
                {
                    ElementPath = section.Content.GetType().Name,
                    Message = $"Page cap of {MaxPages} reached; generation was truncated.",
                    PageNumber = pageNumber,
                });
                return;
            }
        }
    }

    /// <summary>Draws a header/footer instance at a fixed Y; oversized or paginating content is clipped.</summary>
    private static double DrawRepeatedElement(
        DrawingContext context,
        PageSection section,
        Element element,
        double y,
        string role)
    {
        var contentWidth = section.PageSize.Width - 2 * section.Margin;
        var contentHeight = section.PageSize.Height - 2 * section.Margin;
        var measured = element.Measure(new LayoutConstraints(contentWidth, contentHeight));
        var height = Math.Min(measured.Size.Height, contentHeight / 2);
        var bounds = new LayoutRect(section.Margin, y, contentWidth, height);

        if (measured.Verdict != LayoutVerdict.Complete || measured.Size.Height > height)
        {
            context.AddDiagnostic(role, $"{role} content does not fit its band on page {context.PageNumber}; it was clipped.");
            var captured = bounds;
            context.Clipped(bounds, () => element.Draw(context, captured));
        }
        else
        {
            element.Draw(context, bounds);
        }

        return height;
    }
}
