namespace Charta.Html;

/// <summary>Options controlling how <see cref="HtmlExtensions.Html"/> renders a fragment.</summary>
public sealed class HtmlRenderOptions
{
    /// <summary>The root font size in points, inherited by elements that do not set their own. Default: 12.</summary>
    public double BaseFontSize { get; init; } = 12;

    /// <summary>The root font family. When null, Charta's default font resolution applies.</summary>
    public string? BaseFontFamily { get; init; }

    /// <summary>The root text color. Default: black.</summary>
    public Color BaseColor { get; init; } = Color.Black;

    /// <summary>Base directory for resolving relative <c>&lt;img src&gt;</c> file paths. Data URIs always work.</summary>
    public string? BasePath { get; init; }

    /// <summary>
    /// The width available to the content, in points — usually the page width minus its margins. When
    /// set, percentage widths (<c>width: 50%</c>) resolve against it (and against resolved parent
    /// widths when nested); when null, percentage widths are reported as unsupported.
    /// </summary>
    public double? ContentWidth { get; init; }

    /// <summary>
    /// Invoked once per distinct unsupported feature encountered (an unknown CSS property value, a
    /// selector combinator, an inline image, and so on). Rendering never throws for these — the
    /// feature is skipped and reported here.
    /// </summary>
    public Action<string>? OnUnsupported { get; init; }
}
