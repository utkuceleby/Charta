using System.Globalization;
using System.Text;
using Charta.Compliance;
using Charta.Cos;
using Charta.Fonts;
using Charta.Imaging;

namespace Charta.Layout;

/// <summary>
/// Per-page drawing surface. Elements call typed drawing methods with absolute top-left-origin
/// coordinates; this class converts to PDF's bottom-left origin and appends content-stream operators.
/// Also the sink for layout diagnostics and the enforcement point of the overflow policy.
/// </summary>
internal sealed class DrawingContext
{
    private readonly StringBuilder _ops = new();
    private readonly List<LayoutDiagnostic> _diagnostics;
    private readonly NavigationCollector _navigation;
    private readonly double _pageHeight;

    public PageResources Resources { get; }

    public OverflowBehavior OverflowBehavior { get; }

    public int PageNumber { get; }

    /// <summary>Link regions drawn on this page; the page loop turns them into /Annots.</summary>
    public List<PageAnnotation> Annotations { get; } = [];

    public DrawingContext(
        PageResources resources,
        double pageHeight,
        int pageNumber,
        OverflowBehavior overflowBehavior,
        List<LayoutDiagnostic> diagnostics,
        NavigationCollector? navigation = null,
        bool debugOverflow = false)
    {
        Resources = resources;
        _pageHeight = pageHeight;
        PageNumber = pageNumber;
        OverflowBehavior = overflowBehavior;
        _diagnostics = diagnostics;
        _navigation = navigation ?? new NavigationCollector();
        DebugOverflow = debugOverflow;
    }

    /// <summary>The structure tree being built when tagging (PDF/UA) is on; null otherwise.</summary>
    public StructureBuilder? Structure { get; init; }

    /// <summary>
    /// When set (PDF/A or PDF/UA), text drawn with an unmapped glyph (.notdef) raises a diagnostic —
    /// both conformance levels forbid showing .notdef, so this catches the common "font doesn't cover
    /// the text" mistake before a validator does. At most one diagnostic per page.
    /// </summary>
    public bool CheckUnmappedGlyphs { get; init; }

    private bool _reportedUnmapped;

    private int _markedDepth;
    private StructElement? _structParent;

    /// <summary>
    /// Adds a structure element under the current parent. Returns null when not tagging or when
    /// already inside marked content (e.g. an artifact band), so nested content stays untagged.
    /// </summary>
    public StructElement? AddStructElement(string type) =>
        Structure is not null && _markedDepth == 0 ? Structure.AddElement(type, _structParent) : null;

    /// <summary>Draws with a structure element as the parent of any elements created inside.</summary>
    public void WithStructParent(StructElement parent, Action draw)
    {
        var previous = _structParent;
        _structParent = parent;
        draw();
        _structParent = previous;
    }

    /// <summary>0-based page index for MCID allocation.</summary>
    public int PageIndex => PageNumber - 1;

    /// <summary>
    /// Wraps drawing in a tagged marked-content sequence linked to a structure element. Nested
    /// content (e.g. an underline inside a paragraph) stays part of the same tag.
    /// </summary>
    public void Tagged(string tag, StructElement? element, Action draw)
    {
        if (Structure is null || _markedDepth > 0 || element is null)
        {
            draw();
            return;
        }

        var mcid = Structure.AllocateMcid(PageIndex, element);
        Append($"/{tag} <</MCID {mcid.ToString(CultureInfo.InvariantCulture)}>> BDC");
        _markedDepth++;
        draw();
        _markedDepth--;
        Append("EMC");
    }

    /// <summary>Marks decorative drawing (backgrounds, borders, rules, bands) as an artifact.</summary>
    public void Artifact(Action draw)
    {
        if (Structure is null || _markedDepth > 0)
        {
            draw();
            return;
        }

        Append("/Artifact BDC");
        _markedDepth++;
        draw();
        _markedDepth--;
        Append("EMC");
    }

    private void MaybeArtifact(Action draw)
    {
        if (Structure is not null && _markedDepth == 0)
        {
            Artifact(draw);
        }
        else
        {
            draw();
        }
    }

    public void AddAnnotation(PageAnnotation annotation) => Annotations.Add(annotation);

    /// <summary>Records a named destination (and optional bookmark) at a top-left-origin Y on this page.</summary>
    public void RegisterDestination(string name, double top, string? bookmarkTitle)
    {
        _navigation.Destinations[name] = (PageNumber - 1, _pageHeight - top);
        if (bookmarkTitle is not null)
        {
            _navigation.Bookmarks.Add((bookmarkTitle, PageNumber - 1, _pageHeight - top));
        }
    }

    /// <summary>The accumulated content-stream operators for this page.</summary>
    public string GetContent() => _ops.ToString();

    public void FillRect(in LayoutRect rect, LayoutColor color)
    {
        var r = rect;
        MaybeArtifact(() =>
        {
            Append($"{F(color.R)} {F(color.G)} {F(color.B)} rg");
            Append($"{F(r.X)} {F(PdfY(r))} {F(r.Width)} {F(r.Height)} re f");
        });
    }

    public void StrokeRect(in LayoutRect rect, LayoutColor color, double lineWidth)
    {
        var r = rect;
        MaybeArtifact(() =>
        {
            Append($"{F(color.R)} {F(color.G)} {F(color.B)} RG");
            Append($"{F(lineWidth)} w");
            Append($"{F(r.X)} {F(PdfY(r))} {F(r.Width)} {F(r.Height)} re S");
        });
    }

    /// <summary>Draws one shaped run at a baseline position (top-left-origin baseline Y).</summary>
    public void DrawText(
        PdfFont font,
        ShapedText text,
        double x,
        double baselineY,
        double fontSize,
        LayoutColor color,
        double letterSpacing = 0)
    {
        if (CheckUnmappedGlyphs && !_reportedUnmapped && text.CountGlyph(0) > 0)
        {
            _reportedUnmapped = true;
            _diagnostics.Add(new LayoutDiagnostic
            {
                ElementPath = "Text",
                Message = $"Page {PageNumber} contains characters with no glyph in the embedded font "
                    + "(rendered as .notdef), which PDF/A and PDF/UA forbid. Provide a font that covers every character.",
                PageNumber = PageNumber,
            });
        }

        var name = Resources.GetFontName(font);
        Append("BT");
        Append($"{F(color.R)} {F(color.G)} {F(color.B)} rg");
        Append($"/{name} {F(fontSize)} Tf");
        if (letterSpacing != 0)
        {
            Append($"{F(letterSpacing)} Tc"); // character spacing applies per glyph in composite fonts
        }

        Append($"{F(x)} {F(_pageHeight - baselineY)} Td");
        Append(text.ToTextOperator(fontSize));
        if (letterSpacing != 0)
        {
            Append("0 Tc"); // reset so the shared text state does not leak into the next run
        }

        Append("ET");
    }

    /// <summary>Whether overflow debugging overlays are drawn (opt-in via PdfSaveOptions).</summary>
    public bool DebugOverflow { get; init; }

    /// <summary>Draws a red overflow marker — a border and a diagonal cross — over a clipped region.</summary>
    public void DrawOverflowMarker(in LayoutRect rect)
    {
        var red = new LayoutColor(0.85, 0.1, 0.1);
        StrokeRect(rect, red, 1);
        Append($"{F(red.R)} {F(red.G)} {F(red.B)} RG");
        Append("0.5 w");
        Append($"{F(rect.X)} {F(PdfY(rect))} m {F(rect.X + rect.Width)} {F(PdfY(rect) + rect.Height)} l S");
        Append($"{F(rect.X)} {F(PdfY(rect) + rect.Height)} m {F(rect.X + rect.Width)} {F(PdfY(rect))} l S");
    }

    public void DrawImage(PdfImage image, in LayoutRect rect)
    {
        var name = Resources.GetImageName(image);
        Append("q");
        Append($"{F(rect.Width)} 0 0 {F(rect.Height)} {F(rect.X)} {F(PdfY(rect))} cm");
        Append($"/{name} Do");
        Append("Q");
    }

    /// <summary>
    /// Replays canvas path operators (authored in a local top-left coordinate space) inside a
    /// transform that places the local origin at the top-left of <paramref name="rect"/> with y
    /// growing downward, so the drawing code never converts coordinates itself.
    /// </summary>
    public void DrawCanvasContent(in LayoutRect rect, string localOps)
    {
        Append("q");
        Append($"1 0 0 -1 {F(rect.X)} {F(_pageHeight - rect.Y)} cm");
        // Clip to the canvas bounds so drawing cannot spill past its element.
        Append($"0 0 {F(rect.Width)} {F(rect.Height)} re W n");
        _ops.Append(localOps);
        Append("Q");
    }

    /// <summary>Runs <paramref name="draw"/> with output clipped to <paramref name="rect"/>.</summary>
    public void Clipped(in LayoutRect rect, Action draw)
    {
        Append("q");
        Append($"{F(rect.X)} {F(PdfY(rect))} {F(rect.Width)} {F(rect.Height)} re W n");
        draw();
        Append("Q");
    }

    /// <summary>Records an overflow: a diagnostic under Clip, an exception under Throw.</summary>
    public void ReportOverflow(Element element, in LayoutSize requested, in LayoutConstraints available)
    {
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"{element.GetType().Name} needs {requested.Width:0.##}x{requested.Height:0.##}pt but only {available.AvailableWidth:0.##}x{available.AvailableHeight:0.##}pt is available on page {PageNumber}; content was clipped.");
        if (OverflowBehavior == OverflowBehavior.Throw)
        {
            throw new LayoutException(message);
        }

        _diagnostics.Add(new LayoutDiagnostic
        {
            ElementPath = element.GetType().Name,
            Message = message,
            PageNumber = PageNumber,
        });
    }

    public void AddDiagnostic(string elementPath, string message) =>
        _diagnostics.Add(new LayoutDiagnostic
        {
            ElementPath = elementPath,
            Message = message,
            PageNumber = PageNumber,
        });

    private double PdfY(in LayoutRect rect) => _pageHeight - rect.Y - rect.Height;

    private void Append(string line) => _ops.Append(line).Append('\n');

    private static string F(double value) => CosReal.Format(value);
}
