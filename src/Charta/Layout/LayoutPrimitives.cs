namespace Charta.Layout;

/// <summary>Available space handed to an element during measurement, in points.</summary>
internal readonly record struct LayoutConstraints(double AvailableWidth, double AvailableHeight);

/// <summary>A measured or arranged size in points.</summary>
internal readonly record struct LayoutSize(double Width, double Height);

/// <summary>An absolute rectangle in top-left-origin page coordinates.</summary>
internal readonly record struct LayoutRect(double X, double Y, double Width, double Height);

/// <summary>
/// The four-state pagination verdict (the engine's core protocol):
/// how much of an element fits into the offered constraints, measured from its current cursor.
/// </summary>
internal enum LayoutVerdict
{
    /// <summary>Everything remaining fits.</summary>
    Complete,

    /// <summary>Some content fits; drawing it advances the element's cursor, the rest continues on the next page.</summary>
    Partial,

    /// <summary>Nothing fits here; try again with fresh constraints (next page).</summary>
    Empty,

    /// <summary>The element cannot fit even when given a whole page — the overflow policy decides what happens.</summary>
    Overflowing,
}

/// <summary>Result of measuring an element: the size it will occupy and the pagination verdict.</summary>
internal readonly record struct MeasureResult(LayoutSize Size, LayoutVerdict Verdict)
{
    public static MeasureResult Complete(double width, double height) =>
        new(new LayoutSize(width, height), LayoutVerdict.Complete);

    public static MeasureResult Partial(double width, double height) =>
        new(new LayoutSize(width, height), LayoutVerdict.Partial);

    public static readonly MeasureResult Empty = new(default, LayoutVerdict.Empty);

    public static MeasureResult Overflowing(double width, double height) =>
        new(new LayoutSize(width, height), LayoutVerdict.Overflowing);
}

/// <summary>What to do when content reports <see cref="LayoutVerdict.Overflowing"/>.</summary>
internal enum OverflowBehavior
{
    /// <summary>Clip at the boundary and record a diagnostic. The default — generation never fails.</summary>
    Clip,

    /// <summary>Throw <see cref="LayoutException"/>. Opt-in strictness for CI pipelines.</summary>
    Throw,
}

/// <summary>An sRGB color with components in [0, 1].</summary>
internal readonly record struct LayoutColor(double R, double G, double B)
{
    public static readonly LayoutColor Black = new(0, 0, 0);

    public static LayoutColor FromRgb(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);
}

/// <summary>A non-fatal layout problem: what did not fit, where, and what was done about it.</summary>
internal sealed class LayoutDiagnostic
{
    public required string ElementPath { get; init; }

    public required string Message { get; init; }

    public required int PageNumber { get; init; }
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
