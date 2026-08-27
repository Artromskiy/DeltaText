using System.Globalization;

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
        var activeCount = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (!items[i].Removed)
            {
                items[activeCount++] = items[i];
            }
        }

        if (activeCount == 0)
        {
            return [new BidiRun(0, text.Length, baseLevel, Direction(baseLevel))];
        }

        if (activeCount != items.Length)
        {
            Array.Resize(ref items, activeCount);
        }

        ResolveWeakTypes(items, baseLevel);
        ResolveNeutralTypes(items, baseLevel);
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
                if (nextLevel <= MaximumExplicitLevel)
                {
                    stack.Add(new EmbeddingState(nextLevel,
                        item.Class is BidiClass.Lro or BidiClass.Rlo ? direction : BidiClass.None,
                        false));
                }

                continue;
            }

            if (item.Class == BidiClass.Pdf)
            {
                item.Removed = true;
                if (stack.Count > 1 && !stack[^1].Isolate)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            if (item.Class is BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi)
            {
                item.Removed = true;
                var direction = item.Class == BidiClass.Lri
                    ? BidiClass.L
                    : item.Class == BidiClass.Rli ? BidiClass.R : FindFirstStrong(text, item.Start + item.Length, baseLevel);
                var nextLevel = NextEmbeddingLevel(state.Level, direction);
                if (nextLevel <= MaximumExplicitLevel)
                {
                    stack.Add(new EmbeddingState(nextLevel, BidiClass.None, true));
                }

                continue;
            }

            if (item.Class == BidiClass.Pdi)
            {
                item.Removed = true;
                while (stack.Count > 1)
                {
                    var wasIsolate = stack[^1].Isolate;
                    stack.RemoveAt(stack.Count - 1);
                    if (wasIsolate)
                    {
                        break;
                    }
                }

                continue;
            }

            if (state.Override != BidiClass.None && item.Class is not (BidiClass.Bn or BidiClass.B or BidiClass.S))
            {
                item.Type = state.Override;
            }
        }
    }

    private static void ResolveWeakTypes(BidiItem[] items, int baseLevel)
    {
        var previous = baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;
        var lastStrong = previous;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item.Type == BidiClass.Bn)
            {
                continue;
            }

            if (item.Type == BidiClass.Nsm)
            {
                item.Type = previous is BidiClass.Isolate or BidiClass.Bn ?
                    (baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R) : previous;
            }

            if (item.Type == BidiClass.En && lastStrong == BidiClass.R)
            {
                item.Type = BidiClass.An;
            }

            if (item.Type == BidiClass.Al)
            {
                item.Type = BidiClass.R;
            }

            if (item.Type is BidiClass.L or BidiClass.R)
            {
                lastStrong = item.Type;
            }

            previous = item.Type;
        }

        for (var i = 1; i + 1 < items.Length; i++)
        {
            if (items[i].Type == BidiClass.Es && items[i - 1].Type == BidiClass.En && items[i + 1].Type == BidiClass.En)
            {
                items[i].Type = BidiClass.En;
            }
            else if (items[i].Type == BidiClass.Cs && items[i - 1].Type == items[i + 1].Type
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

            if ((i > 0 && items[i - 1].Type == BidiClass.En) || (end < items.Length && items[end].Type == BidiClass.En))
            {
                for (var j = i; j < end; j++)
                {
                    items[j].Type = BidiClass.En;
                }
            }

            i = end;
        }

        foreach (var item in items)
        {
            if (item.Type is BidiClass.Es or BidiClass.Et or BidiClass.Cs)
            {
                item.Type = BidiClass.On;
            }
        }

        lastStrong = baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;
        foreach (var item in items)
        {
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

    private static void ResolveNeutralTypes(BidiItem[] items, int baseLevel)
    {
        for (var i = 0; i < items.Length;)
        {
            if (!IsNeutral(items[i].Type))
            {
                i++;
                continue;
            }

            var end = i + 1;
            while (end < items.Length && IsNeutral(items[end].Type))
            {
                end++;
            }

            var before = StrongForNeutralBefore(items, i - 1, baseLevel);
            var after = StrongForNeutralAfter(items, end, baseLevel);
            var resolved = before == after ? before : (baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R);
            for (var j = i; j < end; j++)
            {
                items[j].Type = resolved;
            }

            i = end;
        }
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
            var type = Classify(text, offset, codepoint);
            items.Add(new BidiItem(offset, length, type));
            offset += length;
        }

        return items.ToArray();
    }

    private static int ParagraphLevel(string text)
    {
        for (var offset = 0; offset < text.Length;)
        {
            var length = CodePointLength(text, offset);
            var codepoint = ReadCodePoint(text, offset);
            var type = Classify(text, offset, codepoint);
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
        for (var i = offset; i < text.Length;)
        {
            var length = CodePointLength(text, i);
            var codepoint = ReadCodePoint(text, i);
            var type = Classify(text, i, codepoint);
            if (type is BidiClass.L or BidiClass.R or BidiClass.Al)
            {
                return type == BidiClass.L ? BidiClass.L : BidiClass.R;
            }

            if (type == BidiClass.Pdi)
            {
                break;
            }

            i += length;
        }

        return baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;
    }

    private static int CodePointLength(string text, int offset)
        => offset + 1 < text.Length && char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;

    private static int ReadCodePoint(string text, int offset)
        => CodePointLength(text, offset) == 2
            ? char.ConvertToUtf32(text[offset], text[offset + 1])
            : text[offset];

    private static BidiClass StrongForNeutralBefore(BidiItem[] items, int index, int baseLevel)
    {
        for (var i = index; i >= 0; i--)
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

        return baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;
    }

    private static BidiClass StrongForNeutralAfter(BidiItem[] items, int index, int baseLevel)
    {
        for (var i = index; i < items.Length; i++)
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

        return baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;
    }

    private static bool IsNeutral(BidiClass type) =>
        type is BidiClass.B or BidiClass.S or BidiClass.Ws or BidiClass.On or BidiClass.Bn or BidiClass.Isolate;

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

    private static TextDirection Direction(int level) => level % 2 == 0 ? TextDirection.LeftToRight : TextDirection.RightToLeft;

    private static BidiClass Classify(string text, int offset, int codepoint)
    {
        switch (codepoint)
        {
            case 0x0009:
                return BidiClass.S;
            case 0x000A:
            case 0x000D:
            case 0x0085:
            case 0x2028:
            case 0x2029:
                return BidiClass.B;
            case 0x061C:
                return BidiClass.Al;
            case 0x200E:
                return BidiClass.L;
            case 0x200F:
                return BidiClass.R;
            case 0x202A:
                return BidiClass.Lre;
            case 0x202B:
                return BidiClass.Rle;
            case 0x202C:
                return BidiClass.Pdf;
            case 0x202D:
                return BidiClass.Lro;
            case 0x202E:
                return BidiClass.Rlo;
            case 0x2066:
                return BidiClass.Lri;
            case 0x2067:
                return BidiClass.Rli;
            case 0x2068:
                return BidiClass.Fsi;
            case 0x2069:
                return BidiClass.Pdi;
            case 0x2060:
            case 0x2061:
            case 0x2062:
            case 0x2063:
            case 0x2064:
            case 0x206A:
            case 0x206B:
            case 0x206C:
            case 0x206D:
            case 0x206E:
            case 0x206F:
            case 0xFEFF:
                return BidiClass.Bn;
        }

        if (codepoint is >= 0x30 and <= 0x39
            || codepoint is >= 0x660 and <= 0x669
            || codepoint is >= 0x6F0 and <= 0x6F9
            || codepoint is >= 0x1D7CE and <= 0x1D7FF)
        {
            return codepoint is >= 0x660 and <= 0x669 or >= 0x6F0 and <= 0x6F9
                ? BidiClass.An
                : BidiClass.En;
        }

        if (codepoint is 0x002B or 0x002D or 0x2212)
        {
            return BidiClass.Es;
        }

        if (codepoint is 0x002C or 0x002E or 0x003A or 0x003B or 0x060C or 0x066B or 0x066C)
        {
            return BidiClass.Cs;
        }

        if (codepoint is 0x0024 or 0x00A2 or 0x00A3 or 0x00A4 or 0x00A5 or 0x20A0 or 0x20CF or 0x20D0)
        {
            return BidiClass.Et;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(text, offset);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
        {
            return BidiClass.Nsm;
        }

        if (category == UnicodeCategory.DecimalDigitNumber)
        {
            return BidiClass.En;
        }

        if (IsRtl(codepoint))
        {
            return IsArabic(codepoint) ? BidiClass.Al : BidiClass.R;
        }

        if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber)
        {
            return BidiClass.L;
        }

        if (category == UnicodeCategory.SpaceSeparator)
        {
            return BidiClass.Ws;
        }

        if (category is UnicodeCategory.Control or UnicodeCategory.Format)
        {
            return BidiClass.Bn;
        }

        return codepoint switch
        {
            0x0020 or 0x00A0 or 0x1680 or 0x2000 or 0x2001 or 0x2002 or 0x2003 or 0x2004 or 0x2005
                or 0x2006 or 0x2007 or 0x2008 or 0x2009 or 0x200A or 0x202F or 0x205F or 0x3000 => BidiClass.Ws,
            _ => BidiClass.On,
        };
    }

    private static bool IsRtl(int codepoint) =>
        (codepoint >= 0x0590 && codepoint <= 0x08FF)
        || (codepoint >= 0xFB1D && codepoint <= 0xFEFC)
        || (codepoint >= 0x10800 && codepoint <= 0x10FFF)
        || (codepoint >= 0x1E800 && codepoint <= 0x1EEFF);

    private static bool IsArabic(int codepoint) =>
        (codepoint >= 0x0600 && codepoint <= 0x08FF)
        || (codepoint >= 0xFB50 && codepoint <= 0xFDFF)
        || (codepoint >= 0xFE70 && codepoint <= 0xFEFF)
        || (codepoint >= 0x1EE00 && codepoint <= 0x1EEFF);

    private sealed class BidiItem
    {
        internal BidiItem(int start, int length, BidiClass type)
        {
            Start = start;
            Length = length;
            Class = type;
            Type = type;
        }

        internal int Start { get; }
        internal int Length { get; }
        internal BidiClass Class { get; }
        internal BidiClass Type { get; set; }
        internal int Level { get; set; }
        internal bool Removed { get; set; }
    }

    private readonly record struct EmbeddingState(int Level, BidiClass Override, bool Isolate);

    private enum BidiClass : byte
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
}

internal readonly record struct BidiRun(int Start, int Length, int Level, TextDirection Direction);
