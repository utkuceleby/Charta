namespace Charta;

/// <summary>An sRGB color.</summary>
public readonly record struct Color(byte R, byte G, byte B)
{
    /// <summary>Black (0, 0, 0).</summary>
    public static readonly Color Black = new(0, 0, 0);

    /// <summary>White (255, 255, 255).</summary>
    public static readonly Color White = new(255, 255, 255);

    /// <summary>Creates a color from a 0xRRGGBB value, e.g. <c>Color.FromHex(0x1E88E5)</c>.</summary>
    public static Color FromHex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

/// <summary>A page size in points (1 pt = 1/72 inch).</summary>
public readonly record struct PageSize(double Width, double Height);

/// <summary>Standard page sizes.</summary>
public static class PageSizes
{
    /// <summary>ISO A3 (297 × 420 mm).</summary>
    public static readonly PageSize A3 = new(841.89, 1190.55);

    /// <summary>ISO A4 (210 × 297 mm).</summary>
    public static readonly PageSize A4 = new(595.276, 841.89);

    /// <summary>ISO A5 (148 × 210 mm).</summary>
    public static readonly PageSize A5 = new(419.53, 595.276);

    /// <summary>US Letter (8.5 × 11 in).</summary>
    public static readonly PageSize Letter = new(612, 792);

    /// <summary>US Legal (8.5 × 14 in).</summary>
    public static readonly PageSize Legal = new(612, 1008);
}

/// <summary>Measurement units convertible to points.</summary>
public enum Unit
{
    /// <summary>Typographic point, 1/72 inch. The native unit.</summary>
    Point,

    /// <summary>Millimeter.</summary>
    Millimeter,

    /// <summary>Centimeter.</summary>
    Centimeter,

    /// <summary>Inch.</summary>
    Inch,
}

/// <summary>Conversions to points.</summary>
public static class UnitExtensions
{
    /// <summary>Converts a value in the given unit to points.</summary>
    public static double ToPoints(this Unit unit, double value) => unit switch
    {
        Unit.Point => value,
        Unit.Millimeter => value * 72 / 25.4,
        Unit.Centimeter => value * 72 / 2.54,
        Unit.Inch => value * 72,
        _ => value,
    };
}

/// <summary>What happens when content cannot fit even a whole page.</summary>
public enum OverflowBehavior
{
    /// <summary>Clip at the boundary and record a diagnostic. The default — generation never fails.</summary>
    Clip,

    /// <summary>Throw <see cref="LayoutException"/>. Opt-in strictness for CI pipelines.</summary>
    Throw,
}

/// <summary>PDF conformance level to target.</summary>
public enum PdfConformance
{
    /// <summary>No conformance constraints (the default).</summary>
    None,

    /// <summary>
    /// PDF/A-2b: archival conformance, basic level. Embeds an sRGB output intent and pdfaid metadata,
    /// marks annotations for printing, and always embeds fonts. Requires document metadata.
    /// </summary>
    PdfA2b,

    /// <summary>
    /// PDF/UA-1: accessible (tagged) PDF. Builds a structure tree, tags content, marks decoration as
    /// artifacts, and sets the document language and title flag. Requires a document title and a
    /// language; add alt text to images with the altText argument.
    /// </summary>
    PdfUA1,
}

/// <summary>Options for <see cref="Document.GeneratePdf(Stream, PdfSaveOptions?, CancellationToken)"/>.</summary>
public sealed class PdfSaveOptions
{
    /// <summary>Compress content streams with Flate. On by default; disable for byte-level debugging.</summary>
    public bool Compress { get; init; } = true;

    /// <summary>Overflow policy. Default: clip with diagnostics.</summary>
    public OverflowBehavior Overflow { get; init; } = OverflowBehavior.Clip;

    /// <summary>
    /// When true, clipped overflow regions are marked with a red overlay in the output — a visual aid
    /// for finding layout problems during development. Off in production.
    /// </summary>
    public bool DebugLayout { get; init; }

    /// <summary>Conformance level to target. Default: none.</summary>
    public PdfConformance Conformance { get; init; } = PdfConformance.None;

    /// <summary>
    /// Document language as a BCP-47 tag (e.g. "en-US", "tr-TR"). Required for PDF/UA. Sets the
    /// catalog /Lang.
    /// </summary>
    public string? Language { get; init; }
}

/// <summary>Thrown only under <see cref="OverflowBehavior.Throw"/>; carries the diagnostic that would have been recorded.</summary>
public sealed class LayoutException : Exception
{
    /// <summary>Initializes the exception without a message.</summary>
    public LayoutException()
    {
    }

    /// <summary>Initializes the exception with a message describing the overflow.</summary>
    public LayoutException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an underlying cause.</summary>
    public LayoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A non-fatal layout problem: what did not fit, where, and what was done about it.</summary>
public sealed class LayoutDiagnostic
{
    /// <summary>The element the problem occurred in.</summary>
    public required string ElementPath { get; init; }

    /// <summary>Human-readable description including what action was taken.</summary>
    public required string Message { get; init; }

    /// <summary>1-based page number where the problem occurred.</summary>
    public required int PageNumber { get; init; }
}

/// <summary>Outcome of a generation run.</summary>
public sealed class GenerationResult
{
    /// <summary>Number of pages generated.</summary>
    public required int PageCount { get; init; }

    /// <summary>Layout problems encountered. Empty when everything fit.</summary>
    public required IReadOnlyList<LayoutDiagnostic> Diagnostics { get; init; }
}
