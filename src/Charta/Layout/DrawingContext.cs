using System.Globalization;
using System.Text;
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
        NavigationCollector? navigation = null)
    {
        Resources = resources;
        _pageHeight = pageHeight;
        PageNumber = pageNumber;
        OverflowBehavior = overflowBehavior;
        _diagnostics = diagnostics;
        _navigation = navigation ?? new NavigationCollector();
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
        Append($"{F(color.R)} {F(color.G)} {F(color.B)} rg");
        Append($"{F(rect.X)} {F(PdfY(rect))} {F(rect.Width)} {F(rect.Height)} re f");
    }

    public void StrokeRect(in LayoutRect rect, LayoutColor color, double lineWidth)
    {
        Append($"{F(color.R)} {F(color.G)} {F(color.B)} RG");
        Append($"{F(lineWidth)} w");
        Append($"{F(rect.X)} {F(PdfY(rect))} {F(rect.Width)} {F(rect.Height)} re S");
    }

    /// <summary>Draws one shaped run at a baseline position (top-left-origin baseline Y).</summary>
    public void DrawText(PdfFont font, ShapedText text, double x, double baselineY, double fontSize, LayoutColor color)
    {
        var name = Resources.GetFontName(font);
        Append("BT");
        Append($"{F(color.R)} {F(color.G)} {F(color.B)} rg");
        Append($"/{name} {F(fontSize)} Tf");
        Append($"{F(x)} {F(_pageHeight - baselineY)} Td");
        Append(text.ToTextOperator());
        Append("ET");
    }

    public void DrawImage(PdfImage image, in LayoutRect rect)
    {
        var name = Resources.GetImageName(image);
        Append("q");
        Append($"{F(rect.Width)} 0 0 {F(rect.Height)} {F(rect.X)} {F(PdfY(rect))} cm");
        Append($"/{name} Do");
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
