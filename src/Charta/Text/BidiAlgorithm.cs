namespace Charta.Text;

/// <summary>
/// The Unicode Bidirectional Algorithm (UAX#9, Unicode 16.0): explicit embeddings and isolates
/// (X1–X10), weak type resolution (W1–W7), bracket pairs (N0/BD16), neutrals (N1–N2), implicit
/// levels (I1–I2), and line reordering (L1–L2). Validated against the official BidiTest.txt and
/// BidiCharacterTest.txt suites. Operates on bidi classes so the conformance suites drive it
/// directly; codepoints are needed only for bracket pairing and are optional.
/// </summary>
internal static class BidiAlgorithm
{
    public const byte RemovedLevel = 0xFF;

    private const int MaxDepth = 125;

    /// <summary>P2/P3: first strong type decides; isolate runs are skipped. −1/2 = auto-detect.</summary>
    public static byte ResolveParagraphLevel(ReadOnlySpan<BidiClass> classes)
    {
        var isolateDepth = 0;
        foreach (var cls in classes)
        {
            switch (cls)
            {
                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    isolateDepth++;
                    break;
                case BidiClass.PDI when isolateDepth > 0:
                    isolateDepth--;
                    break;
                case BidiClass.L when isolateDepth == 0:
                    return 0;
                case BidiClass.R or BidiClass.AL when isolateDepth == 0:
                    return 1;
                default:
                    break;
            }
        }

        return 0;
    }

    /// <summary>
    /// Resolves embedding levels for one paragraph. Removed characters (explicit formatting and BN)
    /// get <see cref="RemovedLevel"/>. <paramref name="codepoints"/> may be empty — bracket pairing
    /// (N0) is then skipped, which is exactly how BidiTest.txt exercises the algorithm.
    /// </summary>
    public static byte[] ResolveLevels(ReadOnlySpan<BidiClass> classes, ReadOnlySpan<int> codepoints, byte paragraphLevel)
    {
        var length = classes.Length;
        var types = classes.ToArray();      // working copy: overrides and W/N rules rewrite it
        var levels = new byte[length];
        var removed = new bool[length];

        RunExplicitPhase(classes, types, levels, removed, paragraphLevel);

        foreach (var sequence in BuildIsolatingRunSequences(types, levels, removed, classes, paragraphLevel))
        {
            ResolveWeakTypes(sequence, types);
            if (!codepoints.IsEmpty)
            {
                ResolveBrackets(sequence, types, codepoints, levels);
            }

            ResolveNeutrals(sequence, types, levels);
            ResolveImplicitLevels(sequence, types, levels);
        }

        for (var i = 0; i < length; i++)
        {
            if (removed[i])
            {
                levels[i] = RemovedLevel;
            }
        }

        ApplyL1(classes, levels, paragraphLevel);
        return levels;
    }

    /// <summary>L2 over a line: visual order of the non-removed logical indices in [start, start+length).</summary>
    public static int[] ReorderLine(ReadOnlySpan<byte> levels, int start, int length)
    {
        var indices = new List<int>(length);
        var lineLevels = new List<byte>(length);
        for (var i = start; i < start + length; i++)
        {
            if (levels[i] != RemovedLevel)
            {
                indices.Add(i);
                lineLevels.Add(levels[i]);
            }
        }

        if (indices.Count == 0)
        {
            return [];
        }

        var maxLevel = lineLevels.Max();
        byte minOdd = 1;
        var minLevel = lineLevels.Min();
        if (minLevel > minOdd)
        {
            minOdd = (byte)(minLevel % 2 == 0 ? minLevel + 1 : minLevel);
        }

        var order = indices.ToArray();
        for (var level = maxLevel; level >= minOdd; level--)
        {
            var i = 0;
            while (i < order.Length)
            {
                if (lineLevels[i] >= level)
                {
                    var j = i;
                    while (j + 1 < order.Length && lineLevels[j + 1] >= level)
                    {
                        j++;
                    }

                    Array.Reverse(order, i, j - i + 1);
                    ReverseRange(lineLevels, i, j);
                    i = j + 1;
                }
                else
                {
                    i++;
                }
            }
        }

        return order;
    }

    private static void ReverseRange(List<byte> values, int from, int to)
    {
        while (from < to)
        {
            (values[from], values[to]) = (values[to], values[from]);
            from++;
            to--;
        }
    }

    // ---------------------------------------------------------------- X1–X9

    private enum OverrideStatus : byte
    {
        Neutral,
        LeftToRight,
        RightToLeft,
    }

    private readonly record struct StackEntry(byte Level, OverrideStatus Override, bool Isolate);

    private static void RunExplicitPhase(
        ReadOnlySpan<BidiClass> original,
        BidiClass[] types,
        byte[] levels,
        bool[] removed,
        byte paragraphLevel)
    {
        var stack = new Stack<StackEntry>();
        stack.Push(new StackEntry(paragraphLevel, OverrideStatus.Neutral, false));
        var overflowIsolate = 0;
        var overflowEmbedding = 0;
        var validIsolate = 0;

        for (var i = 0; i < original.Length; i++)
        {
            var cls = original[i];
            var top = stack.Peek();

            switch (cls)
            {
                case BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO:
                {
                    levels[i] = top.Level;
                    removed[i] = true;
                    var rtl = cls is BidiClass.RLE or BidiClass.RLO;
                    var newLevel = (byte)(rtl ? (top.Level + 1) | 1 : (top.Level + 2) & ~1);
                    if (newLevel <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                    {
                        var status = cls switch
                        {
                            BidiClass.RLO => OverrideStatus.RightToLeft,
                            BidiClass.LRO => OverrideStatus.LeftToRight,
                            _ => OverrideStatus.Neutral,
                        };
                        stack.Push(new StackEntry(newLevel, status, false));
                    }
                    else if (overflowIsolate == 0)
                    {
                        overflowEmbedding++;
                    }

                    break;
                }

                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                {
                    var rtl = cls == BidiClass.RLI;
                    if (cls == BidiClass.FSI)
                    {
                        rtl = FirstStrongInIsolate(original, i + 1) == 1;
                    }

                    levels[i] = top.Level;
                    ApplyOverride(types, i, top.Override);

                    var newLevel = (byte)(rtl ? (top.Level + 1) | 1 : (top.Level + 2) & ~1);
                    if (newLevel <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                    {
                        validIsolate++;
                        stack.Push(new StackEntry(newLevel, OverrideStatus.Neutral, true));
                    }
                    else
                    {
                        overflowIsolate++;
                    }

                    break;
                }

                case BidiClass.PDI:
                {
                    if (overflowIsolate > 0)
                    {
                        overflowIsolate--;
                    }
                    else if (validIsolate > 0)
                    {
                        overflowEmbedding = 0;
                        while (!stack.Peek().Isolate)
                        {
                            stack.Pop();
                        }

                        stack.Pop();
                        validIsolate--;
                    }

                    top = stack.Peek();
                    levels[i] = top.Level;
                    ApplyOverride(types, i, top.Override);
                    break;
                }

                case BidiClass.PDF:
                {
                    levels[i] = top.Level;
                    removed[i] = true;
                    if (overflowIsolate > 0)
                    {
                        break;
                    }

                    if (overflowEmbedding > 0)
                    {
                        overflowEmbedding--;
                    }
                    else if (!top.Isolate && stack.Count > 1)
                    {
                        stack.Pop();
                    }

                    break;
                }

                case BidiClass.B:
                {
                    // X8: paragraph separators reset everything and take the paragraph level.
                    stack.Clear();
                    stack.Push(new StackEntry(paragraphLevel, OverrideStatus.Neutral, false));
                    overflowIsolate = 0;
                    overflowEmbedding = 0;
                    validIsolate = 0;
                    levels[i] = paragraphLevel;
                    break;
                }

                case BidiClass.BN:
                {
                    levels[i] = top.Level;
                    removed[i] = true;
                    break;
                }

                default:
                {
                    levels[i] = top.Level;
                    ApplyOverride(types, i, top.Override);
                    break;
                }
            }
        }
    }

    private static void ApplyOverride(BidiClass[] types, int index, OverrideStatus status)
    {
        if (status == OverrideStatus.LeftToRight)
        {
            types[index] = BidiClass.L;
        }
        else if (status == OverrideStatus.RightToLeft)
        {
            types[index] = BidiClass.R;
        }
    }

    /// <summary>P3 inside an FSI scope: 1 when the first strong type is R/AL, else 0.</summary>
    private static int FirstStrongInIsolate(ReadOnlySpan<BidiClass> classes, int start)
    {
        var depth = 0;
        for (var i = start; i < classes.Length; i++)
        {
            switch (classes[i])
            {
                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    depth++;
                    break;
                case BidiClass.PDI:
                    if (depth == 0)
                    {
                        return 0;
                    }

                    depth--;
                    break;
                case BidiClass.L when depth == 0:
                    return 0;
                case BidiClass.R or BidiClass.AL when depth == 0:
                    return 1;
                default:
                    break;
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------- X10

    private sealed class IsolatingRunSequence
    {
        public required List<int> Positions { get; init; }

        public required BidiClass Sos { get; init; }

        public required BidiClass Eos { get; init; }

        public required byte Level { get; init; }
    }

    private static List<IsolatingRunSequence> BuildIsolatingRunSequences(
        BidiClass[] types,
        byte[] levels,
        bool[] removed,
        ReadOnlySpan<BidiClass> original,
        byte paragraphLevel)
    {
        var length = types.Length;

        // Level runs over non-removed characters.
        var runs = new List<(int Start, int End, byte Level)>(); // [Start, End] inclusive, indices into full array
        var index = 0;
        while (index < length)
        {
            if (removed[index])
            {
                index++;
                continue;
            }

            var level = levels[index];
            var start = index;
            var end = index;
            index++;
            while (index < length && (removed[index] || levels[index] == level))
            {
                if (!removed[index])
                {
                    end = index;
                }

                index++;
            }

            runs.Add((start, end, level));
        }

        // Match isolate initiators to their PDIs (by position).
        var matchingPdi = new Dictionary<int, int>();
        var initiatorStack = new Stack<int>();
        for (var i = 0; i < length; i++)
        {
            var cls = original[i];
            if (cls is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI)
            {
                initiatorStack.Push(i);
            }
            else if (cls == BidiClass.PDI && initiatorStack.Count > 0)
            {
                matchingPdi[initiatorStack.Pop()] = i;
            }
        }

        var runStartingAt = new Dictionary<int, int>();
        for (var r = 0; r < runs.Count; r++)
        {
            runStartingAt[runs[r].Start] = r;
        }

        var consumed = new bool[runs.Count];
        var sequences = new List<IsolatingRunSequence>();

        for (var r = 0; r < runs.Count; r++)
        {
            if (consumed[r])
            {
                continue;
            }

            // A sequence starts with a run whose first character is not a PDI matching an initiator.
            var firstChar = runs[r].Start;
            if (original[firstChar] == BidiClass.PDI && matchingPdi.ContainsValue(firstChar))
            {
                continue;
            }

            var positions = new List<int>();
            var current = r;
            while (true)
            {
                consumed[current] = true;
                var (start, end, _) = runs[current];
                for (var i = start; i <= end; i++)
                {
                    if (!removed[i])
                    {
                        positions.Add(i);
                    }
                }

                var lastChar = end;
                if (original[lastChar] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI &&
                    matchingPdi.TryGetValue(lastChar, out var pdi) &&
                    runStartingAt.TryGetValue(pdi, out var nextRun))
                {
                    current = nextRun;
                    continue;
                }

                break;
            }

            var level = levels[positions[0]];

            // sos: compare with the level of the nearest non-removed character before the sequence.
            var before = positions[0] - 1;
            while (before >= 0 && removed[before])
            {
                before--;
            }

            var sosLevel = Math.Max(level, before >= 0 ? levels[before] : paragraphLevel);

            // eos: if the sequence ends with an unmatched isolate initiator, the paragraph ends it.
            var lastPos = positions[^1];
            byte eosLevel;
            if (original[lastPos] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI && !matchingPdi.ContainsKey(lastPos))
            {
                eosLevel = Math.Max(level, paragraphLevel);
            }
            else
            {
                var after = lastPos + 1;
                while (after < length && removed[after])
                {
                    after++;
                }

                eosLevel = Math.Max(level, after < length ? levels[after] : paragraphLevel);
            }

            sequences.Add(new IsolatingRunSequence
            {
                Positions = positions,
                Sos = sosLevel % 2 == 1 ? BidiClass.R : BidiClass.L,
                Eos = eosLevel % 2 == 1 ? BidiClass.R : BidiClass.L,
                Level = level,
            });
        }

        return sequences;
    }

    // ---------------------------------------------------------------- W1–W7

    private static void ResolveWeakTypes(IsolatingRunSequence sequence, BidiClass[] types)
    {
        var positions = sequence.Positions;
        var count = positions.Count;

        // W1: NSM takes the type of the previous character (sos at the start; ON after isolates).
        // Chains of NSMs all resolve to the base character's type, so track the RESOLVED previous.
        var previous = sequence.Sos;
        foreach (var pos in positions)
        {
            if (types[pos] == BidiClass.NSM)
            {
                types[pos] = previous is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI
                    ? BidiClass.ON
                    : previous;
            }

            previous = types[pos];
        }

        // W2: EN → AN when the last strong type is AL.
        var lastStrong = sequence.Sos;
        foreach (var pos in positions)
        {
            var cls = types[pos];
            if (cls is BidiClass.L or BidiClass.R or BidiClass.AL)
            {
                lastStrong = cls;
            }
            else if (cls == BidiClass.EN && lastStrong == BidiClass.AL)
            {
                types[pos] = BidiClass.AN;
            }
        }

        // W3: AL → R.
        foreach (var pos in positions)
        {
            if (types[pos] == BidiClass.AL)
            {
                types[pos] = BidiClass.R;
            }
        }

        // W4: single ES between EN — EN; single CS between EN–EN or AN–AN.
        for (var i = 1; i < count - 1; i++)
        {
            var cls = types[positions[i]];
            var prev = types[positions[i - 1]];
            var next = types[positions[i + 1]];
            if (cls == BidiClass.ES && prev == BidiClass.EN && next == BidiClass.EN)
            {
                types[positions[i]] = BidiClass.EN;
            }
            else if (cls == BidiClass.CS &&
                     ((prev == BidiClass.EN && next == BidiClass.EN) || (prev == BidiClass.AN && next == BidiClass.AN)))
            {
                types[positions[i]] = prev;
            }
        }

        // W5: runs of ET adjacent to EN become EN.
        for (var i = 0; i < count; i++)
        {
            if (types[positions[i]] != BidiClass.ET)
            {
                continue;
            }

            var runEnd = i;
            while (runEnd + 1 < count && types[positions[runEnd + 1]] == BidiClass.ET)
            {
                runEnd++;
            }

            var beforeEn = i > 0 && types[positions[i - 1]] == BidiClass.EN;
            var afterEn = runEnd + 1 < count && types[positions[runEnd + 1]] == BidiClass.EN;
            if (beforeEn || afterEn)
            {
                for (var j = i; j <= runEnd; j++)
                {
                    types[positions[j]] = BidiClass.EN;
                }
            }

            i = runEnd;
        }

        // W6: leftover separators and terminators become ON.
        foreach (var pos in positions)
        {
            if (types[pos] is BidiClass.ES or BidiClass.ET or BidiClass.CS)
            {
                types[pos] = BidiClass.ON;
            }
        }

        // W7: EN → L when the last strong type is L.
        lastStrong = sequence.Sos;
        foreach (var pos in positions)
        {
            var cls = types[pos];
            if (cls is BidiClass.L or BidiClass.R)
            {
                lastStrong = cls;
            }
            else if (cls == BidiClass.EN && lastStrong == BidiClass.L)
            {
                types[pos] = BidiClass.L;
            }
        }
    }

    // ---------------------------------------------------------------- N0 (BD16)

    private static void ResolveBrackets(
        IsolatingRunSequence sequence,
        BidiClass[] types,
        ReadOnlySpan<int> codepoints,
        byte[] levels)
    {
        var positions = sequence.Positions;
        var embedding = sequence.Level % 2 == 1 ? BidiClass.R : BidiClass.L;
        var opposite = embedding == BidiClass.R ? BidiClass.L : BidiClass.R;

        // BD16: pair brackets with a 63-deep stack; process pairs in opener order.
        var stack = new Stack<(int Codepoint, int SequenceIndex)>();
        var pairs = new List<(int Open, int Close)>();
        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            if (types[pos] != BidiClass.ON || !UnicodeBidi.TryGetBracket(codepoints[pos], out var paired, out var isOpen))
            {
                continue;
            }

            if (isOpen)
            {
                if (stack.Count >= 63)
                {
                    return; // BD16: stop processing on overflow
                }

                stack.Push((codepoints[pos], i));
            }
            else
            {
                // Pop until a matching opener (canonical equivalence applies); discard non-matching.
                var temp = new Stack<(int Codepoint, int SequenceIndex)>();
                var matched = false;
                while (stack.Count > 0)
                {
                    var candidate = stack.Pop();
                    var expectedClose = UnicodeBidi.TryGetBracket(candidate.Codepoint, out var candidatePair, out _)
                        ? candidatePair
                        : 0;
                    if (UnicodeBidi.CanonicalBracket(expectedClose) == UnicodeBidi.CanonicalBracket(codepoints[pos]))
                    {
                        pairs.Add((candidate.SequenceIndex, i));
                        matched = true;
                        break;
                    }

                    temp.Push(candidate);
                }

                if (!matched)
                {
                    while (temp.Count > 0)
                    {
                        stack.Push(temp.Pop());
                    }
                }
            }
        }

        pairs.Sort((a, b) => a.Open.CompareTo(b.Open));

        foreach (var (open, close) in pairs)
        {
            // Look for a strong type inside the pair.
            var foundEmbedding = false;
            var foundOpposite = false;
            for (var i = open + 1; i < close; i++)
            {
                var strong = StrongClassForN(types[positions[i]]);
                if (strong == embedding)
                {
                    foundEmbedding = true;
                    break;
                }

                if (strong == opposite)
                {
                    foundOpposite = true;
                }
            }

            BidiClass? resolved = null;
            if (foundEmbedding)
            {
                resolved = embedding;
            }
            else if (foundOpposite)
            {
                // Context before the opener decides.
                var context = sequence.Sos;
                for (var i = open - 1; i >= 0; i--)
                {
                    var strong = StrongClassForN(types[positions[i]]);
                    if (strong is not null)
                    {
                        context = strong.Value;
                        break;
                    }
                }

                resolved = context == opposite ? opposite : embedding;
            }

            if (resolved is { } value)
            {
                types[positions[open]] = value;
                types[positions[close]] = value;

                // Any NSM sequence following a changed bracket takes its type (N0 note).
                PropagateToFollowingNsm(sequence, types, codepoints, open, value);
                PropagateToFollowingNsm(sequence, types, codepoints, close, value);
            }
        }

        _ = levels;
    }

    private static void PropagateToFollowingNsm(
        IsolatingRunSequence sequence,
        BidiClass[] types,
        ReadOnlySpan<int> codepoints,
        int sequenceIndex,
        BidiClass value)
    {
        for (var i = sequenceIndex + 1; i < sequence.Positions.Count; i++)
        {
            var pos = sequence.Positions[i];
            if (UnicodeBidi.GetClass(codepoints[pos]) == BidiClass.NSM)
            {
                types[pos] = value;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>For N0/N1 purposes EN and AN count as R.</summary>
    private static BidiClass? StrongClassForN(BidiClass cls) => cls switch
    {
        BidiClass.L => BidiClass.L,
        BidiClass.R or BidiClass.EN or BidiClass.AN => BidiClass.R,
        _ => null,
    };

    // ---------------------------------------------------------------- N1–N2

    private static bool IsNeutralOrIsolate(BidiClass cls) =>
        cls is BidiClass.B or BidiClass.S or BidiClass.WS or BidiClass.ON
            or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI;

    private static void ResolveNeutrals(IsolatingRunSequence sequence, BidiClass[] types, byte[] levels)
    {
        var positions = sequence.Positions;
        var count = positions.Count;
        var embedding = sequence.Level % 2 == 1 ? BidiClass.R : BidiClass.L;

        var i = 0;
        while (i < count)
        {
            if (!IsNeutralOrIsolate(types[positions[i]]))
            {
                i++;
                continue;
            }

            var runEnd = i;
            while (runEnd + 1 < count && IsNeutralOrIsolate(types[positions[runEnd + 1]]))
            {
                runEnd++;
            }

            var before = i > 0 ? StrongClassForN(types[positions[i - 1]]) ?? sequence.Sos : sequence.Sos;
            var after = runEnd + 1 < count ? StrongClassForN(types[positions[runEnd + 1]]) ?? sequence.Eos : sequence.Eos;

            var resolved = before == after ? before : embedding; // N1 else N2
            for (var j = i; j <= runEnd; j++)
            {
                types[positions[j]] = resolved;
            }

            i = runEnd + 1;
        }

        _ = levels;
    }

    // ---------------------------------------------------------------- I1–I2

    private static void ResolveImplicitLevels(IsolatingRunSequence sequence, BidiClass[] types, byte[] levels)
    {
        foreach (var pos in sequence.Positions)
        {
            var level = levels[pos];
            var cls = types[pos];
            if (level % 2 == 0)
            {
                levels[pos] = cls switch
                {
                    BidiClass.R => (byte)(level + 1),
                    BidiClass.AN or BidiClass.EN => (byte)(level + 2),
                    _ => level,
                };
            }
            else
            {
                levels[pos] = cls switch
                {
                    BidiClass.L or BidiClass.AN or BidiClass.EN => (byte)(level + 1),
                    _ => level,
                };
            }
        }
    }

    // ---------------------------------------------------------------- L1

    private static void ApplyL1(ReadOnlySpan<BidiClass> original, byte[] levels, byte paragraphLevel)
    {
        var length = original.Length;
        var resetFrom = length; // start of the current trailing whitespace/isolate run

        for (var i = 0; i < length; i++)
        {
            if (levels[i] == RemovedLevel)
            {
                continue; // removed characters neither break nor join the run
            }

            var cls = original[i];
            if (cls is BidiClass.S or BidiClass.B)
            {
                levels[i] = paragraphLevel;
                for (var j = resetFrom; j < i; j++)
                {
                    if (levels[j] != RemovedLevel)
                    {
                        levels[j] = paragraphLevel;
                    }
                }

                resetFrom = length;
            }
            else if (cls is BidiClass.WS or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI)
            {
                if (resetFrom == length)
                {
                    resetFrom = i;
                }
            }
            else
            {
                resetFrom = length;
            }
        }

        for (var j = resetFrom; j < length; j++)
        {
            if (levels[j] != RemovedLevel)
            {
                levels[j] = paragraphLevel;
            }
        }
    }
}
