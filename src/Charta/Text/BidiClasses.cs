namespace Charta.Text;

/// <summary>UAX#9 bidirectional character types. Ordinals must match the generated table.</summary>
internal enum BidiClass : byte
{
    L, R, AL, EN, ES, ET, AN, CS, NSM, BN,
    B, S, WS, ON, LRE, LRO, RLE, RLO, PDF, LRI, RLI, FSI, PDI,
}

/// <summary>Lookups over the generated bidi tables: classes, bracket pairs (BD14–BD16), mirrors (L4).</summary>
internal static class UnicodeBidi
{
    public static BidiClass GetClass(int codepoint)
    {
        var starts = BidiData.ClassRangeStarts;
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

        return (BidiClass)BidiData.ClassRangeValues[lo];
    }

    /// <summary>Paired bracket lookup. Returns false for non-bracket codepoints.</summary>
    public static bool TryGetBracket(int codepoint, out int paired, out bool isOpen)
    {
        var index = BinarySearch(BidiData.BracketCodepoints, codepoint);
        if (index < 0)
        {
            paired = 0;
            isOpen = false;
            return false;
        }

        paired = BidiData.BracketPairedCodepoints[index];
        isOpen = BidiData.BracketIsOpen[index] == 1;
        return true;
    }

    /// <summary>BD16 canonical equivalence: U+2329/U+3008 and U+232A/U+3009 pair with each other.</summary>
    public static int CanonicalBracket(int codepoint) => codepoint switch
    {
        0x2329 => 0x3008,
        0x232A => 0x3009,
        _ => codepoint,
    };

    /// <summary>The mirrored counterpart for L4 (e.g. '(' ↔ ')'), or the codepoint itself.</summary>
    public static int GetMirror(int codepoint)
    {
        var index = BinarySearch(BidiData.MirrorCodepoints, codepoint);
        return index < 0 ? codepoint : BidiData.MirrorTargets[index];
    }

    private static int BinarySearch(ReadOnlySpan<int> values, int target)
    {
        int lo = 0, hi = values.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] < target)
            {
                lo = mid + 1;
            }
            else if (values[mid] > target)
            {
                hi = mid - 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }
}
