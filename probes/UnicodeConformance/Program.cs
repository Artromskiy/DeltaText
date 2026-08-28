using System.Globalization;
using System.Text;
using Delta.Text;
using Delta.Text.Contract;

const string DefaultGraphemePath = "/tmp/GraphemeBreakTest-17.0.0.txt";
const string DefaultLineBreakPath = "/tmp/LineBreakTest-17.0.0.txt";

if (args.Length != 0 && args.Length != 2)
{
    Console.Error.WriteLine("Usage: UnicodeConformance [GraphemeBreakTest.txt LineBreakTest.txt]");
    return 2;
}

var graphemePath = args.Length == 2 ? args[0] : DefaultGraphemePath;
var lineBreakPath = args.Length == 2 ? args[1] : DefaultLineBreakPath;

try
{
    RunBoundaryContractCases();
    var graphemeCases = RunGraphemeCases(graphemePath);
    var lineBreakCases = RunLineBreakCases(lineBreakPath);
    Console.WriteLine($"PASS GraphemeBreakTest: {graphemeCases} cases");
    Console.WriteLine($"PASS LineBreakTest: {lineBreakCases} cases");
    return 0;
}
catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
{
    Console.Error.WriteLine($"FAIL {exception.Message}");
    return 1;
}

static void RunBoundaryContractCases()
{
    var empty = UnicodeText.GetLineBreaks(ReadOnlyMemory<char>.Empty);
    if (empty.TextLengthUtf16 != 0 || empty.Opportunities.Length != 1 ||
        empty.Opportunities.Span[0].PositionUtf16 != 0 ||
        empty.Opportunities.Span[0].Kind != LineBreakKind.Mandatory)
    {
        throw new InvalidOperationException("Empty text must expose one mandatory final boundary.");
    }

    const string invalid = "\uD800";
    try
    {
        UnicodeText.SegmentGraphemes(invalid.AsMemory());
    }
    catch (ArgumentException)
    {
        return;
    }

    throw new InvalidOperationException("Unpaired UTF-16 surrogates must be rejected.");
}

static int RunGraphemeCases(string path)
{
    var count = 0;
    foreach (var testCase in ReadCases(path))
    {
        var text = ToText(testCase.CodePoints);
        var map = UnicodeText.SegmentGraphemes(text.AsMemory());
        var actualBoundaries = new List<int> { 0 };
        foreach (var cluster in map.Clusters.Span)
        {
            actualBoundaries.Add(cluster.SourceRange.EndUtf16);
        }

        var expectedBoundaries = GetUtf16Boundaries(testCase.CodePoints, testCase.BreakPositions);
        if (!actualBoundaries.SequenceEqual(expectedBoundaries))
        {
            throw new InvalidOperationException(
                $"GraphemeBreakTest line {testCase.LineNumber}: expected {Format(expectedBoundaries)}, " +
                $"actual {Format(actualBoundaries)}.");
        }

        var expectedClusterCount = expectedBoundaries.Length - 1;
        if (map.Clusters.Length != expectedClusterCount)
        {
            throw new InvalidOperationException(
                $"GraphemeBreakTest line {testCase.LineNumber}: expected {expectedClusterCount} clusters, " +
                $"actual {map.Clusters.Length}.");
        }

        count++;
    }

    return count;
}

static int RunLineBreakCases(string path)
{
    var count = 0;
    foreach (var testCase in ReadCases(path))
    {
        var text = ToText(testCase.CodePoints);
        var map = UnicodeText.GetLineBreaks(text.AsMemory());
        var actualBreaks = new List<int>();
        foreach (var opportunity in map.Opportunities.Span)
        {
            actualBreaks.Add(GetScalarPosition(testCase.CodePoints, opportunity.PositionUtf16));
        }

        var expectedBreaks = testCase.BreakPositions.Where(position => position != 0).ToArray();
        if (!actualBreaks.SequenceEqual(expectedBreaks))
        {
            throw new InvalidOperationException(
                $"LineBreakTest line {testCase.LineNumber}: expected {Format(expectedBreaks)}, " +
                $"actual {Format(actualBreaks)}.");
        }

        if (map.Opportunities.Length == 0 ||
            map.Opportunities.Span[^1].Kind != LineBreakKind.Mandatory ||
            map.Opportunities.Span[^1].PositionUtf16 != text.Length)
        {
            throw new InvalidOperationException(
                $"LineBreakTest line {testCase.LineNumber}: missing mandatory final boundary.");
        }

        count++;
    }

    return count;
}

static IEnumerable<UnicodeTestCase> ReadCases(string path)
{
    if (!File.Exists(path))
    {
        throw new InvalidDataException($"Unicode conformance file was not found: {path}");
    }

    var lineNumber = 0;
    foreach (var sourceLine in File.ReadLines(path))
    {
        lineNumber++;
        var line = sourceLine.Split('#', 2)[0].Trim();
        if (line.Length == 0)
        {
            continue;
        }

        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3 || tokens.Length % 2 == 0)
        {
            throw new InvalidDataException($"Malformed conformance line {lineNumber} in {path}.");
        }

        var codePoints = new int[tokens.Length / 2];
        var breakPositions = new List<int>();
        for (var tokenIndex = 0; tokenIndex < tokens.Length - 1; tokenIndex += 2)
        {
            var marker = tokens[tokenIndex];
            if (marker == "÷")
            {
                breakPositions.Add(tokenIndex / 2);
            }
            else if (marker != "×")
            {
                throw new InvalidDataException(
                    $"Unexpected boundary marker '{marker}' on line {lineNumber} in {path}.");
            }

            var codePoint = int.Parse(tokens[tokenIndex + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if ((uint)codePoint > 0x10FFFF || (uint)(codePoint - 0xD800) < 0x800)
            {
                throw new InvalidDataException(
                    $"Invalid Unicode scalar U+{codePoint:X} on line {lineNumber} in {path}.");
            }

            codePoints[tokenIndex / 2] = codePoint;
        }

        var finalMarker = tokens[^1];
        if (finalMarker == "÷")
        {
            breakPositions.Add(codePoints.Length);
        }
        else if (finalMarker != "×")
        {
            throw new InvalidDataException(
                $"Unexpected final boundary marker '{finalMarker}' on line {lineNumber} in {path}.");
        }

        yield return new UnicodeTestCase(lineNumber, codePoints, breakPositions.ToArray());
    }
}

static string ToText(int[] codePoints)
{
    var builder = new StringBuilder();
    foreach (var codePoint in codePoints)
    {
        builder.Append(char.ConvertFromUtf32(codePoint));
    }

    return builder.ToString();
}

static int[] GetUtf16Boundaries(int[] codePoints, int[] scalarPositions)
{
    var offsets = new int[codePoints.Length + 1];
    for (var index = 0; index < codePoints.Length; index++)
    {
        offsets[index + 1] = offsets[index] + (codePoints[index] > 0xFFFF ? 2 : 1);
    }

    var boundaries = new int[scalarPositions.Length];
    for (var index = 0; index < scalarPositions.Length; index++)
    {
        boundaries[index] = offsets[scalarPositions[index]];
    }

    return boundaries;
}

static int GetScalarPosition(int[] codePoints, int utf16Position)
{
    var utf16Offset = 0;
    for (var index = 0; index < codePoints.Length; index++)
    {
        if (utf16Offset == utf16Position)
        {
            return index;
        }

        utf16Offset += codePoints[index] > 0xFFFF ? 2 : 1;
    }

    if (utf16Offset == utf16Position)
    {
        return codePoints.Length;
    }

    throw new InvalidOperationException($"Invalid UTF-16 boundary returned: {utf16Position}.");
}

static string Format(IEnumerable<int> values)
{
    return string.Join(", ", values);
}

internal readonly record struct UnicodeTestCase(int LineNumber, int[] CodePoints, int[] BreakPositions);
