using System.Text;

namespace Charta.Text;

/// <summary>A position (in chars) where a line may — or must — be broken. The break goes before the position.</summary>
internal readonly record struct BreakOpportunity(int Position, bool Mandatory);

/// <summary>
/// The UAX#14 line breaking algorithm (Unicode 16.0), rules LB2–LB31, validated against the official
/// LineBreakTest.txt conformance suite. Works on resolved classes from the generated table; LB9/LB10
/// combining-mark absorption is applied via base-index resolution rather than rewriting the sequence.
/// </summary>
internal static class LineBreaker
{
    private const int DottedCircle = 0x25CC;
    private const int Hyphen = 0x2010;

    public static List<BreakOpportunity> FindBreaks(string text)
    {
        var breaks = new List<BreakOpportunity>();
        if (text.Length == 0)
        {
            return breaks;
        }

        // Decode to codepoints once; boundaries are between codepoints.
        var count = 0;
        Span<int> lengths = text.Length <= 512 ? stackalloc int[text.Length] : new int[text.Length];
        var codepoints = new int[text.Length];
        var charIndex = new int[text.Length + 1];
        foreach (var rune in text.EnumerateRunes())
        {
            codepoints[count] = rune.Value;
            lengths[count] = rune.Utf16SequenceLength;
            count++;
        }

        charIndex[0] = 0;
        for (var i = 0; i < count; i++)
        {
            charIndex[i + 1] = charIndex[i] + lengths[i];
        }

        var state = new BreakState(codepoints, count);
        for (var i = 1; i < count; i++)
        {
            var (allowed, mandatory) = state.Evaluate(i);
            if (allowed)
            {
                breaks.Add(new BreakOpportunity(charIndex[i], mandatory));
            }
        }

        breaks.Add(new BreakOpportunity(text.Length, Mandatory: true)); // LB3: always break at eot
        return breaks;
    }

    private readonly ref struct BreakState
    {
        private readonly int[] _codepoints;
        private readonly LineBreakClass[] _classes;
        private readonly LineBreakFlags[] _flags;
        private readonly int _count;

        public BreakState(int[] codepoints, int count)
        {
            _codepoints = codepoints;
            _count = count;
            _classes = new LineBreakClass[count];
            _flags = new LineBreakFlags[count];
            for (var i = 0; i < count; i++)
            {
                _classes[i] = UnicodeLineBreak.GetClass(codepoints[i]);
                _flags[i] = UnicodeLineBreak.GetFlags(codepoints[i]);
            }
        }

        public (bool Allowed, bool Mandatory) Evaluate(int k)
        {
            var j = k - 1;
            var c1 = _classes[j];
            var c2 = _classes[k];

            // LB4–LB6: mandatory breaks and their suppressions.
            if (c1 == LineBreakClass.BK)
            {
                return (true, true);
            }

            if (c1 == LineBreakClass.CR && c2 == LineBreakClass.LF)
            {
                return (false, false);
            }

            if (c1 is LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL)
            {
                return (true, true);
            }

            if (c2 is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL)
            {
                return (false, false);
            }

            // LB7
            if (c2 is LineBreakClass.SP or LineBreakClass.ZW)
            {
                return (false, false);
            }

            // LB8: ZW SP* ÷
            var beforeSpaces = SkipSpacesBack(j);
            if (beforeSpaces >= 0 && _classes[beforeSpaces] == LineBreakClass.ZW)
            {
                return (true, false);
            }

            // LB8a: ZWJ ×
            if (c1 == LineBreakClass.ZWJ)
            {
                return (false, false);
            }

            // LB9: absorb combining marks into their base.
            if (c2 is LineBreakClass.CM or LineBreakClass.ZWJ && !IsAbsorptionBlocker(c1))
            {
                return (false, false);
            }

            var baseJ = BaseIndex(j);
            var e1 = EffectiveClass(baseJ);
            var e2 = c2 is LineBreakClass.CM or LineBreakClass.ZWJ ? LineBreakClass.AL : c2; // LB10
            var f1 = _flags[baseJ];
            var f2 = _flags[k];

            // LB11: word joiner.
            if (e1 == LineBreakClass.WJ || e2 == LineBreakClass.WJ)
            {
                return (false, false);
            }

            // LB12 / LB12a: glue.
            if (e1 == LineBreakClass.GL)
            {
                return (false, false);
            }

            if (e2 == LineBreakClass.GL && e1 is not (LineBreakClass.SP or LineBreakClass.BA or LineBreakClass.HY))
            {
                return (false, false);
            }

            // LB13: closing punctuation.
            if (e2 is LineBreakClass.CL or LineBreakClass.CP or LineBreakClass.EX or LineBreakClass.SY)
            {
                return (false, false);
            }

            // LB14: OP SP* ×
            if (beforeSpaces >= 0 && EffectiveClass(BaseIndex(beforeSpaces)) == LineBreakClass.OP)
            {
                return (false, false);
            }

            // LB15a: (sot | BK CR LF NL OP QU GL SP ZW) [QU-Pi] SP* ×
            if (beforeSpaces >= 0)
            {
                var quBase = BaseIndex(beforeSpaces);
                if (EffectiveClass(quBase) == LineBreakClass.QU &&
                    (_flags[quBase] & LineBreakFlags.InitialPunctuation) != 0 &&
                    IsLb15aContext(quBase - 1))
                {
                    return (false, false);
                }
            }

            // LB15b: × [QU-Pf] (BK CR LF NL SP GL WJ CL QU CP EX IS SY | eot)
            if (e2 == LineBreakClass.QU && (f2 & LineBreakFlags.FinalPunctuation) != 0 && IsLb15bContext(NextAfterCluster(k)))
            {
                return (false, false);
            }

            // LB15c: SP ÷ IS NU
            if (c1 == LineBreakClass.SP && e2 == LineBreakClass.IS)
            {
                var next = NextAfterCluster(k);
                if (next < _count && EffectiveClass(next) == LineBreakClass.NU)
                {
                    return (true, false);
                }
            }

            // LB15d: × IS
            if (e2 == LineBreakClass.IS)
            {
                return (false, false);
            }

            // LB16: (CL | CP) SP* × NS
            if (beforeSpaces >= 0 && e2 == LineBreakClass.NS &&
                EffectiveClass(BaseIndex(beforeSpaces)) is LineBreakClass.CL or LineBreakClass.CP)
            {
                return (false, false);
            }

            // LB17: B2 SP* × B2
            if (beforeSpaces >= 0 && e2 == LineBreakClass.B2 &&
                EffectiveClass(BaseIndex(beforeSpaces)) == LineBreakClass.B2)
            {
                return (false, false);
            }

            // LB18: SP ÷
            if (c1 == LineBreakClass.SP)
            {
                return (true, false);
            }

            // LB19: × [QU-Pi] ; [QU-Pf] ×
            if (e2 == LineBreakClass.QU && (f2 & LineBreakFlags.InitialPunctuation) == 0)
            {
                return (false, false);
            }

            if (e1 == LineBreakClass.QU && (f1 & LineBreakFlags.FinalPunctuation) == 0)
            {
                return (false, false);
            }

            // LB19a: quotes in non-East-Asian context.
            if (e2 == LineBreakClass.QU)
            {
                if ((f1 & LineBreakFlags.EastAsian) == 0)
                {
                    return (false, false);
                }

                var next = NextAfterCluster(k);
                if (next >= _count || (_flags[next] & LineBreakFlags.EastAsian) == 0)
                {
                    return (false, false);
                }
            }

            if (e1 == LineBreakClass.QU)
            {
                if ((f2 & LineBreakFlags.EastAsian) == 0)
                {
                    return (false, false);
                }

                var before = baseJ - 1;
                if (before < 0 || (_flags[BaseIndex(before)] & LineBreakFlags.EastAsian) == 0)
                {
                    return (false, false);
                }
            }

            // LB20: break around contingent breaks.
            if (e1 == LineBreakClass.CB || e2 == LineBreakClass.CB)
            {
                return (true, false);
            }

            // LB20a: (sot | BK CR LF NL SP ZW CB GL) (HY | U+2010) × AL
            if ((e1 == LineBreakClass.HY || _codepoints[baseJ] == Hyphen) && e2 == LineBreakClass.AL && IsLb20aContext(baseJ - 1))
            {
                return (false, false);
            }

            // LB21: × BA HY NS ; BB ×
            if (e2 is LineBreakClass.BA or LineBreakClass.HY or LineBreakClass.NS || e1 == LineBreakClass.BB)
            {
                return (false, false);
            }

            // LB21a: HL (HY | BA-not-EastAsian) × [^HL]
            if ((e1 == LineBreakClass.HY || (e1 == LineBreakClass.BA && (f1 & LineBreakFlags.EastAsian) == 0)) &&
                e2 != LineBreakClass.HL)
            {
                var before = baseJ - 1;
                if (before >= 0 && EffectiveClass(BaseIndex(before)) == LineBreakClass.HL)
                {
                    return (false, false);
                }
            }

            // LB21b: SY × HL
            if (e1 == LineBreakClass.SY && e2 == LineBreakClass.HL)
            {
                return (false, false);
            }

            // LB22: × IN
            if (e2 == LineBreakClass.IN)
            {
                return (false, false);
            }

            // LB23 / LB23a / LB24: letters, numbers, prefixes.
            if (IsAlphabetic(e1) && e2 == LineBreakClass.NU)
            {
                return (false, false);
            }

            if (e1 == LineBreakClass.NU && IsAlphabetic(e2))
            {
                return (false, false);
            }

            if (e1 == LineBreakClass.PR && e2 is LineBreakClass.ID or LineBreakClass.EB or LineBreakClass.EM)
            {
                return (false, false);
            }

            if (e1 is LineBreakClass.ID or LineBreakClass.EB or LineBreakClass.EM && e2 == LineBreakClass.PO)
            {
                return (false, false);
            }

            if (e1 is LineBreakClass.PR or LineBreakClass.PO && IsAlphabetic(e2))
            {
                return (false, false);
            }

            if (IsAlphabetic(e1) && e2 is LineBreakClass.PR or LineBreakClass.PO)
            {
                return (false, false);
            }

            // LB25: numeric sequences.
            if (e2 is LineBreakClass.PO or LineBreakClass.PR)
            {
                var tail = baseJ;
                if (e1 is LineBreakClass.CL or LineBreakClass.CP)
                {
                    tail = PreviousCluster(baseJ);
                }

                if (tail >= 0 && IsNumericTail(tail))
                {
                    return (false, false);
                }
            }

            if (e1 is LineBreakClass.PO or LineBreakClass.PR)
            {
                if (e2 == LineBreakClass.NU)
                {
                    return (false, false);
                }

                if (e2 == LineBreakClass.OP)
                {
                    var n1 = NextAfterCluster(k);
                    if (n1 < _count)
                    {
                        var en1 = EffectiveClass(n1);
                        if (en1 == LineBreakClass.NU)
                        {
                            return (false, false);
                        }

                        if (en1 == LineBreakClass.IS)
                        {
                            var n2 = NextAfterCluster(n1);
                            if (n2 < _count && EffectiveClass(n2) == LineBreakClass.NU)
                            {
                                return (false, false);
                            }
                        }
                    }
                }
            }

            if (e1 is LineBreakClass.HY or LineBreakClass.IS && e2 == LineBreakClass.NU)
            {
                return (false, false);
            }

            if (e2 == LineBreakClass.NU && IsNumericTail(baseJ))
            {
                return (false, false);
            }

            // LB26 / LB27: Korean syllable blocks.
            if (e1 == LineBreakClass.JL &&
                e2 is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.H2 or LineBreakClass.H3)
            {
                return (false, false);
            }

            if (e1 is LineBreakClass.JV or LineBreakClass.H2 && e2 is LineBreakClass.JV or LineBreakClass.JT)
            {
                return (false, false);
            }

            if (e1 is LineBreakClass.JT or LineBreakClass.H3 && e2 == LineBreakClass.JT)
            {
                return (false, false);
            }

            if (IsKorean(e1) && e2 == LineBreakClass.PO)
            {
                return (false, false);
            }

            if (e1 == LineBreakClass.PR && IsKorean(e2))
            {
                return (false, false);
            }

            // LB28: AL/HL × AL/HL
            if (IsAlphabetic(e1) && IsAlphabetic(e2))
            {
                return (false, false);
            }

            // LB28a: Brahmic clusters (aksara, virama).
            if (Lb28a(k, baseJ, e1, e2))
            {
                return (false, false);
            }

            // LB29: IS × AL/HL
            if (e1 == LineBreakClass.IS && IsAlphabetic(e2))
            {
                return (false, false);
            }

            // LB30: letters/numbers with narrow parentheses.
            if (e1 is LineBreakClass.AL or LineBreakClass.HL or LineBreakClass.NU &&
                e2 == LineBreakClass.OP && (f2 & LineBreakFlags.EastAsian) == 0)
            {
                return (false, false);
            }

            if (e1 == LineBreakClass.CP && (f1 & LineBreakFlags.EastAsian) == 0 &&
                e2 is LineBreakClass.AL or LineBreakClass.HL or LineBreakClass.NU)
            {
                return (false, false);
            }

            // LB30a: regional indicator pairs.
            if (e1 == LineBreakClass.RI && e2 == LineBreakClass.RI && CountRegionalIndicatorsEndingAt(baseJ) % 2 == 1)
            {
                return (false, false);
            }

            // LB30b: emoji modifier sequences.
            if (e1 == LineBreakClass.EB && e2 == LineBreakClass.EM)
            {
                return (false, false);
            }

            if ((f1 & LineBreakFlags.ExtendedPictographicUnassigned) != 0 && e2 == LineBreakClass.EM)
            {
                return (false, false);
            }

            // LB31: break everywhere else.
            return (true, false);
        }

        private static bool IsAbsorptionBlocker(LineBreakClass cls) =>
            cls is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                or LineBreakClass.SP or LineBreakClass.ZW;

        private static bool IsAlphabetic(LineBreakClass cls) => cls is LineBreakClass.AL or LineBreakClass.HL;

        private static bool IsKorean(LineBreakClass cls) =>
            cls is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.JT or LineBreakClass.H2 or LineBreakClass.H3;

        /// <summary>Walks back over combining marks to the cluster base (LB9).</summary>
        private int BaseIndex(int index)
        {
            while (index > 0 &&
                   _classes[index] is LineBreakClass.CM or LineBreakClass.ZWJ &&
                   !IsAbsorptionBlocker(_classes[index - 1]))
            {
                index--;
            }

            return index;
        }

        /// <summary>Class of the cluster at <paramref name="index"/>; a lone combining mark acts as AL (LB10).</summary>
        private LineBreakClass EffectiveClass(int index)
        {
            var baseClass = _classes[BaseIndex(index)];
            return baseClass is LineBreakClass.CM or LineBreakClass.ZWJ ? LineBreakClass.AL : baseClass;
        }

        /// <summary>Index of the codepoint after the cluster starting at <paramref name="index"/>.</summary>
        private int NextAfterCluster(int index)
        {
            index++;
            while (index < _count && _classes[index] is LineBreakClass.CM or LineBreakClass.ZWJ)
            {
                index++;
            }

            return index;
        }

        /// <summary>Tail codepoint index of the cluster preceding the one containing <paramref name="index"/>; −1 at sot.</summary>
        private int PreviousCluster(int index) => BaseIndex(index) - 1;

        private int SkipSpacesBack(int index)
        {
            while (index >= 0 && _classes[index] == LineBreakClass.SP)
            {
                index--;
            }

            return index;
        }

        /// <summary>True when the position before a Pi-quote satisfies LB15a's left context.</summary>
        private bool IsLb15aContext(int index)
        {
            if (index < 0)
            {
                return true; // sot
            }

            var cls = _classes[index] == LineBreakClass.SP ? LineBreakClass.SP : EffectiveClass(index);
            return cls is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                or LineBreakClass.OP or LineBreakClass.QU or LineBreakClass.GL or LineBreakClass.SP or LineBreakClass.ZW;
        }

        /// <summary>True when the position after a Pf-quote satisfies LB15b's right context.</summary>
        private bool IsLb15bContext(int index)
        {
            if (index >= _count)
            {
                return true; // eot
            }

            var cls = _classes[index];
            if (cls is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                or LineBreakClass.SP or LineBreakClass.ZW)
            {
                return true;
            }

            return EffectiveClass(index) is LineBreakClass.GL or LineBreakClass.WJ or LineBreakClass.CL
                or LineBreakClass.QU or LineBreakClass.CP or LineBreakClass.EX or LineBreakClass.IS or LineBreakClass.SY;
        }

        private bool IsLb20aContext(int index)
        {
            if (index < 0)
            {
                return true; // sot
            }

            var raw = _classes[index];
            if (raw is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                or LineBreakClass.SP or LineBreakClass.ZW)
            {
                return true;
            }

            return EffectiveClass(index) is LineBreakClass.CB or LineBreakClass.GL;
        }

        /// <summary>True when the cluster at <paramref name="index"/> ends a NU (SY|IS)* sequence (LB25).</summary>
        private bool IsNumericTail(int index)
        {
            while (index >= 0 && EffectiveClass(index) is LineBreakClass.SY or LineBreakClass.IS)
            {
                index = PreviousCluster(index);
            }

            return index >= 0 && EffectiveClass(index) == LineBreakClass.NU;
        }

        private bool Lb28a(int k, int baseJ, LineBreakClass e1, LineBreakClass e2)
        {
            var akLikeLeft = e1 == LineBreakClass.AK || _codepoints[baseJ] == DottedCircle || e1 == LineBreakClass.AS;
            var akRight = e2 == LineBreakClass.AK || _codepoints[k] == DottedCircle;
            var akLikeRight = akRight || e2 == LineBreakClass.AS;

            // AP × (AK | ◌ | AS)
            if (e1 == LineBreakClass.AP && akLikeRight)
            {
                return true;
            }

            // (AK | ◌ | AS) × (VF | VI)
            if (akLikeLeft && e2 is LineBreakClass.VF or LineBreakClass.VI)
            {
                return true;
            }

            // (AK | ◌ | AS) VI × (AK | ◌)
            if (e1 == LineBreakClass.VI && akRight)
            {
                var before = PreviousCluster(baseJ);
                if (before >= 0)
                {
                    var cls = EffectiveClass(before);
                    if (cls is LineBreakClass.AK or LineBreakClass.AS || _codepoints[BaseIndex(before)] == DottedCircle)
                    {
                        return true;
                    }
                }
            }

            // (AK | ◌ | AS) × (AK | ◌ | AS) VF
            if (akLikeLeft && akLikeRight)
            {
                var next = NextAfterCluster(k);
                if (next < _count && EffectiveClass(next) == LineBreakClass.VF)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountRegionalIndicatorsEndingAt(int index)
        {
            var count = 0;
            while (index >= 0 && EffectiveClass(index) == LineBreakClass.RI)
            {
                count++;
                index = PreviousCluster(index);
            }

            return count;
        }
    }
}
