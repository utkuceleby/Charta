namespace Charta.Text;

/// <summary>
/// Detects text the simple shaper cannot render faithfully: scripts that require glyph joining or
/// syllabic reordering (Arabic, Indic, and friends). Bidirectional layout is handled by the
/// built-in UAX#9 implementation — Hebrew renders correctly — but joining scripts still render
/// with unjoined letterforms until the shaping add-on lands, and the layout reports a diagnostic
/// instead of staying silent.
/// </summary>
internal static class ScriptSupport
{
    /// <summary>True when the codepoint belongs to a script needing glyph shaping beyond bidi.</summary>
    public static bool RequiresComplexShaping(int codepoint) => codepoint switch
    {
        // Arabic, Syriac, Arabic Supplement, Thaana, NKo, Samaritan, Mandaic, Arabic Extended.
        >= 0x0600 and <= 0x08FF => true,
        // Indic scripts (Devanagari through Malayalam), Sinhala.
        >= 0x0900 and <= 0x0DFF => true,
        // Myanmar.
        >= 0x1000 and <= 0x109F => true,
        // Khmer.
        >= 0x1780 and <= 0x17FF => true,
        // Arabic presentation forms.
        >= 0xFB50 and <= 0xFDFF => true,
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
