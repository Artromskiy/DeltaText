using Delta.Text.Contract;

namespace Delta.Text;

internal static class BidiResolver
{
    private const byte MaximumExplicitLevel = 125;

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

        ResolveWeakTypes(items);
        ResolveNeutralTypes(items);
        items = RemoveFormatting(items, retainIsolates: false);
        if (items.Length == 0)
        {
            return [new BidiRun(0, text.Length, baseLevel, Direction(baseLevel))];
        }

        ResolveImplicitLevels(items);
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
        var stack = new List<EmbeddingState>(8) { new(baseLevel, BidiClass.None, false) };
        var overflowIsolateCount = 0;
        var overflowEmbeddingCount = 0;
        var validIsolateCount = 0;

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var state = stack[^1];
            item.Level = state.Level;
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
                        stack.Add(new EmbeddingState(nextLevel, overrideType, false));
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
                item.Type = BidiClass.Isolate;
                var direction = item.Class == BidiClass.Lri
                    ? BidiClass.L
                    : item.Class == BidiClass.Rli ? BidiClass.R : FindFirstStrong(text, item.Start + item.Length, baseLevel);
                var nextLevel = NextEmbeddingLevel(state.Level, direction);
                if (overflowIsolateCount > 0)
                {
                    overflowIsolateCount++;
                }
                else if (nextLevel <= MaximumExplicitLevel && overflowEmbeddingCount == 0)
                {
                    stack.Add(new EmbeddingState(nextLevel, BidiClass.None, true));
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
                item.Type = BidiClass.Isolate;
                if (overflowIsolateCount > 0)
                {
                    overflowIsolateCount--;
                }
                else if (validIsolateCount > 0)
                {
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

    private static void ResolveWeakTypes(BidiItem[] items)
    {
        var start = 0;
        while (start < items.Length)
        {
            if (items[start].Type == BidiClass.Isolate)
            {
                start++;
                continue;
            }

            var end = start + 1;
            while (end < items.Length && items[end].Type != BidiClass.Isolate)
            {
                end++;
            }

            ResolveWeakSequence(items, start, end);
            start = end;
        }
    }

    private static void ResolveWeakSequence(BidiItem[] items, int start, int end)
    {
        var sos = DirectionClass(items[start].Level);
        var previous = sos;
        for (var i = start; i < end; i++)
        {
            var item = items[i];
            if (item.Type == BidiClass.Nsm)
            {
                item.Type = previous is BidiClass.Isolate or BidiClass.Bn ? sos : previous;
            }

            previous = item.Type;
        }

        var lastStrong = sos;
        for (var i = start; i < end; i++)
        {
            var item = items[i];
            if (item.Type is BidiClass.R or BidiClass.Al)
            {
                lastStrong = BidiClass.R;
            }
            else if (item.Type == BidiClass.En && lastStrong == BidiClass.R)
            {
                item.Type = BidiClass.An;
            }
        }

        for (var i = start; i < end; i++)
        {
            if (items[i].Type == BidiClass.Al)
            {
                items[i].Type = BidiClass.R;
            }
        }

        for (var i = start + 1; i + 1 < end; i++)
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

        for (var i = start; i < end;)
        {
            if (items[i].Type != BidiClass.Et)
            {
                i++;
                continue;
            }

            var sequenceEnd = i + 1;
            while (sequenceEnd < end && items[sequenceEnd].Type == BidiClass.Et)
            {
                sequenceEnd++;
            }

            if ((i > start && items[i - 1].Type == BidiClass.En)
                || (sequenceEnd < end && items[sequenceEnd].Type == BidiClass.En))
            {
                for (var j = i; j < sequenceEnd; j++)
                {
                    items[j].Type = BidiClass.En;
                }
            }

            i = sequenceEnd;
        }

        for (var i = start; i < end; i++)
        {
            if (items[i].Type is BidiClass.Es or BidiClass.Et or BidiClass.Cs)
            {
                items[i].Type = BidiClass.On;
            }
        }

        lastStrong = sos;
        for (var i = start; i < end; i++)
        {
            var item = items[i];
            if (item.Type is BidiClass.L or BidiClass.R)
            {
                lastStrong = item.Type;
            }
            else if (item.Type == BidiClass.En && lastStrong == BidiClass.L)
            {
                item.Type = BidiClass.L;
            }
        }
    }

    private static void ResolveNeutralTypes(BidiItem[] items)
    {
        var start = 0;
        while (start < items.Length)
        {
            if (items[start].Type == BidiClass.Isolate)
            {
                start++;
                continue;
            }

            var end = start + 1;
            while (end < items.Length && items[end].Type != BidiClass.Isolate)
            {
                end++;
            }

            ResolveBracketPairs(items, start, end);
            ResolveNeutralSequence(items, start, end);
            start = end;
        }
    }

    private static void ResolveBracketPairs(BidiItem[] items, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (!UnicodeBidiData.TryGetPairedBracket(items[i].CodePoint, out var pair) || !IsNeutral(items[i].Type))
            {
                continue;
            }

            var depth = 0;
            for (var j = i + 1; j < end; j++)
            {
                if (items[j].CodePoint == items[i].CodePoint)
                {
                    depth++;
                }
                else if (items[j].CodePoint == pair)
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    if (!IsNeutral(items[j].Type))
                    {
                        break;
                    }

                    var before = StrongTypeBefore(items, i - 1, start, DirectionClass(items[i].Level));
                    var after = StrongTypeAfter(items, j + 1, end, DirectionClass(items[i].Level));
                    var inside = StrongTypeInside(items, i + 1, j);
                    var resolved = before == after
                        ? before
                        : inside == before || inside == after ? inside : DirectionClass(items[i].Level);
                    items[i].Type = resolved;
                    items[j].Type = resolved;
                    break;
                }
            }
        }
    }

    private static void ResolveNeutralSequence(BidiItem[] items, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            if (!IsNeutral(items[i].Type))
            {
                i++;
                continue;
            }

            var sequenceEnd = i + 1;
            while (sequenceEnd < end && IsNeutral(items[sequenceEnd].Type))
            {
                sequenceEnd++;
            }

            var before = StrongTypeBefore(items, i - 1, start, DirectionClass(items[i].Level));
            var after = StrongTypeAfter(items, sequenceEnd, end, DirectionClass(items[i].Level));
            var resolved = before == after ? before : DirectionClass(items[i].Level);
            for (var j = i; j < sequenceEnd; j++)
            {
                items[j].Type = resolved;
            }

            i = sequenceEnd;
        }
    }

    private static BidiClass StrongTypeBefore(BidiItem[] items, int index, int start, BidiClass fallback)
    {
        for (var i = index; i >= start; i--)
        {
            if (items[i].Type is BidiClass.L or BidiClass.R)
            {
                return items[i].Type;
            }

            if (items[i].Type is BidiClass.En or BidiClass.An)
            {
                return BidiClass.R;
            }
        }

        return fallback;
    }

    private static BidiClass StrongTypeAfter(BidiItem[] items, int index, int end, BidiClass fallback)
    {
        for (var i = index; i < end; i++)
        {
            if (items[i].Type is BidiClass.L or BidiClass.R)
            {
                return items[i].Type;
            }

            if (items[i].Type is BidiClass.En or BidiClass.An)
            {
                return BidiClass.R;
            }
        }

        return fallback;
    }

    private static BidiClass StrongTypeInside(BidiItem[] items, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (items[i].Type is BidiClass.L or BidiClass.R)
            {
                return items[i].Type;
            }

            if (items[i].Type is BidiClass.En or BidiClass.An)
            {
                return BidiClass.R;
            }
        }

        return BidiClass.None;
    }

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
            items.Add(new BidiItem(offset, length, codepoint, UnicodeBidiData.Get(codepoint)));
            offset += length;
        }

        return items.ToArray();
    }

    private static BidiItem[] RemoveFormatting(BidiItem[] items, bool retainIsolates)
    {
        var count = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].Removed || retainIsolates && items[i].Type == BidiClass.Isolate)
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
            if (item.Type != BidiClass.Isolate)
            {
                return true;
            }
        }

        return false;
    }

    private static int ParagraphLevel(string text)
    {
        for (var offset = 0; offset < text.Length;)
        {
            var length = CodePointLength(text, offset);
            var type = UnicodeBidiData.Get(ReadCodePoint(text, offset));
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

    private static BidiClass FindFirstStrong(string text, int offset, int baseLevel)
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

        return DirectionClass(baseLevel);
    }

    private static int CodePointLength(string text, int offset)
        => offset + 1 < text.Length && char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;

    private static int ReadCodePoint(string text, int offset)
        => CodePointLength(text, offset) == 2
            ? char.ConvertToUtf32(text[offset], text[offset + 1])
            : text[offset];

    private static bool IsNeutral(BidiClass type) => type is BidiClass.B or BidiClass.S or BidiClass.Ws or BidiClass.On;

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
        internal BidiItem(int start, int length, int codePoint, BidiClass type)
        {
            Start = start;
            Length = length;
            CodePoint = codePoint;
            Class = type;
            Type = type;
        }

        internal int Start { get; }
        internal int Length { get; }
        internal int CodePoint { get; }
        internal BidiClass Class { get; }
        internal BidiClass Type { get; set; }
        internal int Level { get; set; }
        internal bool Removed { get; set; }
    }

    private readonly record struct EmbeddingState(int Level, BidiClass Override, bool Isolate);
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
    Isolate,
}

internal readonly record struct BidiRun(int Start, int Length, int Level, TextDirection Direction);
