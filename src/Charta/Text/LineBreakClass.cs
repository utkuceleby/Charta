namespace Charta.Text;

/// <summary>
/// UAX#14 line breaking classes after LB1 resolution (AI/SG/XX→AL, CJ→NS, SA→CM/AL).
/// Ordinals must match the generated table — see tools/Charta.CodeGen.
/// </summary>
internal enum LineBreakClass : byte
{
    OP, CL, CP, QU, GL, NS, EX, SY, IS, PR, PO, NU, AL, HL, ID, IN,
    HY, BA, BB, B2, ZW, CM, WJ, H2, H3, JL, JV, JT, RI, EB, EM, ZWJ,
    CB, AK, AP, AS, VF, VI, BK, CR, LF, NL, SP,
}

/// <summary>Per-codepoint properties the line breaking rules consult beyond the class.</summary>
[Flags]
internal enum LineBreakFlags : byte
{
    None = 0,

    /// <summary>East_Asian_Width is F, W, or H (LB19a, LB21a, LB30).</summary>
    EastAsian = 1,

    /// <summary>Extended_Pictographic with general category Cn (LB30b).</summary>
    ExtendedPictographicUnassigned = 2,

    /// <summary>General category Pi — initial quote (LB15a, LB19).</summary>
    InitialPunctuation = 4,

    /// <summary>General category Pf — final quote (LB15b, LB19).</summary>
    FinalPunctuation = 8,
}

/// <summary>Range-table lookup over the generated Unicode data.</summary>
internal static class UnicodeLineBreak
{
    public static LineBreakClass GetClass(int codepoint) =>
        (LineBreakClass)LineBreakData.RangeClasses[RangeIndex(codepoint)];

    public static LineBreakFlags GetFlags(int codepoint) =>
        (LineBreakFlags)LineBreakData.RangeFlags[RangeIndex(codepoint)];

    private static int RangeIndex(int codepoint)
    {
        var starts = LineBreakData.RangeStarts;
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (starts[mid] <= codepoint)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }
}
