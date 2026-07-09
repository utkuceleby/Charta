using Charta.Fonts;

namespace Charta.Shaping.HarfBuzz;

/// <summary>
/// Enables HarfBuzz shaping for Charta. Call <see cref="Register"/> once at startup; afterwards all
/// text is shaped with full OpenType support, so Arabic, Indic, and other complex scripts join and
/// position correctly and their layout diagnostics stop firing.
/// </summary>
public static class ChartaHarfBuzz
{
    private static readonly HarfBuzzShaper Shaper = new();

    /// <summary>Registers the HarfBuzz shaper as Charta's active text shaper.</summary>
    public static void Register() => TextShaperRegistry.Register(Shaper);
}
