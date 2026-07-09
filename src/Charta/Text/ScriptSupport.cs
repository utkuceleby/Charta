namespace Charta.Text;

/// <summary>
/// Detects text the simple shaper cannot render faithfully: scripts that require glyph joining,
/// reordering, or bidirectional layout (Arabic, Hebrew, Indic, and friends). Until the shaping
/// add-on lands, such text renders with unjoined, logically-ordered glyphs — usable for extraction
/// but typographically wrong — and the layout reports a diagnostic instead of staying silent.
/// </summary>
internal static class ScriptSupport
{
    /// <summary>True when the codepoint belongs to a script needing complex shaping or RTL layout.</summary>
    public static bool RequiresComplexShaping(int codepoint) => codepoint switch
    {
        // Hebrew, Arabic, Syriac, Arabic Supplement, Thaana, NKo, Samaritan, Mandaic, Arabic Extended.
        >= 0x0590 and <= 0x08FF => true,
        // Indic scripts (Devanagari through Malayalam), Sinhala.
        >= 0x0900 and <= 0x0DFF => true,
        // Myanmar.
        >= 0x1000 and <= 0x109F => true,
        // Khmer.
        >= 0x1780 and <= 0x17FF => true,
        // Presentation forms (Arabic/Hebrew ligatures and shaped forms).
        >= 0xFB1D and <= 0xFDFF => true,
        >= 0xFE70 and <= 0xFEFF => true,
        _ => false,
    };

    public static bool ContainsComplexScript(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (RequiresComplexShaping(rune.Value))
            {
                return true;
            }
        }

        return false;
    }
}
