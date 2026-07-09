namespace Charta.Fonts;

/// <summary>Direction of a shaping run. Determines glyph order and cursive joining behavior.</summary>
internal enum ShaperDirection
{
    LeftToRight,
    RightToLeft,
}

/// <summary>
/// One shaped glyph in visual (final page) order. <see cref="AdvanceDelta"/> is added to the glyph's
/// natural hmtx advance; offsets shift the glyph without moving the pen (used for marks). Cluster
/// fields map the glyph back to the run's source text for the ToUnicode CMap.
/// </summary>
internal readonly record struct ShaperGlyph(
    ushort GlyphId,
    int AdvanceDelta,
    int XOffset,
    int YOffset,
    int ClusterStart,
    int ClusterLength);

/// <summary>
/// Shapes a run of text set in one font, in one direction. Text is supplied in logical order; the
/// returned glyphs are in visual order (left to right on the page). Implementations own substitution
/// (ligatures, cursive joining) and positioning (kerning, marks). The built-in simple shaper does
/// cmap + hmtx + GPOS kerning; the optional HarfBuzz add-on does full OpenType shaping.
/// </summary>
internal interface ITextShaper
{
    IReadOnlyList<ShaperGlyph> Shape(SfntFont font, string text, ShaperDirection direction);

    /// <summary>True when this shaper can render the script correctly (joining/reordering).</summary>
    bool SupportsComplexScript { get; }
}

/// <summary>
/// The process-wide shaper. Defaults to the managed simple shaper; the HarfBuzz add-on replaces it
/// by calling <see cref="Register"/>. Text layout reads <see cref="Current"/> for both shaping and
/// the "is complex shaping available?" decision behind the diagnostic.
/// </summary>
internal static class TextShaperRegistry
{
    private static ITextShaper _current = new SimpleTextShaper();

    public static ITextShaper Current => _current;

    public static void Register(ITextShaper shaper) => _current = shaper ?? throw new ArgumentNullException(nameof(shaper));
}
