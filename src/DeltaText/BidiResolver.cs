using Delta.Text.Contract;

namespace Delta.Text;

// Implements UAX #9 through L2 (P2/P3, X1-X9, W1-W7, N0-N2, I1-I2, L1/L2)
// using the bundled Unicode 17.0 property table. The official Unicode 17
// BidiCharacterTest corpus passes through L2; that corpus does not exercise
// the optional L3/L4 line-layout rules.
internal static class BidiResolver
{
    private const byte MaximumExplicitLevel = 125;

    internal static BidiConformanceResult ResolveForConformance(string text, TextDirection requested)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return new BidiConformanceResult(0, Array.Empty<int>(), Array.Empty<int>());
        }

        if (requested is TextDirection.TopToBottom or TextDirection.BottomToTop)
        {
            return new BidiConformanceResult(0, Array.Empty<int>(), Array.Empty<int>());
        }

        var baseLevel = requested switch
        {
            TextDirection.LeftToRight => 0,
            TextDirection.RightToLeft => 1,
            _ => ParagraphLevel(text),
        };
        var sourceItems = Tokenize(text);
        var items = sourceItems.ToArray();
        ResolveExplicitLevels(text, items, baseLevel);
        items = RemoveFormatting(items, retainIsolates: true);
        if (HasVisibleItems(items))
        {
            var sequences = BuildIsolatingRunSequences(items, baseLevel);
            ResolveWeakTypesInSequences(sequences);
            ResolvePairedBracketsInSequences(sequences);
            ResolveNeutralTypesInSequences(sequences);
        }

        items = RemoveFormatting(items, retainIsolates: true);
        ResolveImplicitLevels(items);
        ApplyLineBreakRuleL1(items, baseLevel);
        ReorderVisual(items);

        var levels = new int[sourceItems.Length];
        Array.Fill(levels, -1);
        for (var i = 0; i < sourceItems.Length; i++)
        {
            if (!sourceItems[i].Removed || sourceItems[i].IsIsolateControl)
            {
                levels[i] = sourceItems[i].Level;
            }
        }

        var visualOrder = new int[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            visualOrder[i] = items[i].OriginalIndex;
        }

        return new BidiConformanceResult(baseLevel, levels, visualOrder);
    }

    internal static BidiRun[] Resolve(string text, TextDirection requested)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return Array.Empty<BidiRun>();
        }

        if (requested is TextDirection.TopToBottom or TextDirection.BottomToTop)
        {
            return [new BidiRun(0, text.Length, 0, requested)];
        }

        var baseLevel = requested switch
        {
            TextDirection.LeftToRight => 0,
            TextDirection.RightToLeft => 1,
            _ => ParagraphLevel(text),
        };
        var items = Tokenize(text);
        ResolveExplicitLevels(text, items, baseLevel);
        items = RemoveFormatting(items, retainIsolates: true);
        if (!HasVisibleItems(items))
        {
            return [new BidiRun(0, text.Length, baseLevel, Direction(baseLevel))];
        }

        var sequences = BuildIsolatingRunSequences(items, baseLevel);
        ResolveWeakTypesInSequences(sequences);
        ResolvePairedBracketsInSequences(sequences);
        ResolveNeutralTypesInSequences(sequences);
        items = RemoveFormatting(items, retainIsolates: false);
        if (items.Length == 0)
        {
            return [new BidiRun(0, text.Length, baseLevel, Direction(baseLevel))];
        }

        ResolveImplicitLevels(items);
        ApplyLineBreakRuleL1(items, baseLevel);
        ReorderVisual(items);
        return BuildRuns(items);
    }

    private static BidiRun[] BuildRuns(BidiItem[] visualItems)
    {
        var runs = new List<BidiRun>();
        var start = 0;
        while (start < visualItems.Length)
        {
            var level = visualItems[start].Level;
            var end = start + 1;
            while (end < visualItems.Length && visualItems[end].Level == level)
            {
                end++;
            }

            var first = visualItems[start].Start;
            var last = first + visualItems[start].Length;
            for (var i = start + 1; i < end; i++)
            {
                first = Math.Min(first, visualItems[i].Start);
                last = Math.Max(last, visualItems[i].Start + visualItems[i].Length);
            }

            runs.Add(new BidiRun(first, last - first, level, Direction(level)));
            start = end;
        }

        return runs.ToArray();
    }

    private static void ResolveExplicitLevels(string text, BidiItem[] items, int baseLevel)
    {
        var stack = new List<EmbeddingState>(8) { new(baseLevel, BidiClass.None, false, -1) };
        var overflowIsolateCount = 0;
        var overflowEmbeddingCount = 0;
        var validIsolateCount = 0;

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var state = stack[^1];
            item.Level = state.Level;
            if (item.Class == BidiClass.B)
            {
                item.Level = baseLevel;
                continue;
            }

            if (item.Class is BidiClass.Lre or BidiClass.Rle or BidiClass.Lro or BidiClass.Rlo)
            {
                item.Removed = true;
                var direction = item.Class is BidiClass.Lre or BidiClass.Lro ? BidiClass.L : BidiClass.R;
                var nextLevel = NextEmbeddingLevel(state.Level, direction);
                if (overflowIsolateCount == 0)
                {
                    if (nextLevel <= MaximumExplicitLevel && overflowEmbeddingCount == 0)
                    {
                        var overrideType = item.Class is BidiClass.Lro or BidiClass.Rlo ? direction : BidiClass.None;
                        stack.Add(new EmbeddingState(nextLevel, overrideType, false, -1));
                    }
                    else
                    {
                        overflowEmbeddingCount++;
                    }
                }

                continue;
            }

            if (item.Class == BidiClass.Pdf)
            {
                item.Removed = true;
                if (overflowIsolateCount == 0)
                {
                    if (overflowEmbeddingCount > 0)
                    {
                        overflowEmbeddingCount--;
                    }
                    else if (stack.Count > 1 && !stack[^1].Isolate)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                }

                continue;
            }

            if (item.Class is BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi)
            {
                item.Removed = true;
                var direction = item.Class == BidiClass.Lri
                    ? BidiClass.L
                    : item.Class == BidiClass.Rli ? BidiClass.R : FindFirstStrong(text, item.Start + item.Length);
                var nextLevel = NextEmbeddingLevel(state.Level, direction);
                if (overflowIsolateCount > 0)
                {
                    overflowIsolateCount++;
                }
                else if (nextLevel <= MaximumExplicitLevel && overflowEmbeddingCount == 0)
                {
                    stack.Add(new EmbeddingState(nextLevel, BidiClass.None, true, state.Level));
                    validIsolateCount++;
                }
                else
                {
                    overflowIsolateCount++;
                }

                continue;
            }

            if (item.Class == BidiClass.Pdi)
            {
                item.Removed = true;
                if (overflowIsolateCount > 0)
                {
                    overflowIsolateCount--;
                }
                else if (validIsolateCount > 0)
                {
                    var isolateIndex = stack.Count - 1;
                    while (isolateIndex > 0 && !stack[isolateIndex].Isolate)
                    {
                        isolateIndex--;
                    }

                    if (stack[isolateIndex].Isolate)
                    {
                        item.Level = stack[isolateIndex].InitiatorLevel;
                    }

                    overflowEmbeddingCount = 0;
                    while (stack.Count > 1)
                    {
                        var wasIsolate = stack[^1].Isolate;
                        stack.RemoveAt(stack.Count - 1);
                        if (wasIsolate)
                        {
                            validIsolateCount--;
                            break;
                        }
                    }
                }

                continue;
            }

            if (item.Class == BidiClass.Bn)
            {
                item.Removed = true;
                continue;
            }

            if (state.Override != BidiClass.None && item.Class is not (BidiClass.B or BidiClass.S))
            {
                item.Type = state.Override;
            }
        }
    }

    private static IsolatingRunSequence[] BuildIsolatingRunSequences(BidiItem[] items, int baseLevel)
    {
        var matchingPdi = new int[items.Length];
        Array.Fill(matchingPdi, -1);
        var isolateStack = new List<int>(4);
        for (var i = 0; i < items.Length; i++)
        {
            if (IsIsolateInitiator(items[i].Class))
            {
                isolateStack.Add(i);
            }
            else if (items[i].Class == BidiClass.Pdi && isolateStack.Count > 0)
            {
                var opening = isolateStack[^1];
                isolateStack.RemoveAt(isolateStack.Count - 1);
                matchingPdi[opening] = i;
                matchingPdi[i] = opening;
            }
        }

        var runStarts = new List<int>();
        var runEnds = new List<int>();
        for (var i = 0; i < items.Length;)
        {
            var end = i + 1;
            while (end < items.Length && items[end].Level == items[i].Level)
            {
                end++;
            }

            runStarts.Add(i);
            runEnds.Add(end - 1);
            i = end;
        }

        var runByItem = new int[items.Length];
        for (var run = 0; run < runStarts.Count; run++)
        {
            for (var i = runStarts[run]; i <= runEnds[run]; i++)
            {
                runByItem[i] = run;
            }
        }

        var sequences = new List<IsolatingRunSequence>(runStarts.Count);
        for (var run = 0; run < runStarts.Count; run++)
        {
            var first = runStarts[run];
            if (items[first].Class == BidiClass.Pdi && matchingPdi[first] >= 0)
            {
                continue;
            }

            var sequenceItems = new List<BidiItem>();
            var currentRun = run;
            while (true)
            {
                for (var i = runStarts[currentRun]; i <= runEnds[currentRun]; i++)
                {
                    sequenceItems.Add(items[i]);
                }

                var last = runEnds[currentRun];
                if (!IsIsolateInitiator(items[last].Class) || matchingPdi[last] < 0)
                {
                    break;
                }

                var nextRun = runByItem[matchingPdi[last]];
                if (nextRun == currentRun)
                {
                    break;
                }

                currentRun = nextRun;
            }

            var lastLogicalIndex = runEnds[currentRun];
            var unmatchedIsolate = IsIsolateInitiator(items[lastLogicalIndex].Class)
                && matchingPdi[lastLogicalIndex] < 0;
            var sos = SequenceBoundary(items, first, baseLevel, atStart: true, unmatchedIsolate: false);
            var eos = SequenceBoundary(items, lastLogicalIndex, baseLevel, atStart: false, unmatchedIsolate);
            sequences.Add(new IsolatingRunSequence(
                sequenceItems.ToArray(),
                items[first].Level,
                sos,
                eos));
        }

        return sequences.ToArray();
    }

    private static BidiClass SequenceBoundary(
        BidiItem[] items,
        int boundaryIndex,
        int baseLevel,
        bool atStart,
        bool unmatchedIsolate)
    {
        var otherLevel = baseLevel;
        if (atStart)
        {
            for (var i = boundaryIndex - 1; i >= 0; i--)
            {
                if (items[i].Type != BidiClass.Bn)
                {
                    otherLevel = items[i].Level;
                    break;
                }
            }
        }
        else if (!unmatchedIsolate)
        {
            for (var i = boundaryIndex + 1; i < items.Length; i++)
            {
                if (items[i].Type != BidiClass.Bn)
                {
                    otherLevel = items[i].Level;
                    break;
                }
            }
        }

        var level = Math.Max(items[boundaryIndex].Level, otherLevel);
        return DirectionClass(level);
    }

    private static void ResolveWeakTypesInSequences(IsolatingRunSequence[] sequences)
    {
        for (var i = 0; i < sequences.Length; i++)
        {
            ResolveWeakTypesInSequence(sequences[i]);
        }
    }

    private static void ResolveWeakTypesInSequence(IsolatingRunSequence sequence)
    {
        var items = sequence.Items;
        var previous = sequence.Sos;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Type == BidiClass.Nsm)
            {
                items[i].Type = IsIsolateControlType(previous) ? BidiClass.On : previous;
            }

            if (items[i].Type != BidiClass.Bn)
            {
                previous = items[i].Type;
            }
        }

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Type == BidiClass.En && PreviousStrongType(items, i, sequence.Sos) == BidiClass.Al)
            {
                items[i].Type = BidiClass.An;
            }
        }

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Type == BidiClass.Al)
            {
                items[i].Type = BidiClass.R;
            }
        }

        for (var i = 1; i + 1 < items.Length; i++)
        {
            if (items[i].Type == BidiClass.Es
                && items[i - 1].Type == BidiClass.En
                && items[i + 1].Type == BidiClass.En)
            {
                items[i].Type = BidiClass.En;
            }
            else if (items[i].Type == BidiClass.Cs
                && items[i - 1].Type == items[i + 1].Type
                && items[i - 1].Type is BidiClass.En or BidiClass.An)
            {
                items[i].Type = items[i - 1].Type;
            }
        }

        for (var i = 0; i < items.Length;)
        {
            if (items[i].Type != BidiClass.Et)
            {
                i++;
                continue;
            }

            var end = i + 1;
            while (end < items.Length && items[end].Type == BidiClass.Et)
            {
                end++;
            }

            var before = TypeBefore(items, i, sequence.Sos);
            var after = TypeAfter(items, end - 1, sequence.Eos);
            if (before == BidiClass.En || after == BidiClass.En)
            {
                for (var j = i; j < end; j++)
                {
                    items[j].Type = BidiClass.En;
                }
            }

            i = end;
        }

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Type is BidiClass.Es or BidiClass.Et or BidiClass.Cs)
            {
                items[i].Type = BidiClass.On;
            }
        }

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Type == BidiClass.En && PreviousStrongType(items, i, sequence.Sos) == BidiClass.L)
            {
                items[i].Type = BidiClass.L;
            }
        }
    }

    private static BidiClass PreviousStrongType(BidiItem[] items, int position, BidiClass fallback)
    {
        for (var i = position - 1; i >= 0; i--)
        {
            if (items[i].Type is BidiClass.L or BidiClass.R or BidiClass.Al)
            {
                return items[i].Type;
            }
        }

        return fallback;
    }

    private static BidiClass TypeBefore(BidiItem[] items, int position, BidiClass fallback)
    {
        for (var i = position - 1; i >= 0; i--)
        {
            if (items[i].Type != BidiClass.Bn)
            {
                return items[i].Type;
            }
        }

        return fallback;
    }

    private static BidiClass TypeAfter(BidiItem[] items, int position, BidiClass fallback)
    {
        for (var i = position + 1; i < items.Length; i++)
        {
            if (items[i].Type != BidiClass.Bn)
            {
                return items[i].Type;
            }
        }

        return fallback;
    }

    private static void ResolvePairedBracketsInSequences(IsolatingRunSequence[] sequences)
    {
        for (var i = 0; i < sequences.Length; i++)
        {
            ResolvePairedBracketsInSequence(sequences[i]);
        }
    }

    private static void ResolvePairedBracketsInSequence(IsolatingRunSequence sequence)
    {
        const int maximumPairingDepth = 63;
        var items = sequence.Items;
        var openings = new List<int>(maximumPairingDepth);
        var pairs = new List<PairedBracketPair>();
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Type != BidiClass.On
                || !UnicodeBidiData.TryGetPairedBracketInfo(items[i].CodePoint, out _, out var isOpening))
            {
                continue;
            }

            if (isOpening)
            {
                if (openings.Count >= maximumPairingDepth)
                {
                    pairs.Clear();
                    openings.Clear();
                    break;
                }

                openings.Add(i);
                continue;
            }

            for (var openingIndex = openings.Count - 1; openingIndex >= 0; openingIndex--)
            {
                var opening = openings[openingIndex];
                if (!BracketsMatch(items[opening].CodePoint, items[i].CodePoint))
                {
                    continue;
                }

                pairs.Add(new PairedBracketPair(opening, i));
                openings.RemoveRange(openingIndex, openings.Count - openingIndex);
                break;
            }
        }

        pairs.Sort((left, right) => left.OpenPosition.CompareTo(right.OpenPosition));
        var embedding = DirectionClass(sequence.Level);
        for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            var bracketPair = pairs[pairIndex];
            var opposite = BidiClass.None;
            var hasEmbeddingType = false;
            for (var i = bracketPair.OpenPosition + 1; i < bracketPair.ClosePosition; i++)
            {
                var strong = StrongTypeForBracket(items[i].Type);
                if (strong == embedding)
                {
                    hasEmbeddingType = true;
                    break;
                }

                if (strong is BidiClass.L or BidiClass.R)
                {
                    opposite = strong;
                }
            }

            if (!hasEmbeddingType && opposite is not (BidiClass.L or BidiClass.R))
            {
                continue;
            }

            var resolved = embedding;
            if (!hasEmbeddingType)
            {
                resolved = StrongTypeBeforePosition(items, bracketPair.OpenPosition, sequence.Sos);
            }

            items[bracketPair.OpenPosition].Type = resolved;
            items[bracketPair.ClosePosition].Type = resolved;
        }

        for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            var bracketPair = pairs[pairIndex];
            var type = items[bracketPair.OpenPosition].Type;
            if (type is not (BidiClass.L or BidiClass.R))
            {
                continue;
            }

            ApplyBracketTypeToMarks(items, bracketPair.OpenPosition, type);
            ApplyBracketTypeToMarks(items, bracketPair.ClosePosition, type);
        }
    }

    private static BidiClass StrongTypeForBracket(BidiClass type)
        => type is BidiClass.L or BidiClass.R
            ? type
            : type is BidiClass.En or BidiClass.An ? BidiClass.R : BidiClass.None;

    private static BidiClass StrongTypeBeforePosition(BidiItem[] items, int position, BidiClass fallback)
    {
        for (var i = position - 1; i >= 0; i--)
        {
            var strong = StrongTypeForBracket(items[i].Type);
            if (strong is BidiClass.L or BidiClass.R)
            {
                return strong;
            }
        }

        return fallback;
    }

    private static bool BracketsMatch(int openingCodePoint, int closingCodePoint)
    {
        if (!UnicodeBidiData.TryGetPairedBracket(openingCodePoint, out var expected))
        {
            return false;
        }

        if (expected == closingCodePoint)
        {
            return true;
        }

        return openingCodePoint is 0x2329 or 0x3008
            && closingCodePoint is 0x232A or 0x3009;
    }

    private static void ApplyBracketTypeToMarks(BidiItem[] items, int position, BidiClass type)
    {
        for (var i = position + 1; i < items.Length; i++)
        {
            if (items[i].Class == BidiClass.Bn)
            {
                continue;
            }

            if (items[i].Class != BidiClass.Nsm)
            {
                break;
            }

            items[i].Type = type;
        }
    }

    private static void ResolveNeutralTypesInSequences(IsolatingRunSequence[] sequences)
    {
        for (var sequenceIndex = 0; sequenceIndex < sequences.Length; sequenceIndex++)
        {
            var sequence = sequences[sequenceIndex];
            var items = sequence.Items;
            for (var i = 0; i < items.Length;)
            {
                if (!IsNeutralTypeForSequence(items[i].Type))
                {
                    i++;
                    continue;
                }

                var end = i + 1;
                while (end < items.Length && IsNeutralTypeForSequence(items[end].Type))
                {
                    end++;
                }

                var before = StrongTypeBeforeNeutral(items, i, sequence.Sos);
                var after = StrongTypeAfterNeutral(items, end, sequence.Eos);
                var resolved = before == after ? before : DirectionClass(sequence.Level);
                var beforeIsActual = HasStrongBeforeNeutral(items, i);
                var afterIsActual = HasStrongAfterNeutral(items, end);
                for (var j = i; j < end; j++)
                {
                    if ((!beforeIsActual && !afterIsActual && IsIsolateControlType(items[j].Type))
                        || !IsNeutralTypeForSequence(items[j].Type))
                    {
                        continue;
                    }

                    items[j].Type = resolved;
                }

                i = end;
            }
        }
    }

    private static BidiClass StrongTypeBeforeNeutral(BidiItem[] items, int position, BidiClass fallback)
    {
        for (var i = position - 1; i >= 0; i--)
        {
            var type = StrongTypeForNeutral(items[i].Type);
            if (type != BidiClass.None)
            {
                return type;
            }
        }

        return fallback;
    }

    private static BidiClass StrongTypeAfterNeutral(BidiItem[] items, int position, BidiClass fallback)
    {
        for (var i = position; i < items.Length; i++)
        {
            var type = StrongTypeForNeutral(items[i].Type);
            if (type != BidiClass.None)
            {
                return type;
            }
        }

        return fallback;
    }

    private static bool HasStrongBeforeNeutral(BidiItem[] items, int position)
    {
        for (var i = position - 1; i >= 0; i--)
        {
            if (StrongTypeForNeutral(items[i].Type) != BidiClass.None)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStrongAfterNeutral(BidiItem[] items, int position)
    {
        for (var i = position; i < items.Length; i++)
        {
            if (StrongTypeForNeutral(items[i].Type) != BidiClass.None)
            {
                return true;
            }
        }

        return false;
    }

    private static BidiClass StrongTypeForNeutral(BidiClass type)
        => type is BidiClass.L or BidiClass.R
            ? type
            : type is BidiClass.En or BidiClass.An ? BidiClass.R : BidiClass.None;

    private static bool IsNeutralTypeForSequence(BidiClass type)
        => type is BidiClass.B
            or BidiClass.S
            or BidiClass.Ws
            or BidiClass.On
            or BidiClass.Bn
            or BidiClass.Lri
            or BidiClass.Rli
            or BidiClass.Fsi
            or BidiClass.Pdi;

    private static bool IsIsolateInitiator(BidiClass type)
        => type is BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi;

    private static bool IsIsolateControlType(BidiClass type)
        => type is BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi or BidiClass.Pdi;

    private static void ApplyLineBreakRuleL1(BidiItem[] items, int baseLevel)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Class is not (BidiClass.B or BidiClass.S))
            {
                continue;
            }

            items[i].Level = baseLevel;
            for (var j = i - 1; j >= 0 && IsL1Resettable(items[j].Class); j--)
            {
                items[j].Level = baseLevel;
            }
        }

        for (var i = items.Length - 1; i >= 0 && IsL1Resettable(items[i].Class); i--)
        {
            items[i].Level = baseLevel;
        }
    }

    private static bool IsL1Resettable(BidiClass type)
        => type is BidiClass.Ws
            or BidiClass.Lri
            or BidiClass.Rli
            or BidiClass.Fsi
            or BidiClass.Pdi
            or BidiClass.Lre
            or BidiClass.Rle
            or BidiClass.Lro
            or BidiClass.Rlo
            or BidiClass.Pdf
            or BidiClass.Bn;

    private static void ResolveImplicitLevels(BidiItem[] items)
    {
        foreach (var item in items)
        {
            if (item.Level % 2 == 0)
            {
                if (item.Type == BidiClass.R)
                {
                    item.Level++;
                }
                else if (item.Type is BidiClass.En or BidiClass.An)
                {
                    item.Level += 2;
                }
            }
            else if (item.Type == BidiClass.L)
            {
                item.Level++;
            }
            else if (item.Type is BidiClass.En or BidiClass.An)
            {
                item.Level++;
            }
        }
    }

    private static void ReorderVisual(BidiItem[] items)
    {
        var maximum = 0;
        var minimumOdd = int.MaxValue;
        foreach (var item in items)
        {
            maximum = Math.Max(maximum, item.Level);
            if (item.Level % 2 != 0)
            {
                minimumOdd = Math.Min(minimumOdd, item.Level);
            }
        }

        for (var level = maximum; level >= minimumOdd; level--)
        {
            for (var start = 0; start < items.Length;)
            {
                if (items[start].Level < level)
                {
                    start++;
                    continue;
                }

                var end = start + 1;
                while (end < items.Length && items[end].Level >= level)
                {
                    end++;
                }

                Array.Reverse(items, start, end - start);
                start = end;
            }
        }
    }

    private static BidiItem[] Tokenize(string text)
    {
        var items = new List<BidiItem>(text.Length);
        for (var offset = 0; offset < text.Length;)
        {
            var length = CodePointLength(text, offset);
            var codepoint = ReadCodePoint(text, offset);
            items.Add(new BidiItem(items.Count, offset, length, codepoint, UnicodeBidiData.Get(codepoint)));
            offset += length;
        }

        return items.ToArray();
    }

    private static BidiItem[] RemoveFormatting(BidiItem[] items, bool retainIsolates)
    {
        var count = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].Removed || retainIsolates && items[i].IsIsolateControl)
            {
                items[count++] = items[i];
            }
        }

        if (count != items.Length)
        {
            Array.Resize(ref items, count);
        }

        return items;
    }

    private static bool HasVisibleItems(BidiItem[] items)
    {
        foreach (var item in items)
        {
            if (!item.IsIsolateControl)
            {
                return true;
            }
        }

        return false;
    }

    private static int ParagraphLevel(string text)
    {
        var isolateDepth = 0;
        for (var offset = 0; offset < text.Length;)
        {
            var length = CodePointLength(text, offset);
            var type = UnicodeBidiData.Get(ReadCodePoint(text, offset));
            if (IsIsolateInitiator(type))
            {
                isolateDepth++;
                offset += length;
                continue;
            }

            if (type == BidiClass.Pdi)
            {
                if (isolateDepth > 0)
                {
                    isolateDepth--;
                }

                offset += length;
                continue;
            }

            if (isolateDepth > 0)
            {
                offset += length;
                continue;
            }

            if (type is BidiClass.R or BidiClass.Al)
            {
                return 1;
            }

            if (type == BidiClass.L)
            {
                return 0;
            }

            offset += length;
        }

        return 0;
    }

    private static BidiClass FindFirstStrong(string text, int offset)
    {
        var isolateDepth = 0;
        for (var i = offset; i < text.Length;)
        {
            var length = CodePointLength(text, i);
            var type = UnicodeBidiData.Get(ReadCodePoint(text, i));
            if (type is BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi)
            {
                isolateDepth++;
            }
            else if (type == BidiClass.Pdi)
            {
                if (isolateDepth == 0)
                {
                    break;
                }

                isolateDepth--;
            }
            else if (isolateDepth == 0 && type is BidiClass.L or BidiClass.R or BidiClass.Al)
            {
                return type == BidiClass.L ? BidiClass.L : BidiClass.R;
            }

            i += length;
        }

        return BidiClass.L;
    }

    private static int CodePointLength(string text, int offset)
        => offset + 1 < text.Length && char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;

    private static int ReadCodePoint(string text, int offset)
        => CodePointLength(text, offset) == 2
            ? char.ConvertToUtf32(text[offset], text[offset + 1])
            : text[offset];

    private static int NextEmbeddingLevel(int current, BidiClass direction)
    {
        var level = current + 1;
        var odd = direction == BidiClass.R;
        while ((level % 2 != 0) != odd)
        {
            level++;
        }

        return level;
    }

    private static BidiClass DirectionClass(int level) => level % 2 == 0 ? BidiClass.L : BidiClass.R;

    private static TextDirection Direction(int level) => level % 2 == 0 ? TextDirection.LeftToRight : TextDirection.RightToLeft;

    private sealed class BidiItem
    {
        internal BidiItem(int originalIndex, int start, int length, int codePoint, BidiClass type)
        {
            OriginalIndex = originalIndex;
            Start = start;
            Length = length;
            CodePoint = codePoint;
            Class = type;
            Type = type;
        }

        internal int OriginalIndex { get; }
        internal int Start { get; }
        internal int Length { get; }
        internal int CodePoint { get; }
        internal BidiClass Class { get; }
        internal BidiClass Type { get; set; }
        internal int Level { get; set; }
        internal bool Removed { get; set; }
        internal bool IsIsolateControl => Class is BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi or BidiClass.Pdi;
    }

    private readonly record struct IsolatingRunSequence(
        BidiItem[] Items,
        int Level,
        BidiClass Sos,
        BidiClass Eos);

    private readonly record struct PairedBracketPair(int OpenPosition, int ClosePosition);

    private readonly record struct EmbeddingState(int Level, BidiClass Override, bool Isolate, int InitiatorLevel);

}

internal enum BidiClass : byte
{
    None,
    L,
    R,
    Al,
    En,
    An,
    Es,
    Et,
    Cs,
    Nsm,
    Bn,
    B,
    S,
    Ws,
    On,
    Lre,
    Rle,
    Lro,
    Rlo,
    Pdf,
    Lri,
    Rli,
    Fsi,
    Pdi,
}

internal readonly record struct BidiRun(int Start, int Length, int Level, TextDirection Direction);

internal readonly record struct BidiConformanceResult(
    int ParagraphLevel,
    int[] Levels,
    int[] VisualOrder);
