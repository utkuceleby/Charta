namespace Charta.Layout;

/// <summary>A link region collected while drawing a page; becomes a /Link annotation.</summary>
internal sealed class PageAnnotation
{
    public required LayoutRect Rect { get; init; }

    /// <summary>External URI target; mutually exclusive with <see cref="DestinationName"/>.</summary>
    public string? Uri { get; init; }

    /// <summary>Named internal destination target.</summary>
    public string? DestinationName { get; init; }
}

/// <summary>
/// Document-wide navigation state: named destinations (targets of internal links), and bookmarks
/// (the outline panel). Page indices are resolved to page object references in the trailer phase.
/// </summary>
internal sealed class NavigationCollector
{
    public Dictionary<string, (int PageIndex, double Top)> Destinations { get; } = new(StringComparer.Ordinal);

    public List<(string Title, int PageIndex, double Top)> Bookmarks { get; } = [];
}
