using System.Globalization;
using System.Runtime.CompilerServices;
using SixLabors.Fonts.Unicode;

namespace Delta.Text;

internal enum UnicodeBreakType : byte
{
    None,
    Optional,
    Mandatory,
}

internal static class UnicodeLineBreakEngine
{
    public static void GetBreakOpportunities(ReadOnlySpan<int> codePoints, Span<UnicodeBreakType> breaks)
    {
        var length = codePoints.Length;

        if (breaks.Length < length + 1)
        {
            throw new ArgumentException($"breaks array must have length at least {length + 1}");
        }

        if (length == 0)
        {
            breaks[0] = UnicodeBreakType.Mandatory;
            return;
        }

        breaks[0] = UnicodeBreakType.None;
        breaks[length] = UnicodeBreakType.Mandatory;

        var beforeRaw = GetLineBreakClass(codePoints[0]);

        for (var i = 0; i < length - 1; i++)
        {
            var afterRaw = GetLineBreakClass(codePoints[i + 1]);
            breaks[i + 1] = GetBreakTypeCore(codePoints, i, beforeRaw, afterRaw);
            beforeRaw = afterRaw;
        }
    }

    /// <summary>Computes break opportunities, allocating a new result array.</summary>
    /// <param name="codePoints">Input codepoints to analyze.</param>
    /// <returns>Array of break types with length codePoints.Length + 1.</returns>
    public static UnicodeBreakType[] GetBreakOpportunities(ReadOnlySpan<int> codePoints)
    {
        var breaks = new UnicodeBreakType[codePoints.Length + 1];
        GetBreakOpportunities(codePoints, breaks);
        return breaks;
    }

    /// <summary>Checks if a line break is allowed at a specific position.</summary>
    /// <param name="codePoints">Input codepoints to analyze.</param>
    /// <param name="index">Position to check (0 = before first character).</param>
    /// <returns>The break type at this position.</returns>
    public static UnicodeBreakType GetBreakTypeAt(ReadOnlySpan<int> codePoints, int index)
    {
        if (index <= 0)
        {
            return UnicodeBreakType.None;
        }

        if (index >= codePoints.Length)
        {
            return UnicodeBreakType.Mandatory;
        }

        return GetBreakType(codePoints, index - 1);
    }

    /// <summary>Checks if a line break is allowed at a specific position (legacy bool API).</summary>
    public static bool CanBreakAt(ReadOnlySpan<int> codePoints, int index)
    {
        return GetBreakTypeAt(codePoints, index) != UnicodeBreakType.None;
    }

    private static LineBreakClass GetLineBreakClass(int codePoint)
    {
        return CodePoint.GetLineBreakClass(new CodePoint(codePoint));
    }

    private static UnicodeCategory GetGeneralCategory(int codePoint)
    {
        return CodePoint.GetGeneralCategory(new CodePoint(codePoint));
    }

    private static EastAsianWidthClass GetEastAsianWidth(int codePoint)
    {
        return CodePoint.GetEastAsianWidthClass(new CodePoint(codePoint));
    }

    private static bool IsExtendedPictographic(int codePoint)
    {
        return CodePoint.GetGraphemeClusterClass(new CodePoint(codePoint)) ==
               GraphemeClusterClass.ExtendedPictographic;
    }

    private static bool IsUnambiguousHyphen(int codePoint)
    {
        return GetLineBreakClass(codePoint) == LineBreakClass.UnambiguousHyphen;
    }

    private static bool IsDottedCircle(int codePoint)
    {
        return codePoint == 0x25CC;
    }

    private static bool IsBrahmicForLB28a(int codePoint)
    {
        var script = CodePoint.GetScriptClass(new CodePoint(codePoint));
        return script is ScriptClass.Balinese or ScriptClass.Batak or ScriptClass.Buginese or
            ScriptClass.Javanese or ScriptClass.KayahLi or ScriptClass.Makasar or
            ScriptClass.Mandaic or ScriptClass.Modi or ScriptClass.Nandinagari or
            ScriptClass.Sundanese or ScriptClass.TaiLe or ScriptClass.NewTaiLue or
            ScriptClass.Takri or ScriptClass.Tibetan;
    }

    #region Inline Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCM(LineBreakClass cls)
    {
        return cls == LineBreakClass.CombiningMark || cls == LineBreakClass.ZeroWidthJoiner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLB9Exception(LineBreakClass cls)
    {
        return cls == LineBreakClass.Space || cls == LineBreakClass.MandatoryBreak || cls == LineBreakClass.CarriageReturn ||
               cls == LineBreakClass.LineFeed || cls == LineBreakClass.NextLine || cls == LineBreakClass.ZeroWidthSpace ||
               cls == LineBreakClass.ZeroWidthJoiner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAlphabetic(LineBreakClass cls)
    {
        return cls == LineBreakClass.Alphabetic || cls == LineBreakClass.HebrewLetter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAksara(LineBreakClass cls)
    {
        return cls == LineBreakClass.Aksara || cls == LineBreakClass.AksaraStart;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsVirama(LineBreakClass cls)
    {
        return cls == LineBreakClass.ViramaFinal || cls == LineBreakClass.Virama;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumericAffix(LineBreakClass cls)
    {
        return cls == LineBreakClass.PostfixNumeric || cls == LineBreakClass.PrefixNumeric;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsKorean(LineBreakClass cls)
    {
        return cls == LineBreakClass.HangulLeadJamo || cls == LineBreakClass.HangulVowelJamo || cls == LineBreakClass.HangulTailJamo ||
               cls == LineBreakClass.HangulLeadVowelSyllable || cls == LineBreakClass.HangulLeadVowelTailSyllable;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEastAsianWide(EastAsianWidthClass eaw)
    {
        return eaw == EastAsianWidthClass.Wide || eaw == EastAsianWidthClass.Fullwidth;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEastAsianForLB19a(EastAsianWidthClass eaw)
    {
        return eaw == EastAsianWidthClass.Fullwidth || eaw == EastAsianWidthClass.Wide || eaw == EastAsianWidthClass.Halfwidth;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LineBreakClass ResolveClass(LineBreakClass cls)
    {
        return cls switch
        {
            LineBreakClass.CombiningMark or LineBreakClass.ZeroWidthJoiner => LineBreakClass.Alphabetic,
            LineBreakClass.Ambiguous or LineBreakClass.Surrogate or LineBreakClass.Unknown or LineBreakClass.ComplexContext => LineBreakClass.Alphabetic,
            LineBreakClass.ConditionalJapaneseStarter => LineBreakClass.Nonstarter,
            _ => cls
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsEffectivelyCombining(LineBreakClass cls, int cp)
    {
        if (IsCM(cls))
        {
            return true;
        }

        if (cls == LineBreakClass.ComplexContext)
        {
            var gc = GetGeneralCategory(cp);
            return gc == UnicodeCategory.NonSpacingMark || gc == UnicodeCategory.SpacingCombiningMark;
        }

        return false;
    }

    #endregion

    private static UnicodeBreakType GetBreakType(ReadOnlySpan<int> codePoints, int index)
    {
        var beforeRaw = GetLineBreakClass(codePoints[index]);
        var afterRaw = GetLineBreakClass(codePoints[index + 1]);
        return GetBreakTypeCore(codePoints, index, beforeRaw, afterRaw);
    }

    private readonly ref struct BreakContext
    {
        public readonly ReadOnlySpan<int> CodePoints;
        public readonly int Index;
        public readonly int EffectiveIndex;
        public readonly int BeforeCodePoint;
        public readonly int AfterCodePoint;
        public readonly int EffectiveCodePoint;
        public readonly LineBreakClass Before;
        public readonly LineBreakClass After;
        public readonly LineBreakClass BeforeRaw;
        public readonly LineBreakClass EffectiveBeforeRaw;

        public BreakContext(
            ReadOnlySpan<int> codePoints,
            int index,
            int effectiveIndex,
            int beforeCodePoint,
            int afterCodePoint,
            int effectiveCodePoint,
            LineBreakClass before,
            LineBreakClass after,
            LineBreakClass beforeRaw,
            LineBreakClass effectiveBeforeRaw)
        {
            CodePoints = codePoints;
            Index = index;
            EffectiveIndex = effectiveIndex;
            BeforeCodePoint = beforeCodePoint;
            AfterCodePoint = afterCodePoint;
            EffectiveCodePoint = effectiveCodePoint;
            Before = before;
            After = after;
            BeforeRaw = beforeRaw;
            EffectiveBeforeRaw = effectiveBeforeRaw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UnicodeBreakType GetBreakTypeCore(
        ReadOnlySpan<int> codePoints,
        int index,
        LineBreakClass beforeRaw,
        LineBreakClass afterRaw)
    {
        var beforeCodePoint = codePoints[index];
        var afterCodePoint = codePoints[index + 1];

        var initialDecision = TryBreakInitial(beforeRaw, afterRaw, afterCodePoint);
        if (initialDecision.HasValue)
        {
            return initialDecision.Value;
        }

        ResolveEffectiveBefore(
            codePoints,
            index,
            beforeRaw,
            beforeCodePoint,
            out var effectiveIndex,
            out var effectiveBeforeRaw,
            out var effectiveCodePoint);

        if (beforeRaw == LineBreakClass.ZeroWidthJoiner)
        {
            return UnicodeBreakType.None;
        }

        var context = new BreakContext(
            codePoints,
            index,
            effectiveIndex,
            beforeCodePoint,
            afterCodePoint,
            effectiveCodePoint,
            ResolveClass(effectiveBeforeRaw),
            ResolveClass(afterRaw),
            beforeRaw,
            effectiveBeforeRaw);

        var decision = TryBreakMandatoryAndGlue(context);
        if (decision.HasValue)
        {
            return decision.Value;
        }

        decision = TryBreakQuotesAndSpaces(context);
        if (decision.HasValue)
        {
            return decision.Value;
        }

        decision = TryBreakHyphens(context);
        if (decision.HasValue)
        {
            return decision.Value;
        }

        decision = TryBreakNumeric(context);
        if (decision.HasValue)
        {
            return decision.Value;
        }

        decision = TryBreakKorean(context);
        if (decision.HasValue)
        {
            return decision.Value;
        }

        decision = TryBreakIndicAndEastAsian(context);
        if (decision.HasValue)
        {
            return decision.Value;
        }

        decision = TryBreakEmoji(context);
        return decision ?? UnicodeBreakType.Optional;
    }

    private static UnicodeBreakType? TryBreakInitial(
        LineBreakClass beforeRaw,
        LineBreakClass afterRaw,
        int afterCodePoint)
    {
        if ((afterRaw == LineBreakClass.CombiningMark || afterRaw == LineBreakClass.ZeroWidthJoiner) &&
            !IsLB9Exception(beforeRaw))
        {
            return UnicodeBreakType.None;
        }

        if ((beforeRaw == LineBreakClass.Alphabetic && (afterRaw == LineBreakClass.Alphabetic ||
                                                         afterRaw == LineBreakClass.Numeric)) ||
            (beforeRaw == LineBreakClass.Numeric && (afterRaw == LineBreakClass.Alphabetic ||
                                                      afterRaw == LineBreakClass.Numeric)))
        {
            return UnicodeBreakType.None;
        }

        if (afterRaw == LineBreakClass.ComplexContext &&
            IsCombiningCategory(GetGeneralCategory(afterCodePoint)) &&
            !IsLB9Exception(beforeRaw))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCombiningCategory(UnicodeCategory category)
    {
        return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark;
    }

    private static void ResolveEffectiveBefore(
        ReadOnlySpan<int> codePoints,
        int index,
        LineBreakClass beforeRaw,
        int beforeCodePoint,
        out int effectiveIndex,
        out LineBreakClass effectiveBeforeRaw,
        out int effectiveCodePoint)
    {
        effectiveIndex = index;
        effectiveBeforeRaw = beforeRaw;
        effectiveCodePoint = beforeCodePoint;

        while (effectiveIndex > 0 && IsEffectivelyCombining(effectiveBeforeRaw, effectiveCodePoint))
        {
            effectiveIndex--;
            effectiveCodePoint = codePoints[effectiveIndex];
            effectiveBeforeRaw = GetLineBreakClass(effectiveCodePoint);
        }

        if (IsEffectivelyCombining(effectiveBeforeRaw, effectiveCodePoint))
        {
            effectiveBeforeRaw = LineBreakClass.Alphabetic;
        }

        if (IsLB9Exception(effectiveBeforeRaw) && IsEffectivelyCombining(beforeRaw, beforeCodePoint))
        {
            effectiveBeforeRaw = LineBreakClass.Alphabetic;
        }
    }

    private static UnicodeBreakType? TryBreakMandatoryAndGlue(BreakContext context)
    {
        var mandatoryDecision = TryBreakMandatory(context);
        return mandatoryDecision ?? TryBreakGlueAndPunctuation(context);
    }

    private static UnicodeBreakType? TryBreakMandatory(BreakContext context)
    {
        var hardBoundary = TryBreakHardBoundary(context);
        return hardBoundary ?? TryBreakZeroWidthBoundary(context);
    }

    private static UnicodeBreakType? TryBreakHardBoundary(BreakContext context)
    {
        if (context.Before == LineBreakClass.MandatoryBreak)
        {
            return UnicodeBreakType.Mandatory;
        }

        if (context.Before == LineBreakClass.CarriageReturn && context.After == LineBreakClass.LineFeed)
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.CarriageReturn ||
            context.Before == LineBreakClass.LineFeed ||
            context.Before == LineBreakClass.NextLine)
        {
            return UnicodeBreakType.Mandatory;
        }

        if (context.After == LineBreakClass.MandatoryBreak ||
            context.After == LineBreakClass.CarriageReturn ||
            context.After == LineBreakClass.LineFeed ||
            context.After == LineBreakClass.NextLine ||
            context.After == LineBreakClass.Space ||
            context.After == LineBreakClass.ZeroWidthSpace)
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakZeroWidthBoundary(BreakContext context)
    {
        if (context.Before == LineBreakClass.ZeroWidthSpace)
        {
            return UnicodeBreakType.Optional;
        }

        if (context.Before == LineBreakClass.Space && HasZeroWidthSpaceBefore(context))
        {
            return UnicodeBreakType.Optional;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakGlueAndPunctuation(BreakContext context)
    {
        if (IsWordJoinerOrGlue(context))
        {
            return UnicodeBreakType.None;
        }

        if (IsGlueBlocked(context))
        {
            return UnicodeBreakType.None;
        }

        if (IsClosingPunctuation(context.After))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.OpenPunctuation)
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.Space && HasOpenPunctuationBefore(context))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWordJoinerOrGlue(BreakContext context)
    {
        return context.Before == LineBreakClass.WordJoiner || context.After == LineBreakClass.WordJoiner ||
               context.Before == LineBreakClass.Glue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGlueBlocked(BreakContext context)
    {
        return context.After == LineBreakClass.Glue &&
               context.Before != LineBreakClass.Space &&
               context.Before != LineBreakClass.BreakAfter &&
               context.Before != LineBreakClass.UnambiguousHyphen &&
               context.Before != LineBreakClass.Hyphen &&
               !IsUnambiguousHyphen(context.EffectiveCodePoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsClosingPunctuation(LineBreakClass lineBreakClass)
    {
        return lineBreakClass == LineBreakClass.ClosePunctuation ||
               lineBreakClass == LineBreakClass.CloseParenthesis ||
               lineBreakClass == LineBreakClass.Exclamation ||
               lineBreakClass == LineBreakClass.BreakSymbols;
    }

    private static bool HasZeroWidthSpaceBefore(BreakContext context)
    {
        for (var index = context.EffectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(context.CodePoints[index]);
            if (lineBreakClass == LineBreakClass.Space)
            {
                continue;
            }

            return lineBreakClass == LineBreakClass.ZeroWidthSpace;
        }

        return false;
    }

    private static bool HasOpenPunctuationBefore(BreakContext context)
    {
        for (var index = context.EffectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(context.CodePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            var resolved = ResolveClass(lineBreakClass);
            if (resolved == LineBreakClass.OpenPunctuation)
            {
                return true;
            }

            if (resolved != LineBreakClass.Space)
            {
                return false;
            }
        }

        return false;
    }

    private static UnicodeBreakType? TryBreakQuotesAndSpaces(BreakContext context)
    {
        var quoteBeforeOpenDecision = TryBreakBeforeOpenQuote(context);
        if (quoteBeforeOpenDecision.HasValue)
        {
            return quoteBeforeOpenDecision.Value;
        }

        var spaceDecision = TryBreakSpaceRules(context);
        return spaceDecision ?? TryBreakQuoteRules(context);
    }

    private static UnicodeBreakType? TryBreakBeforeOpenQuote(BreakContext context)
    {
        if (context.After == LineBreakClass.OpenPunctuation &&
            CheckLB15(context.CodePoints, context.EffectiveIndex, context.Before))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakSpaceRules(BreakContext context)
    {
        if (context.Before == LineBreakClass.Space &&
            context.After == LineBreakClass.InfixNumeric &&
            LookAheadGetClass(context.CodePoints, context.Index + 2) == LineBreakClass.Numeric)
        {
            return UnicodeBreakType.Optional;
        }

        if (context.After == LineBreakClass.InfixNumeric)
        {
            return UnicodeBreakType.None;
        }

        if (context.After == LineBreakClass.Nonstarter &&
            CheckClosingBeforeNS(context.CodePoints, context.EffectiveIndex, context.Before))
        {
            return UnicodeBreakType.None;
        }

        if (context.After == LineBreakClass.BreakBeforeAndAfter &&
            CheckB2Pattern(context.CodePoints, context.EffectiveIndex, context.Before))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.Space)
        {
            return HandleLB18(context.CodePoints, context.Index, context.After, context.AfterCodePoint)
                ? UnicodeBreakType.Optional
                : UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakQuoteRules(BreakContext context)
    {
        if (context.After == LineBreakClass.Quotation &&
            !CanBreakBeforeQU(
                context.CodePoints,
                context.Index,
                context.AfterCodePoint,
                context.EffectiveCodePoint))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.Quotation &&
            !CanBreakAfterQU(
                context.CodePoints,
                context.EffectiveIndex,
                context.EffectiveCodePoint,
                context.AfterCodePoint))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakHyphens(BreakContext context)
    {
        var startDecision = TryBreakHyphenStart(context);
        return startDecision ?? TryBreakHyphenEnd(context);
    }

    private static UnicodeBreakType? TryBreakHyphenStart(BreakContext context)
    {
        if (context.Before == LineBreakClass.ContingentBreak ||
            context.After == LineBreakClass.ContingentBreak)
        {
            return UnicodeBreakType.Optional;
        }

        if ((context.Before == LineBreakClass.Hyphen || IsUnambiguousHyphen(context.EffectiveCodePoint)) &&
            IsWordInitialHyphen(context.CodePoints, context.EffectiveIndex) &&
            IsAlphabetic(context.After))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakHyphenEnd(BreakContext context)
    {
        if (context.After == LineBreakClass.BreakAfter ||
            context.After == LineBreakClass.Hyphen ||
            context.After == LineBreakClass.UnambiguousHyphen ||
            context.After == LineBreakClass.Nonstarter ||
            IsUnambiguousHyphen(context.AfterCodePoint) ||
            context.Before == LineBreakClass.BreakBefore)
        {
            return UnicodeBreakType.None;
        }

        if ((context.Before == LineBreakClass.Hyphen ||
             context.Before == LineBreakClass.UnambiguousHyphen ||
             IsUnambiguousHyphen(context.EffectiveCodePoint)) &&
            context.After != LineBreakClass.HebrewLetter &&
            IsHLBeforeHyphen(context.CodePoints, context.EffectiveIndex))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.BreakSymbols &&
            context.After == LineBreakClass.HebrewLetter)
        {
            return UnicodeBreakType.None;
        }

        if (context.After == LineBreakClass.Inseparable)
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakNumeric(BreakContext context)
    {
        var pairDecision = TryBreakNumericPairs(context);
        if (pairDecision.HasValue)
        {
            return pairDecision.Value;
        }

        if (!CheckLB25(
                context.CodePoints,
                context.Index,
                context.EffectiveIndex,
                context.Before,
                context.After))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakNumericPairs(BreakContext context)
    {
        if (IsAlphabeticNumericPair(context.Before, context.After))
        {
            return UnicodeBreakType.None;
        }

        if (IsNumericPrefixPair(context.Before, context.After))
        {
            return UnicodeBreakType.None;
        }

        if (IsNumericPostfixPair(context.Before, context.After))
        {
            return UnicodeBreakType.None;
        }

        if (IsNumericAffix(context.Before) && IsAlphabetic(context.After) ||
            IsAlphabetic(context.Before) && IsNumericAffix(context.After))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAlphabeticNumericPair(LineBreakClass before, LineBreakClass after)
    {
        return IsAlphabetic(before) && after == LineBreakClass.Numeric ||
               before == LineBreakClass.Numeric && IsAlphabetic(after);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumericPrefixPair(LineBreakClass before, LineBreakClass after)
    {
        return before == LineBreakClass.PrefixNumeric &&
               (after == LineBreakClass.Ideographic ||
                after == LineBreakClass.EmojiBase ||
                after == LineBreakClass.EmojiModifier);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumericPostfixPair(LineBreakClass before, LineBreakClass after)
    {
        return (before == LineBreakClass.Ideographic ||
                before == LineBreakClass.EmojiBase ||
                before == LineBreakClass.EmojiModifier) &&
               after == LineBreakClass.PostfixNumeric;
    }

    private static UnicodeBreakType? TryBreakKorean(BreakContext context)
    {
        var hangulDecision = TryBreakHangul(context);
        return hangulDecision ?? TryBreakKoreanPairs(context);
    }

    private static UnicodeBreakType? TryBreakHangul(BreakContext context)
    {
        if (context.Before == LineBreakClass.HangulLeadJamo &&
            (context.After == LineBreakClass.HangulLeadJamo ||
             context.After == LineBreakClass.HangulVowelJamo ||
             context.After == LineBreakClass.HangulLeadVowelSyllable ||
             context.After == LineBreakClass.HangulLeadVowelTailSyllable))
        {
            return UnicodeBreakType.None;
        }

        if ((context.Before == LineBreakClass.HangulVowelJamo ||
             context.Before == LineBreakClass.HangulLeadVowelSyllable) &&
            (context.After == LineBreakClass.HangulVowelJamo ||
             context.After == LineBreakClass.HangulTailJamo))
        {
            return UnicodeBreakType.None;
        }

        if ((context.Before == LineBreakClass.HangulTailJamo ||
             context.Before == LineBreakClass.HangulLeadVowelTailSyllable) &&
            context.After == LineBreakClass.HangulTailJamo)
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakKoreanPairs(BreakContext context)
    {
        if (IsKorean(context.Before) && context.After == LineBreakClass.PostfixNumeric)
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.PrefixNumeric && IsKorean(context.After))
        {
            return UnicodeBreakType.None;
        }

        if (IsAlphabetic(context.Before) && IsAlphabetic(context.After))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakIndicAndEastAsian(BreakContext context)
    {
        if (!CheckLB28a(
                context.CodePoints,
                context.Index,
                context.EffectiveIndex,
                context.Before,
                context.After,
                context.BeforeRaw,
                context.EffectiveBeforeRaw,
                context.BeforeCodePoint,
                context.AfterCodePoint,
                context.EffectiveCodePoint))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.InfixNumeric && IsAlphabetic(context.After))
        {
            return UnicodeBreakType.None;
        }

        if ((IsAlphabetic(context.Before) || context.Before == LineBreakClass.Numeric) &&
            context.After == LineBreakClass.OpenPunctuation &&
            !IsEastAsianWide(GetEastAsianWidth(context.AfterCodePoint)))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.CloseParenthesis &&
            (IsAlphabetic(context.After) || context.After == LineBreakClass.Numeric))
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static UnicodeBreakType? TryBreakEmoji(BreakContext context)
    {
        if ((context.EffectiveBeforeRaw == LineBreakClass.RegionalIndicator ||
             context.Before == LineBreakClass.RegionalIndicator) &&
            context.After == LineBreakClass.RegionalIndicator &&
            HasOddRegionalIndicatorRun(context))
        {
            return UnicodeBreakType.None;
        }

        if (context.Before == LineBreakClass.EmojiBase &&
            context.After == LineBreakClass.EmojiModifier)
        {
            return UnicodeBreakType.None;
        }

        if (context.After == LineBreakClass.EmojiModifier &&
            IsExtendedPictographic(context.EffectiveCodePoint) &&
            GetGeneralCategory(context.EffectiveCodePoint) == UnicodeCategory.OtherNotAssigned)
        {
            return UnicodeBreakType.None;
        }

        return null;
    }

    private static bool HasOddRegionalIndicatorRun(BreakContext context)
    {
        var regionalIndicatorCount = 1;
        for (var index = context.EffectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(context.CodePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            if (lineBreakClass == LineBreakClass.RegionalIndicator)
            {
                regionalIndicatorCount++;
                continue;
            }

            break;
        }

        return (regionalIndicatorCount & 1) == 1;
    }

    #region Rule Handlers

    private static LineBreakClass LookAheadGetClass(ReadOnlySpan<int> codePoints, int start)
    {
        for (var i = start; i < codePoints.Length; i++)
        {
            var cls = GetLineBreakClass(codePoints[i]);
            if (!IsCM(cls))
            {
                return ResolveClass(cls);
            }
        }

        return LineBreakClass.Unknown;
    }

    private static bool CheckLB15(ReadOnlySpan<int> codePoints, int effectiveIndex, LineBreakClass before)
    {
        var quoteIndex = FindOpeningQuoteBeforeSpaces(codePoints, effectiveIndex, before);
        if (quoteIndex < 0)
        {
            return false;
        }

        return quoteIndex == 0 || HasLineBreakBeforeQuote(codePoints, quoteIndex);
    }

    private static int FindOpeningQuoteBeforeSpaces(
        ReadOnlySpan<int> codePoints,
        int effectiveIndex,
        LineBreakClass before)
    {
        var previous = before;
        var quoteIndex = -1;
        for (var index = effectiveIndex; previous == LineBreakClass.Space && index > 0;)
        {
            index--;
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            previous = ResolveClass(lineBreakClass);
            if (previous == LineBreakClass.Quotation)
            {
                quoteIndex = index;
            }
        }

        if (previous != LineBreakClass.Quotation || quoteIndex < 0 ||
            GetGeneralCategory(codePoints[quoteIndex]) != UnicodeCategory.InitialQuotePunctuation)
        {
            return -1;
        }

        return quoteIndex;
    }

    private static bool HasLineBreakBeforeQuote(ReadOnlySpan<int> codePoints, int quoteIndex)
    {
        for (var index = quoteIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            return lineBreakClass == LineBreakClass.MandatoryBreak ||
                   lineBreakClass == LineBreakClass.CarriageReturn ||
                   lineBreakClass == LineBreakClass.LineFeed ||
                   lineBreakClass == LineBreakClass.NextLine ||
                   lineBreakClass == LineBreakClass.Space ||
                   lineBreakClass == LineBreakClass.ZeroWidthSpace ||
                   lineBreakClass == LineBreakClass.ContingentBreak ||
                   lineBreakClass == LineBreakClass.Glue;
        }

        return false;
    }

    private static bool CheckClosingBeforeNS(ReadOnlySpan<int> codePoints, int effectiveIndex, LineBreakClass before)
    {
        var prev = before;
        for (var i = effectiveIndex; (prev == LineBreakClass.Space || prev == LineBreakClass.Alphabetic) && i > 0;)
        {
            i--;
            var cls = GetLineBreakClass(codePoints[i]);
            if (IsCM(cls))
            {
                continue;
            }

            prev = ResolveClass(cls);
            if (prev != LineBreakClass.Space)
            {
                break;
            }
        }

        return prev == LineBreakClass.ClosePunctuation || prev == LineBreakClass.CloseParenthesis;
    }

    private static bool CheckB2Pattern(ReadOnlySpan<int> codePoints, int effectiveIndex, LineBreakClass before)
    {
        var prev = before;
        for (var i = effectiveIndex; (prev == LineBreakClass.Space || prev == LineBreakClass.Alphabetic) && i > 0;)
        {
            i--;
            var cls = GetLineBreakClass(codePoints[i]);
            if (IsCM(cls))
            {
                continue;
            }

            prev = ResolveClass(cls);
            if (prev != LineBreakClass.Space)
            {
                break;
            }
        }

        return prev == LineBreakClass.BreakBeforeAndAfter;
    }

    private static bool HandleLB18(ReadOnlySpan<int> codePoints, int index, LineBreakClass after, int afterCp)
    {
        if (IsClosingQuoteBlocked(codePoints, index, after, afterCp))
        {
            return false;
        }

        return !IsOpeningSequenceBlocked(codePoints, index);
    }

    private static bool IsClosingQuoteBlocked(
        ReadOnlySpan<int> codePoints,
        int index,
        LineBreakClass after,
        int afterCp)
    {
        if (after != LineBreakClass.Quotation ||
            GetGeneralCategory(afterCp) != UnicodeCategory.FinalQuotePunctuation)
        {
            return false;
        }

        var nextClass = LookAheadGetClass(codePoints, index + 2);
        for (var nextIndex = index + 2; nextIndex < codePoints.Length; nextIndex++)
        {
            if (!IsCM(GetLineBreakClass(codePoints[nextIndex])))
            {
                return IsLB18ForbiddenFollowing(nextClass);
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLB18ForbiddenFollowing(LineBreakClass cls)
    {
        return cls == LineBreakClass.Space || cls == LineBreakClass.Glue ||
               cls == LineBreakClass.WordJoiner || cls == LineBreakClass.ClosePunctuation ||
               cls == LineBreakClass.Quotation || cls == LineBreakClass.CloseParenthesis ||
               cls == LineBreakClass.Exclamation || cls == LineBreakClass.InfixNumeric ||
               cls == LineBreakClass.BreakSymbols || cls == LineBreakClass.MandatoryBreak ||
               cls == LineBreakClass.CarriageReturn || cls == LineBreakClass.LineFeed ||
               cls == LineBreakClass.NextLine || cls == LineBreakClass.ZeroWidthSpace;
    }

    private static bool IsOpeningSequenceBlocked(ReadOnlySpan<int> codePoints, int index)
    {
        for (var currentIndex = index - 1; currentIndex >= 0; currentIndex--)
        {
            var currentClass = GetLineBreakClass(codePoints[currentIndex]);
            if (currentClass == LineBreakClass.Space || IsCM(currentClass))
            {
                continue;
            }

            if (!IsOpeningQuoteSequence(codePoints, currentIndex, currentClass))
            {
                return false;
            }

            return currentIndex == 0 || HasForbiddenPreviousForOpening(codePoints, currentIndex);
        }

        return false;
    }

    private static bool IsOpeningQuoteSequence(
        ReadOnlySpan<int> codePoints,
        int index,
        LineBreakClass lineBreakClass)
    {
        return lineBreakClass == LineBreakClass.OpenPunctuation ||
               (lineBreakClass == LineBreakClass.Quotation &&
                GetGeneralCategory(codePoints[index]) == UnicodeCategory.InitialQuotePunctuation);
    }

    private static bool HasForbiddenPreviousForOpening(ReadOnlySpan<int> codePoints, int openingIndex)
    {
        for (var index = openingIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            return lineBreakClass == LineBreakClass.MandatoryBreak ||
                   lineBreakClass == LineBreakClass.CarriageReturn ||
                   lineBreakClass == LineBreakClass.LineFeed ||
                   lineBreakClass == LineBreakClass.NextLine ||
                   lineBreakClass == LineBreakClass.OpenPunctuation ||
                   lineBreakClass == LineBreakClass.Quotation ||
                   lineBreakClass == LineBreakClass.Glue ||
                   lineBreakClass == LineBreakClass.Space ||
                   lineBreakClass == LineBreakClass.ZeroWidthSpace ||
                   lineBreakClass == LineBreakClass.ContingentBreak;
        }

        return false;
    }

    private static bool CanBreakBeforeQU(ReadOnlySpan<int> codePoints, int index, int afterCp, int effectiveCp)
    {
        if (GetGeneralCategory(afterCp) != UnicodeCategory.InitialQuotePunctuation)
        {
            return false;
        }

        if (!IsEastAsianForLB19a(GetEastAsianWidth(effectiveCp)))
        {
            return false;
        }

        for (var i = index + 2; i < codePoints.Length; i++)
        {
            var cls = GetLineBreakClass(codePoints[i]);
            if (IsCM(cls))
            {
                continue;
            }

            return IsEastAsianForLB19a(GetEastAsianWidth(codePoints[i]));
        }

        return false;
    }

    private static bool CanBreakAfterQU(ReadOnlySpan<int> codePoints, int effectiveIndex, int effectiveCp, int afterCp)
    {
        if (GetGeneralCategory(effectiveCp) != UnicodeCategory.FinalQuotePunctuation)
        {
            return false;
        }

        if (!IsEastAsianForLB19a(GetEastAsianWidth(afterCp)))
        {
            return false;
        }

        for (var i = effectiveIndex - 1; i >= 0; i--)
        {
            var cls = GetLineBreakClass(codePoints[i]);
            if (IsCM(cls))
            {
                continue;
            }

            return IsEastAsianForLB19a(GetEastAsianWidth(codePoints[i]));
        }

        return false;
    }

    private static bool IsWordInitialHyphen(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        if (effectiveIndex == 0)
        {
            return true;
        }

        var prev = ResolveClass(GetLineBreakClass(codePoints[effectiveIndex - 1]));
        return prev == LineBreakClass.MandatoryBreak || prev == LineBreakClass.CarriageReturn ||
               prev == LineBreakClass.LineFeed || prev == LineBreakClass.NextLine ||
               prev == LineBreakClass.Space || prev == LineBreakClass.ZeroWidthSpace ||
               prev == LineBreakClass.ContingentBreak || prev == LineBreakClass.Glue;
    }

    private static bool IsHLBeforeHyphen(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        for (var i = effectiveIndex - 1; i >= 0; i--)
        {
            var cls = GetLineBreakClass(codePoints[i]);
            if (IsCM(cls))
            {
                continue;
            }

            return ResolveClass(cls) == LineBreakClass.HebrewLetter;
        }

        return false;
    }

    private static bool CheckLB25(ReadOnlySpan<int> codePoints, int index, int effectiveIndex,
        LineBreakClass before, LineBreakClass after)
    {
        if (HasDirectNumericConflict(before, after))
        {
            return false;
        }

        if (before == LineBreakClass.BreakSymbols && after == LineBreakClass.Numeric)
        {
            if (HasNumericBeforeBreakSymbols(codePoints, effectiveIndex))
            {
                return false;
            }
        }

        if (IsNumericAffix(after) && HasNumericBeforeAffix(codePoints, effectiveIndex))
        {
            return false;
        }

        if ((before == LineBreakClass.ClosePunctuation || before == LineBreakClass.CloseParenthesis) &&
            IsNumericAffix(after) && HasNumericAfterClosing(codePoints, effectiveIndex))
        {
            return false;
        }

        if (IsNumericAffix(before) && after == LineBreakClass.OpenPunctuation &&
            HasNumericAfterAffixOpening(codePoints, index))
        {
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasDirectNumericConflict(LineBreakClass before, LineBreakClass after)
    {
        return before == LineBreakClass.Numeric && IsNumericAffix(after) ||
               IsNumericAffix(before) && after == LineBreakClass.Numeric ||
               (before == LineBreakClass.Hyphen || before == LineBreakClass.InfixNumeric) &&
               after == LineBreakClass.Numeric ||
               before == LineBreakClass.Numeric &&
               (after == LineBreakClass.Numeric || after == LineBreakClass.BreakSymbols ||
                after == LineBreakClass.InfixNumeric);
    }

    private static bool HasNumericBeforeBreakSymbols(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        for (var index = effectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            if (lineBreakClass == LineBreakClass.Numeric)
            {
                return true;
            }

            if (lineBreakClass != LineBreakClass.BreakSymbols &&
                lineBreakClass != LineBreakClass.InfixNumeric)
            {
                break;
            }
        }

        return false;
    }

    private static bool HasNumericBeforeAffix(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        for (var index = effectiveIndex; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            if (lineBreakClass == LineBreakClass.Numeric)
            {
                return true;
            }

            if (lineBreakClass != LineBreakClass.BreakSymbols &&
                lineBreakClass != LineBreakClass.InfixNumeric &&
                lineBreakClass != LineBreakClass.ClosePunctuation &&
                lineBreakClass != LineBreakClass.CloseParenthesis)
            {
                break;
            }
        }

        return false;
    }

    private static bool HasNumericAfterClosing(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        for (var index = effectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (lineBreakClass == LineBreakClass.Numeric)
            {
                return true;
            }

            if (lineBreakClass == LineBreakClass.OpenPunctuation ||
                lineBreakClass == LineBreakClass.MandatoryBreak ||
                lineBreakClass == LineBreakClass.CarriageReturn ||
                lineBreakClass == LineBreakClass.LineFeed ||
                lineBreakClass == LineBreakClass.NextLine ||
                lineBreakClass == LineBreakClass.Space)
            {
                break;
            }
        }

        return false;
    }

    private static bool HasNumericAfterAffixOpening(ReadOnlySpan<int> codePoints, int index)
    {
        for (var scanIndex = index + 2; scanIndex < codePoints.Length; scanIndex++)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[scanIndex]);
            if (lineBreakClass == LineBreakClass.Numeric)
            {
                return true;
            }

            if (lineBreakClass == LineBreakClass.ClosePunctuation ||
                lineBreakClass == LineBreakClass.CloseParenthesis ||
                lineBreakClass == LineBreakClass.MandatoryBreak ||
                lineBreakClass == LineBreakClass.CarriageReturn ||
                lineBreakClass == LineBreakClass.LineFeed ||
                lineBreakClass == LineBreakClass.NextLine)
            {
                break;
            }
        }

        return false;
    }

    private static bool CheckLB28a(ReadOnlySpan<int> codePoints, int index, int effectiveIndex,
        LineBreakClass before, LineBreakClass after,
        LineBreakClass beforeRaw, LineBreakClass effectiveBeforeRaw,
        int beforeCp, int afterCp, int effectiveCp)
    {
        if (IsAksaraViramaBlocked(before, after))
        {
            return false;
        }

        if (before == LineBreakClass.Virama && IsAksara(after) &&
            HasPreviousIndicLink(codePoints, effectiveIndex))
        {
            return false;
        }

        var brahmicDecision = TryBrahmicJoining(
                codePoints,
                index,
                effectiveIndex,
                beforeRaw,
                effectiveBeforeRaw,
                beforeCp,
                after);
        if (brahmicDecision.HasValue)
        {
            return brahmicDecision.Value;
        }

        if (before == LineBreakClass.AksaraPrebase && IsDottedCircle(afterCp))
        {
            return false;
        }

        if (IsDottedCircle(effectiveCp) && IsVirama(after))
        {
            return false;
        }

        if (beforeRaw == LineBreakClass.Virama &&
            HasPreviousAksaraOrDottedCircle(codePoints, index))
        {
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAksaraViramaBlocked(LineBreakClass before, LineBreakClass after)
    {
        return before == LineBreakClass.AksaraPrebase && IsAksara(after) ||
               IsAksara(before) && IsVirama(after);
    }

    private static bool? TryBrahmicJoining(
        ReadOnlySpan<int> codePoints,
        int index,
        int effectiveIndex,
        LineBreakClass beforeRaw,
        LineBreakClass effectiveBeforeRaw,
        int beforeCp,
        LineBreakClass after)
    {
        if (beforeRaw != LineBreakClass.CombiningMark ||
            !IsAksara(after) ||
            !IsAksara(effectiveBeforeRaw) ||
            !IsBrahmicForLB28a(beforeCp) ||
            !IsVirama(LookAheadGetClass(codePoints, index + 2)))
        {
            return null;
        }

        return HasPreviousCombiningVirama(codePoints, effectiveIndex);
    }

    private static bool HasPreviousIndicLink(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        for (var index = effectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            return IsAksara(lineBreakClass) || lineBreakClass == LineBreakClass.Virama;
        }

        return false;
    }

    private static bool HasPreviousCombiningVirama(ReadOnlySpan<int> codePoints, int effectiveIndex)
    {
        var foundCombiningMark = false;
        for (var index = effectiveIndex - 1; index >= 0; index--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[index]);
            if (IsCM(lineBreakClass))
            {
                foundCombiningMark = true;
                continue;
            }

            return lineBreakClass == LineBreakClass.Virama && foundCombiningMark;
        }

        return false;
    }

    private static bool HasPreviousAksaraOrDottedCircle(ReadOnlySpan<int> codePoints, int index)
    {
        for (var previousIndex = index - 1; previousIndex >= 0; previousIndex--)
        {
            var lineBreakClass = GetLineBreakClass(codePoints[previousIndex]);
            if (IsCM(lineBreakClass))
            {
                continue;
            }

            return IsAksara(lineBreakClass) || IsDottedCircle(codePoints[previousIndex]);
        }

        return false;
    }

    #endregion
}
