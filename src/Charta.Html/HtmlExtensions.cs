namespace Charta.Html;

/// <summary>Renders a subset of HTML/CSS into a Charta container.</summary>
public static class HtmlExtensions
{
    /// <summary>
    /// Lays a fragment of HTML out inside this container. Supported: block flow (headings,
    /// paragraphs, div/section, blockquote, pre), inline styling (bold, italic, underline,
    /// strike-through, color, font-size/family, super/subscript, links), unordered and ordered
    /// lists, tables (with colspan/rowspan), horizontal rules, and images (data URIs or file paths).
    /// The cascade covers type/<c>.class</c>/<c>#id</c> selectors from <c>&lt;style&gt;</c> blocks and
    /// inline <c>style</c> attributes. Anything else is skipped and reported through
    /// <see cref="HtmlRenderOptions.OnUnsupported"/> — this method never throws for unsupported markup.
    /// </summary>
    public static void Html(this IContainer container, string html, HtmlRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(html);
        HtmlRenderer.Render(container, html, options ?? new HtmlRenderOptions());
    }
}
