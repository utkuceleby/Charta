using System.Globalization;
using System.Text;
using Charta.Cos;
using Charta.Metadata;

namespace Charta.Layout;

/// <summary>One run of pages sharing a size, margin, and repeating bands.</summary>
internal sealed class PageSection
{
    public LayoutSize PageSize { get; init; } = new(595.276, 841.89); // A4

    public double Margin { get; init; } = 42.5; // 1.5 cm

    public required Element Content { get; init; }

    /// <summary>
    /// Repeated at the top of every page; receives the 1-based page number. A factory, not an
    /// element: element trees carry pagination cursors, so each page needs a fresh instance.
    /// </summary>
    public Func<int, Element>? Header { get; init; }

    /// <summary>Repeated at the bottom of every page. Factory for the same reason as <see cref="Header"/>.</summary>
    public Func<int, Element>? Footer { get; init; }
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

    public Func<int, Element>? Header { get; init; }

    public Func<int, Element>? Footer { get; init; }

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
        PdfWriterOptions? options = null,
        DocumentMetadata? metadata = null,
        Charta.Signing.SigningRequest? signing = null,
        CancellationToken cancellationToken = default)
    {
        using var writer = new PdfWriter(output, options);
        writer.WriteHeader();

        var catalogRef = writer.Allocate();
        var pagesRef = writer.Allocate();
        var resourcesRef = writer.Allocate();
        var resources = new PageResources(writer);
        var diagnostics = new List<LayoutDiagnostic>();
        var pageRefs = new List<CosReference>();
        var navigation = new NavigationCollector();

        // The signature widget must live in the first page's annotations, so reserve its object
        // number before composing; ComposeSection injects it into page 1 only.
        var signatureFieldRef = signing is null ? null : writer.Allocate();

        foreach (var section in sections)
        {
            ComposeSection(writer, section, resources, resourcesRef, pagesRef, pageRefs, diagnostics, overflowBehavior, navigation, signatureFieldRef, cancellationToken);
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

        WriteNavigation(writer, catalog, navigation, pageRefs, diagnostics);

        if (signing is not null && signatureFieldRef is not null && pageRefs.Count > 0)
        {
            var sigValueRef = Charta.Signing.PdfSignature.WriteSignatureValue(writer, signing);
            writer.WriteObject(signatureFieldRef, Charta.Signing.PdfSignature.BuildSignatureField(sigValueRef, pageRefs[0]));
            catalog[CosNames.AcroForm] = Charta.Signing.PdfSignature.BuildAcroForm(signatureFieldRef);
        }

        CosReference? infoRef = null;
        if (metadata is { HasAnyValue: true })
        {
            var xmpRef = writer.Allocate();
            var xmp = new CosStream(XmpWriter.Build(metadata)) { AllowCompression = false };
            xmp.Dictionary[CosNames.Type] = CosNames.Metadata;
            xmp.Dictionary[CosNames.Subtype] = CosNames.Xml;
            writer.WriteObject(xmpRef, xmp);
            catalog[CosNames.Metadata] = xmpRef;

            infoRef = writer.Allocate();
            writer.WriteObject(infoRef, BuildInfoDictionary(metadata));
        }

        writer.WriteObject(catalogRef, catalog);
        writer.WriteTrailer(catalogRef, infoRef);

        return new GenerationResult
        {
            PageCount = pageRefs.Count,
            Diagnostics = diagnostics,
        };
    }

    private static void WriteNavigation(
        PdfWriter writer,
        CosDictionary catalog,
        NavigationCollector navigation,
        List<CosReference> pageRefs,
        List<LayoutDiagnostic> diagnostics)
    {
        if (navigation.Destinations.Count > 0)
        {
            // Name tree with a single leaf node: sorted (name, destination) pairs.
            var names = new CosArray();
            foreach (var (name, target) in navigation.Destinations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                names.Add(CosString.FromText(name));
                names.Add(MakeDestination(pageRefs, target.PageIndex, target.Top));
            }

            var dests = new CosDictionary { [CosNames.Names] = names };
            catalog[CosNames.Names] = new CosDictionary { [CosNames.Dests] = dests };
        }

        if (navigation.Bookmarks.Count > 0)
        {
            var outlinesRef = writer.Allocate();
            var itemRefs = navigation.Bookmarks.Select(_ => writer.Allocate()).ToList();

            for (var i = 0; i < navigation.Bookmarks.Count; i++)
            {
                var (title, pageIndex, top) = navigation.Bookmarks[i];
                var item = new CosDictionary
                {
                    [CosNames.Title] = CosString.FromText(title),
                    [CosNames.Parent] = outlinesRef,
                    [CosNames.Dest] = MakeDestination(pageRefs, pageIndex, top),
                };
                if (i > 0)
                {
                    item[CosNames.Prev] = itemRefs[i - 1];
                }

                if (i < itemRefs.Count - 1)
                {
                    item[CosNames.Next] = itemRefs[i + 1];
                }

                writer.WriteObject(itemRefs[i], item);
            }

            var outlines = new CosDictionary
            {
                [CosNames.Type] = CosNames.Outlines,
                [CosNames.First] = itemRefs[0],
                [CosNames.Last] = itemRefs[^1],
                [CosNames.Count] = new CosInteger(itemRefs.Count),
            };
            writer.WriteObject(outlinesRef, outlines);
            catalog[CosNames.Outlines] = outlinesRef;
        }

        _ = diagnostics; // unresolved SectionLink names are reported at annotation time
    }

    private static CosArray MakeDestination(List<CosReference> pageRefs, int pageIndex, double top) =>
        new(
            pageRefs[Math.Clamp(pageIndex, 0, pageRefs.Count - 1)],
            CosNames.Xyz,
            CosNull.Instance,
            new CosReal(top),
            CosNull.Instance);

    private static CosDictionary BuildInfoDictionary(DocumentMetadata metadata)
    {
        var info = new CosDictionary
        {
            [CosNames.Producer] = CosString.FromAscii("Charta"),
        };
        if (metadata.Title is { } title)
        {
            info[CosNames.Title] = CosString.FromText(title);
        }

        if (metadata.Author is { } author)
        {
            info[CosNames.Author] = CosString.FromText(author);
        }

        if (metadata.Subject is { } subject)
        {
            info[CosNames.Subject] = CosString.FromText(subject);
        }

        if (metadata.Keywords is { } keywords)
        {
            info[CosNames.Keywords] = CosString.FromText(keywords);
        }

        if (metadata.Creator is { } creator)
        {
            info[CosNames.Creator] = CosString.FromText(creator);
        }

        if (metadata.CreationDate is { } date)
        {
            var offset = date.Offset;
            var sign = offset >= TimeSpan.Zero ? '+' : '-';
            var formatted = string.Create(
                CultureInfo.InvariantCulture,
                $"D:{date:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}'");
            info[CosNames.CreationDate] = CosString.FromAscii(formatted);
        }

        return info;
    }

    private static void ComposeSection(
        PdfWriter writer,
        PageSection section,
        PageResources resources,
        CosReference resourcesRef,
        CosReference pagesRef,
        List<CosReference> pageRefs,
        List<LayoutDiagnostic> diagnostics,
        OverflowBehavior overflowBehavior,
        NavigationCollector navigation,
        CosReference? signatureFieldRef,
        CancellationToken cancellationToken)
    {
        var contentBox = new LayoutRect(
            section.Margin,
            section.Margin,
            section.PageSize.Width - 2 * section.Margin,
            section.PageSize.Height - 2 * section.Margin);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageNumber = pageRefs.Count + 1;
            var context = new DrawingContext(resources, section.PageSize.Height, pageNumber, overflowBehavior, diagnostics, navigation);

            // Header and footer carve their heights out of this page's body box.
            var bodyTop = contentBox.Y;
            var bodyBottom = contentBox.Y + contentBox.Height;
            if (section.Header?.Invoke(pageNumber) is { } header)
            {
                bodyTop += DrawRepeatedElement(context, section, header, contentBox.Y, "Header");
            }

            if (section.Footer?.Invoke(pageNumber) is { } footer)
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
            // The signature widget goes on the very first page only.
            var includeSignature = signatureFieldRef is not null && pageRefs.Count == 0;
            if (context.Annotations.Count > 0 || includeSignature)
            {
                var annots = new CosArray();
                foreach (var annotation in context.Annotations)
                {
                    annots.Add(BuildLinkAnnotation(annotation, section.PageSize.Height));
                }

                if (includeSignature)
                {
                    annots.Add(signatureFieldRef!);
                }

                pageDict[CosNames.Annots] = annots;
            }

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

    private static CosDictionary BuildLinkAnnotation(PageAnnotation annotation, double pageHeight)
    {
        var rect = annotation.Rect;
        var dict = new CosDictionary
        {
            [CosNames.Type] = CosNames.Annot,
            [CosNames.Subtype] = CosNames.Link,
            [CosNames.Rect] = CosArray.OfReals(
                rect.X, pageHeight - rect.Y - rect.Height, rect.X + rect.Width, pageHeight - rect.Y),
            [CosNames.Border] = CosArray.OfIntegers(0, 0, 0),
        };

        if (annotation.Uri is { } uri)
        {
            dict[CosNames.A] = new CosDictionary
            {
                [CosNames.S] = CosNames.Uri,
                [CosNames.Uri] = CosString.FromAscii(uri),
            };
        }
        else if (annotation.DestinationName is { } destination)
        {
            dict[CosNames.Dest] = CosString.FromText(destination);
        }

        return dict;
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
