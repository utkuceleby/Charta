using Charta.Cos;
using Charta.Layout;

namespace Charta.Tests.Layout;

/// <summary>A leaf with a fixed size: Complete when it fits, Overflowing when it does not.</summary>
internal sealed class FixedElement(double width, double height) : Element
{
    public int DrawCount { get; private set; }

    public override MeasureResult Measure(in LayoutConstraints constraints) =>
        height <= constraints.AvailableHeight && width <= constraints.AvailableWidth
            ? MeasureResult.Complete(width, height)
            : MeasureResult.Overflowing(width, height);

    public override void Draw(DrawingContext context, in LayoutRect bounds) => DrawCount++;
}

internal static class LayoutTestHelpers
{
    /// <summary>A context writing to a throwaway PDF writer — content ops and diagnostics are inspectable.</summary>
    public static DrawingContext CreateContext(
        List<LayoutDiagnostic>? diagnostics = null,
        double pageHeight = 800,
        OverflowBehavior overflow = OverflowBehavior.Clip)
    {
        var writer = new PdfWriter(Stream.Null);
        return new DrawingContext(new PageResources(writer), pageHeight, 1, overflow, diagnostics ?? []);
    }

    public static LayoutConstraints Constraints(double width, double height) => new(width, height);
}
