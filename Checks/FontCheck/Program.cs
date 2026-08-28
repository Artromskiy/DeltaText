using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Delta.Text;
using Delta.Text.Contract;
using FontCheck;

const string expectedFontSha256 = "12aadc8d9b95025d6d135af4ac58ef3fe98c1bdfab306ac762afae912f7b92b2";
const string expectedLuckiestGuySha256 = "bde64ad70289579762149264eeaad6f2bf19b542eca5d62ca9fb0d9cb04a47b2";
const string expectedNotoSansSha256 = "b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5";
const string expectedNotoSansArabicSha256 = "7babaf48a110fb38a89339cec00b96df455424a5ff06065d4eea1e40d7efafe1";
const string text = "Doto";
const float distanceRange = 8;
var options = CheckOptions.Parse(args);

if (!File.Exists(options.FontPath))
{
    throw new FileNotFoundException($"Doto font was not found: {options.FontPath}");
}

var fontBytes = File.ReadAllBytes(options.FontPath);
var actualFontSha256 = Convert.ToHexString(SHA256.HashData(fontBytes)).ToLowerInvariant();
Require(actualFontSha256 == expectedFontSha256,
    $"Unexpected Doto font SHA-256. Expected {expectedFontSha256}, got {actualFontSha256}.");

Directory.CreateDirectory(options.OutputDirectory);
var bidi = RunBidiChecks(options.BidiCorpusPath);
var bidiTest = RunBidiTest(options.BidiTestPath);
var bidiBrackets = RunBidiBrackets(options.BidiBracketsPath);
using var service = new SixLaborsTextService();
var font = service.OpenFont(new FontOpenRequest(
    new FontSourceId(Guid.Parse("8f43c363-400a-4f27-b1d4-72c1b9948a22")),
    fontBytes,
    0));

try
{
    var large = CheckRender(service, font, 128, options.OutputDirectory, "128");
    var small = CheckRender(service, font, 64, options.OutputDirectory, "64");
    var notoLatinPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "NotoSans-Regular.ttf");
    var notoArabicPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "NotoSansArabic-Regular.ttf");
    var notoLatinBytes = ReadVerifiedFont(notoLatinPath, expectedNotoSansSha256, "Noto Sans");
    var notoArabicBytes = ReadVerifiedFont(notoArabicPath, expectedNotoSansArabicSha256, "Noto Sans Arabic");
    var notoLatin = service.OpenFont(new FontOpenRequest(
        new FontSourceId(Guid.Parse("c08a369a-cd85-4be7-9a20-c2aaf17d1e2b")),
        notoLatinBytes,
        0));
    var notoArabic = service.OpenFont(new FontOpenRequest(
        new FontSourceId(Guid.Parse("7f1d3e65-f18d-46d1-9dc7-373e6ef0f2a8")),
        notoArabicBytes,
        0));
    UnicodeRenderSummary unicode;
    try
    {
        unicode = CheckUnicodeRenderCoverage(service, new[] { notoLatin, notoArabic }, "Noto");
    }
    finally
    {
        service.CloseFont(notoArabic);
        service.CloseFont(notoLatin);
    }
    var comparison = CheckImageSharpReference(
        service,
        font,
        fontBytes,
        options.OutputDirectory,
        text,
        64,
        "Doto-coverage-64");
    var largeComparison = CheckImageSharpReference(
        service,
        font,
        fontBytes,
        options.OutputDirectory,
        text,
        128,
        "Doto-coverage-128");
    var luckiestGuy = CheckFontFixture(
        service,
        options.LuckiestGuyPath,
        options.OutputDirectory,
        expectedLuckiestGuySha256);
    Require(large.GlyphCount == small.GlyphCount, "Glyph count changed with pixel size.");
    Require(small.Width < large.Width && small.Height < large.Height,
        "The lower pixel size did not produce a smaller image.");

    var report = new
    {
        font = Path.GetFileName(options.FontPath),
        fontSha256 = actualFontSha256,
        text,
        mode = GlyphImageMode.Sdf.ToString(),
        encoding = GlyphImageEncoding.SdfR8.ToString(),
        distanceRange,
        bidi,
        bidiBrackets,
        large,
        small,
        unicode,
        luckiestGuy,
        comparison,
        largeComparison,
    };
    var reportPath = Path.Combine(options.OutputDirectory, "Doto-sdf-checks.json");
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine("PASS FontCheck (SDF)");
    Console.WriteLine($"  font: {options.FontPath}");
    Console.WriteLine($"  text: {text}");
    Console.WriteLine($"  128px: {large.Width}x{large.Height}, glyphs={large.GlyphCount}, checksum={large.Checksum:x16}");
    Console.WriteLine($"  64px:  {small.Width}x{small.Height}, glyphs={small.GlyphCount}, checksum={small.Checksum:x16}");
    Console.WriteLine($"  Unicode probes: {unicode.CaseCount} cases, {unicode.ImageCount} images, checksum={unicode.Checksum:x16}");
    Console.WriteLine($"  Luckiest Guy: {luckiestGuy.Large.Width}x{luckiestGuy.Large.Height} / {luckiestGuy.Small.Width}x{luckiestGuy.Small.Height}, glyphs={luckiestGuy.Large.GlyphCount}, "
        + $"ImageSharp 48px MAE={luckiestGuy.Comparison.MeanAbsoluteError:0.00}, P95={luckiestGuy.Comparison.P95Error:0.00}; "
        + $"96px MAE={luckiestGuy.LargeComparison.MeanAbsoluteError:0.00}, P95={luckiestGuy.LargeComparison.P95Error:0.00}");
    Console.WriteLine($"  ImageSharp Doto coverage: 64px MAE={comparison.MeanAbsoluteError:0.00}, P95={comparison.P95Error:0.00}; "
        + $"128px MAE={largeComparison.MeanAbsoluteError:0.00}, P95={largeComparison.P95Error:0.00}");
    Console.WriteLine($"  UAX #9 probes: {bidi.CaseCount} cases, Unicode data={bidi.UnicodeDataVersion}");
    Console.WriteLine(bidi.OfficialCorpusRun
        ? $"  UAX #9 corpus: {bidi.CorpusCaseCount} Unicode 17.0.0 cases passed"
        : "  UAX #9 corpus: NOT RUN (pass --bidi-corpus PATH to BidiCharacterTest.txt)");
    Console.WriteLine(bidiTest.OfficialCorpusRun
        ? $"  UAX #9 class corpus: {bidiTest.DataCaseCount} data cases, {bidiTest.VariantCount} paragraph variants passed"
        : "  UAX #9 class corpus: NOT RUN (pass --bidi-test PATH to BidiTest.txt)");
    Console.WriteLine(bidiBrackets.OfficialCorpusRun
        ? $"  BidiBrackets: {bidiBrackets.CaseCount} Unicode 17 mappings passed"
        : "  BidiBrackets: NOT RUN (pass --bidi-brackets PATH to BidiBrackets.txt)");
    Console.WriteLine($"  output: {options.OutputDirectory}");
}

finally
{
    service.CloseFont(font);
}

static BidiSummary RunBidiChecks(string? corpusPath)
{
    var probes = new[]
    {
        new BidiProbe("latin", "abc", TextDirection.LeftToRight, true, false),
        new BidiProbe("hebrew", "אבג", TextDirection.RightToLeft, false, true),
        new BidiProbe("mixed", "abc אבג 123", TextDirection.Auto, true, true),
        new BidiProbe("controls", "A \u202Bאבג\u202C B", TextDirection.Auto, true, true),
        new BidiProbe("isolates", "A\u2067אבג\u2069B", TextDirection.Auto, true, true),
        new BidiProbe("rtl-numbers", "אבג ١٢٣", TextDirection.Auto, true, true),
        new BidiProbe("trailing-neutral", "A.", TextDirection.Auto, true, false),
    };

    for (var i = 0; i < probes.Length; i++)
    {
        var probe = probes[i];
        var runs = BidiResolver.Resolve(probe.Text, probe.Direction);
        Require(runs.Length > 0, $"UAX #9 probe '{probe.Name}' produced no runs.");
        var hasLeftToRight = false;
        var hasRightToLeft = false;
        for (var runIndex = 0; runIndex < runs.Length; runIndex++)
        {
            var run = runs[runIndex];
            Require(run.Start >= 0 && run.Length > 0 && run.Start + run.Length <= probe.Text.Length,
                $"UAX #9 probe '{probe.Name}' produced an out-of-range run.");
            Require(run.Direction == (run.Level % 2 == 0 ? TextDirection.LeftToRight : TextDirection.RightToLeft),
                $"UAX #9 probe '{probe.Name}' has a direction/level mismatch.");
            hasLeftToRight |= run.Direction == TextDirection.LeftToRight;
            hasRightToLeft |= run.Direction == TextDirection.RightToLeft;
        }

        Require(hasLeftToRight == probe.ExpectsLeftToRight,
            $"UAX #9 probe '{probe.Name}' has an unexpected LTR run set.");
        Require(hasRightToLeft == probe.ExpectsRightToLeft,
            $"UAX #9 probe '{probe.Name}' has an unexpected RTL run set.");
    }

    var corpusCaseCount = 0;
    if (corpusPath is not null)
    {
        corpusCaseCount = RunBidiCorpus(corpusPath);
    }

    return new BidiSummary(probes.Length, UnicodeBidiData.UnicodeVersion, corpusCaseCount > 0, corpusCaseCount);
}

static int RunBidiCorpus(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"Unicode bidi corpus was not found: {path}");
    }

    var caseCount = 0;
    var lineNumber = 0;
    var mismatchCount = 0;
    var paragraphMismatchCount = 0;
    var levelMismatchCount = 0;
    var orderMismatchCount = 0;
    var firstMismatches = new List<string>(5);
    foreach (var line in File.ReadLines(path))
    {
        lineNumber++;
        if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
        {
            continue;
        }

        var fields = line.Split(';');
        Require(fields.Length == 5, $"Bidi corpus line {lineNumber} does not have five fields.");
        var codePoints = ParseCodePoints(fields[0], lineNumber);
        var direction = ParseParagraphDirection(fields[1], lineNumber);
        var expectedParagraphLevel = ParseSingleLevel(fields[2], lineNumber);
        var expectedLevels = ParseLevels(fields[3], lineNumber);
        var expectedOrder = ParseOrder(fields[4], lineNumber);
        Require(expectedLevels.Length == codePoints.Length,
            $"Bidi corpus line {lineNumber} has a level count different from its code point count.");

        var text = ToText(codePoints);
        var actual = BidiResolver.ResolveForConformance(text, direction);
        var paragraphMismatch = actual.ParagraphLevel != expectedParagraphLevel;
        var levelMismatch = !actual.Levels.AsSpan().SequenceEqual(expectedLevels);
        var orderMismatch = !actual.VisualOrder.AsSpan().SequenceEqual(expectedOrder);
        if (paragraphMismatch || levelMismatch || orderMismatch)
        {
            mismatchCount++;
            paragraphMismatchCount += paragraphMismatch ? 1 : 0;
            levelMismatchCount += levelMismatch ? 1 : 0;
            orderMismatchCount += orderMismatch ? 1 : 0;
            if (firstMismatches.Count < 5)
            {
                firstMismatches.Add(
                    $"line {lineNumber}, case {caseCount + 1}: "
                    + $"input {fields[0]}, direction {fields[1]}, "
                    + $"paragraph {actual.ParagraphLevel}/{expectedParagraphLevel}, "
                    + $"levels {DescribeDifference(actual.Levels, expectedLevels)}, "
                    + $"order {DescribeDifference(actual.VisualOrder, expectedOrder)}");
            }
        }

        caseCount++;
    }

    Require(caseCount > 0, "Unicode bidi corpus contained no test cases.");
    if (mismatchCount > 0)
    {
        throw new InvalidOperationException(
            $"UAX #9 Unicode 17 corpus failed: {mismatchCount}/{caseCount} cases. "
            + $"Paragraph={paragraphMismatchCount}, levels={levelMismatchCount}, order={orderMismatchCount}.\n"
            + string.Join("\n", firstMismatches));
    }

    return caseCount;
}

static BidiTestSummary RunBidiTest(string? path)
{
    if (path is null)
    {
        return new BidiTestSummary(0, 0, false);
    }

    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"Unicode bidi class corpus was not found: {path}");
    }

    var expectedLevels = Array.Empty<int>();
    var expectedOrder = Array.Empty<int>();
    var hasLevels = false;
    var hasOrder = false;
    var dataCaseCount = 0;
    var variantCount = 0;
    var lineNumber = 0;
    foreach (var sourceLine in File.ReadLines(path))
    {
        lineNumber++;
        var line = sourceLine.Trim();
        if (line.Length == 0 || line[0] == '#')
        {
            continue;
        }

        if (line.StartsWith("@Levels:", StringComparison.Ordinal))
        {
            expectedLevels = ParseBidiTestLevels(line[8..], lineNumber);
            hasLevels = true;
            continue;
        }

        if (line.StartsWith("@Reorder:", StringComparison.Ordinal))
        {
            expectedOrder = ParseOrder(line[9..], lineNumber);
            hasOrder = true;
            continue;
        }

        if (line[0] == '@')
        {
            continue;
        }

        var separator = line.IndexOf(';');
        Require(separator > 0, $"Unicode bidi class corpus line {lineNumber} has no data separator.");
        Require(hasLevels && hasOrder, $"Unicode bidi class corpus line {lineNumber} has no expected result.");
        var properties = SplitTokens(line[..separator]);
        var bitsetText = line[(separator + 1)..].Trim();
        Require(int.TryParse(bitsetText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bitset),
            $"Invalid paragraph bitset at Unicode bidi class corpus line {lineNumber}.");
        var text = BidiClassPropertiesToText(properties, lineNumber);

        if ((bitset & 1) != 0)
        {
            CheckBidiTestCase(text, TextDirection.Auto, expectedLevels, expectedOrder, lineNumber);
            variantCount++;
        }

        if ((bitset & 2) != 0)
        {
            CheckBidiTestCase(text, TextDirection.LeftToRight, expectedLevels, expectedOrder, lineNumber);
            variantCount++;
        }

        if ((bitset & 4) != 0)
        {
            CheckBidiTestCase(text, TextDirection.RightToLeft, expectedLevels, expectedOrder, lineNumber);
            variantCount++;
        }

        dataCaseCount++;
    }

    Require(dataCaseCount > 0, "Unicode bidi class corpus contained no test cases.");
    return new BidiTestSummary(dataCaseCount, variantCount, true);
}

static BidiBracketSummary RunBidiBrackets(string? path)
{
    if (path is null)
    {
        return new BidiBracketSummary(0, false);
    }

    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"Unicode bidi bracket corpus was not found: {path}");
    }

    var caseCount = 0;
    var lineNumber = 0;
    var mismatches = new List<string>(64);
    foreach (var sourceLine in File.ReadLines(path))
    {
        lineNumber++;
        var line = sourceLine;
        var comment = line.IndexOf('#');
        if (comment >= 0)
        {
            line = line[..comment];
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var fields = line.Split(';');
        Require(fields.Length == 3, $"Bidi bracket corpus line {lineNumber} does not have three fields.");
        Require(int.TryParse(fields[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint)
            && Rune.IsValid(codePoint),
            $"Invalid bracket code point at line {lineNumber}.");
        var pairText = fields[1].Trim();
        var type = fields[2].Trim();
        Require(type is "o" or "c" or "n", $"Invalid bracket type at line {lineNumber}.");

        var hasPair = int.TryParse(pairText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pair)
            && Rune.IsValid(pair);
        Require((type is "o" or "c") == hasPair,
            $"Bracket pair presence disagrees with type at line {lineNumber}.");

        var actual = UnicodeBidiData.TryGetPairedBracketInfo(codePoint, out var actualPair, out var isOpening);
        if (actual != hasPair)
        {
            mismatches.Add($"line {lineNumber}: U+{codePoint:X} expected {pairText}/{type}, actual "
                + (actual ? $"U+{actualPair:X}/{(isOpening ? 'o' : 'c')}" : "none"));
        }
        else if (hasPair)
        {
            if (actualPair != pair)
            {
                mismatches.Add($"line {lineNumber}: U+{codePoint:X} pair U+{actualPair:X}/U+{pair:X}");
            }

            if (isOpening != (type == "o"))
            {
                mismatches.Add($"line {lineNumber}: U+{codePoint:X} type mismatch");
            }
        }

        caseCount++;
    }

    Require(caseCount > 0, "Unicode bidi bracket corpus contained no test cases.");
    Require(mismatches.Count == 0,
        $"Unicode bidi bracket corpus failed with at least {mismatches.Count} mismatches.\n"
        + string.Join("\n", mismatches));
    return new BidiBracketSummary(caseCount, true);
}

static int[] ParseBidiTestLevels(string field, int lineNumber)
{
    var tokens = SplitTokens(field);
    var levels = new int[tokens.Length];
    for (var i = 0; i < tokens.Length; i++)
    {
        if (tokens[i] == "x")
        {
            levels[i] = -1;
            continue;
        }

        if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out levels[i]))
        {
            throw new InvalidDataException($"Invalid resolved level at Unicode bidi class corpus line {lineNumber}.");
        }
    }

    return levels;
}

static string BidiClassPropertiesToText(string[] properties, int lineNumber)
{
    var builder = new StringBuilder(properties.Length);
    for (var i = 0; i < properties.Length; i++)
    {
        builder.Append(properties[i] switch
        {
            "L" => 'A',
            "R" => '\u05D0',
            "AL" => '\u0627',
            "EN" => '0',
            "AN" => '\u0660',
            "ES" => '+',
            "ET" => '$',
            "CS" => ',',
            "NSM" => '\u0300',
            "B" => '\u2029',
            "S" => '\t',
            "WS" => ' ',
            "ON" => '~',
            "LRE" => '\u202A',
            "RLE" => '\u202B',
            "LRO" => '\u202D',
            "RLO" => '\u202E',
            "PDF" => '\u202C',
            "BN" => '\u00AD',
            "LRI" => '\u2066',
            "RLI" => '\u2067',
            "FSI" => '\u2068',
            "PDI" => '\u2069',
            _ => throw new InvalidDataException(
                $"Unknown bidi property '{properties[i]}' at Unicode bidi class corpus line {lineNumber}."),
        });
    }

    return builder.ToString();
}

static void CheckBidiTestCase(
    string text,
    TextDirection direction,
    int[] expectedLevels,
    int[] expectedOrder,
    int lineNumber)
{
    var actual = BidiResolver.ResolveForConformance(text, direction);
    Require(actual.Levels.AsSpan().SequenceEqual(expectedLevels),
        $"Unicode bidi class corpus levels mismatch at line {lineNumber} ({direction}): "
        + $"actual=[{string.Join(',', actual.Levels)}], expected=[{string.Join(',', expectedLevels)}].");
    Require(actual.VisualOrder.AsSpan().SequenceEqual(expectedOrder),
        $"Unicode bidi class corpus visual order mismatch at line {lineNumber} ({direction}): "
        + $"actual=[{string.Join(',', actual.VisualOrder)}], expected=[{string.Join(',', expectedOrder)}].");
}

static string DescribeDifference(ReadOnlySpan<int> actual, ReadOnlySpan<int> expected)
{
    if (actual.Length != expected.Length)
    {
        return $"length {actual.Length}/{expected.Length}";
    }

    for (var i = 0; i < actual.Length; i++)
    {
        if (actual[i] != expected[i])
        {
            return $"first[{i}] {actual[i]}/{expected[i]}";
        }
    }

    return "match";
}

static int[] ParseCodePoints(string field, int lineNumber)
{
    var tokens = SplitTokens(field);
    var values = new int[tokens.Length];
    for (var i = 0; i < tokens.Length; i++)
    {
        if (!int.TryParse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out values[i])
            || !Rune.IsValid(values[i]))
        {
            throw new InvalidDataException($"Invalid code point '{tokens[i]}' at corpus line {lineNumber}.");
        }
    }

    return values;
}

static TextDirection ParseParagraphDirection(string field, int lineNumber)
{
    return field.Trim() switch
    {
        "0" => TextDirection.LeftToRight,
        "1" => TextDirection.RightToLeft,
        "2" => TextDirection.Auto,
        _ => throw new InvalidDataException($"Invalid paragraph direction at corpus line {lineNumber}."),
    };
}

static int ParseSingleLevel(string field, int lineNumber)
{
    if (!int.TryParse(field.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
    {
        throw new InvalidDataException($"Invalid paragraph level at corpus line {lineNumber}.");
    }

    return level;
}

static int[] ParseLevels(string field, int lineNumber)
{
    var tokens = SplitTokens(field);
    var levels = new int[tokens.Length];
    for (var i = 0; i < tokens.Length; i++)
    {
        if (tokens[i] == "x")
        {
            levels[i] = -1;
            continue;
        }

        if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out levels[i]))
        {
            throw new InvalidDataException($"Invalid resolved level at corpus line {lineNumber}.");
        }
    }

    return levels;
}

static int[] ParseOrder(string field, int lineNumber)
{
    var tokens = SplitTokens(field);
    var order = new int[tokens.Length];
    for (var i = 0; i < tokens.Length; i++)
    {
        if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out order[i]))
        {
            throw new InvalidDataException($"Invalid visual order at corpus line {lineNumber}.");
        }
    }

    return order;
}

static string ToText(ReadOnlySpan<int> codePoints)
{
    var builder = new StringBuilder(codePoints.Length);
    for (var i = 0; i < codePoints.Length; i++)
    {
        builder.Append(char.ConvertFromUtf32(codePoints[i]));
    }

    return builder.ToString();
}

static string[] SplitTokens(string field)
    => field.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

static RenderSummary CheckRender(
    SixLaborsTextService service,
    FontInstanceId font,
    float pixelsPerEm,
    string outputDirectory,
    string sizeLabel,
    string renderText = text,
    string outputPrefix = "Doto-sdf")
{
    var request = new TextShapeRequest(renderText.AsMemory(), pixelsPerEm, new[] { font });
    var shaped = service.Shape(request);
    Require(shaped.TextLengthUtf16 == renderText.Length, "Shaped text length does not match the source text.");
    Require(shaped.Runs.Length > 0, "Shaping produced no runs.");

    var glyphCount = 0;
    var glyphChecksum = 14695981039346656037UL;
    for (var runIndex = 0; runIndex < shaped.Runs.Length; runIndex++)
    {
        var run = shaped.Runs.Span[runIndex];
        var glyphs = run.Glyphs.Span;
        Require(glyphs.Length > 0, "Shaping produced an empty run.");
        for (var glyphIndex = 0; glyphIndex < glyphs.Length; glyphIndex++)
        {
            var glyph = glyphs[glyphIndex];
            var image = service.GenerateGlyphImage(new GlyphImageRequest(
                font,
                glyph.GlyphId,
                pixelsPerEm,
                GlyphImageMode.Sdf,
                distanceRange));
            ValidateSdfImage(image, font, glyph.GlyphId);
            glyphChecksum = AppendFnv1a64(glyphChecksum, image.Pixels.Span);
            glyphCount++;
        }
    }

    var renderer = new CpuTextRenderer(service);
    var first = renderer.Render(request, new CpuTextRenderOptions(
        GlyphImageMode.Sdf,
        distanceRange,
        new Rgba32(234, 242, 255, 255)));
    var second = renderer.Render(request, new CpuTextRenderOptions(
        GlyphImageMode.Sdf,
        distanceRange,
        new Rgba32(234, 242, 255, 255)));
    Require(!first.IsEmpty, "CPU SDF composition produced an empty image.");
    Require(first.Width == second.Width && first.Height == second.Height,
        "Repeated SDF composition changed image dimensions.");
    Require(first.Pixels.Span.SequenceEqual(second.Pixels.Span),
        "Repeated SDF composition changed output pixels.");

    var preview = CompositeOnBackground(first.Pixels.Span, new Rgba32(11, 16, 28, 255));
    var outputPath = Path.Combine(outputDirectory, $"{outputPrefix}-{sizeLabel}.png");
    PngWriter.Write(outputPath, first.Width, first.Height, preview);

    return new RenderSummary(
        first.Width,
        first.Height,
        glyphCount,
        glyphChecksum,
        Fnv1a64(first.Pixels.Span),
        outputPath);
}

static FontFixtureSummary CheckFontFixture(
    SixLaborsTextService service,
    string path,
    string outputDirectory,
    string expectedSha256)
{
    Require(File.Exists(path), $"Luckiest Guy font was not found: {path}");
    var bytes = File.ReadAllBytes(path);
    var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    Require(actualSha256 == expectedSha256,
        $"Unexpected Luckiest Guy font SHA-256. Expected {expectedSha256}, got {actualSha256}.");

    var font = service.OpenFont(new FontOpenRequest(
        new FontSourceId(Guid.Parse("f60b2cd2-46a4-4da7-83ed-0ebd9a1bd68e")),
        bytes,
        0));
    try
    {
        var small = CheckRender(service, font, 48, outputDirectory, "48", "Luckiest Guy", "LuckiestGuy-sdf");
        var large = CheckRender(service, font, 96, outputDirectory, "96", "Luckiest Guy", "LuckiestGuy-sdf");
        var comparison = CheckImageSharpReference(
            service,
            font,
            bytes,
            outputDirectory,
            "Luckiest Guy",
            48,
            "LuckiestGuy-coverage");
        var largeComparison = CheckImageSharpReference(
            service,
            font,
            bytes,
            outputDirectory,
            "Luckiest Guy",
            96,
            "LuckiestGuy-coverage-96");
        Require(small.GlyphCount == large.GlyphCount, "Luckiest Guy glyph count changed with pixel size.");
        Require(small.Width < large.Width && small.Height < large.Height,
            "Luckiest Guy lower pixel size did not produce a smaller image.");
        return new FontFixtureSummary("LuckiestGuy", actualSha256, small, large, comparison, largeComparison);
    }
    finally
    {
        service.CloseFont(font);
    }
}

static UnicodeRenderSummary CheckUnicodeRenderCoverage(
    SixLaborsTextService service,
    FontInstanceId[] fallbackFonts,
    string fontName)
{
    var probes = new[]
    {
        new UnicodeRenderProbe("latin", "Hello, Delta! 123", RequiresGlyphCoverage: true),
        new UnicodeRenderProbe("cyrillic", "Привет, мир", RequiresGlyphCoverage: true),
        new UnicodeRenderProbe("arabic", "مرحبا بالعالم", RequiresGlyphCoverage: true),
        new UnicodeRenderProbe("hebrew", "שלום עולם"),
        new UnicodeRenderProbe("devanagari", "नमस्ते दुनिया"),
        new UnicodeRenderProbe("thai", "สวัสดีโลก"),
        new UnicodeRenderProbe("cjk", "日本語 中文 한국어"),
        new UnicodeRenderProbe("combining", "Cafe\u0301 A\u0323"),
        new UnicodeRenderProbe("emoji", "👩🏽‍💻 ❤️ 🏳️‍🌈"),
        new UnicodeRenderProbe("mixed", "LTR אבג / العربية / 123"),
        new UnicodeRenderProbe("controls", "A\u2067אבג\u2069B"),
        new UnicodeRenderProbe("noncharacters", "\u0378 \uFFFE \U0001FFFE", true),
    };
    var renderer = new CpuTextRenderer(service);
    var checksum = 14695981039346656037UL;
    var imageCount = 0;
    for (var probeIndex = 0; probeIndex < probes.Length; probeIndex++)
    {
        var probe = probes[probeIndex];
        ValidateUnicodeBoundaries(probe);
        var request = new TextShapeRequest(probe.Text.AsMemory(), 32, fallbackFonts);
        if (probe.ExpectsShapeRejection)
        {
            var rejected = false;
            try
            {
                _ = service.Shape(request);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Require(rejected, $"Unicode probe '{probe.Name}' was expected to be rejected.");
            continue;
        }

        var shaped = service.Shape(request);
        ValidateUnicodeShape(probe, shaped);

        var modes = new[]
        {
            GlyphImageMode.Coverage,
            GlyphImageMode.Sdf,
            GlyphImageMode.Msdf,
            GlyphImageMode.Color,
        };
        for (var modeIndex = 0; modeIndex < modes.Length; modeIndex++)
        {
            var mode = modes[modeIndex];
            var image = renderer.Render(request, new CpuTextRenderOptions(
                mode,
                mode is GlyphImageMode.Sdf or GlyphImageMode.Msdf ? distanceRange : 0,
                new Rgba32(231, 237, 255, 255)));
            ValidateCpuImage(probe, fontName, mode, image);
            checksum = AppendFnv1a64(checksum, image.Pixels.Span);
            imageCount++;
        }
    }

    return new UnicodeRenderSummary(probes.Length, imageCount, checksum);
}

static void ValidateUnicodeBoundaries(UnicodeRenderProbe probe)
{
    var clusters = UnicodeText.SegmentGraphemes(probe.Text.AsMemory());
    var previousEnd = 0;
    for (var index = 0; index < clusters.Clusters.Length; index++)
    {
        var cluster = clusters.Clusters.Span[index];
        Require(cluster.SourceRange.StartUtf16 == previousEnd,
            $"Unicode probe '{probe.Name}' has a grapheme gap or overlap.");
        Require(cluster.SourceRange.EndUtf16 > cluster.SourceRange.StartUtf16
            && cluster.SourceRange.EndUtf16 <= probe.Text.Length
            && cluster.CodePointCount > 0,
            $"Unicode probe '{probe.Name}' has an invalid grapheme range.");
        previousEnd = cluster.SourceRange.EndUtf16;
    }

    Require(previousEnd == probe.Text.Length,
        $"Unicode probe '{probe.Name}' grapheme clusters do not cover the source.");

    var breaks = UnicodeText.GetLineBreaks(probe.Text.AsMemory());
    var previousPosition = -1;
    for (var index = 0; index < breaks.Opportunities.Length; index++)
    {
        var opportunity = breaks.Opportunities.Span[index];
        Require(opportunity.PositionUtf16 >= previousPosition
            && opportunity.PositionUtf16 <= probe.Text.Length
            && opportunity.Kind is LineBreakKind.Optional or LineBreakKind.Mandatory,
            $"Unicode probe '{probe.Name}' has an invalid line-break boundary.");
        previousPosition = opportunity.PositionUtf16;
    }

    Require(breaks.Opportunities.Length > 0
        && breaks.Opportunities.Span[^1].PositionUtf16 == probe.Text.Length
        && breaks.Opportunities.Span[^1].Kind == LineBreakKind.Mandatory,
        $"Unicode probe '{probe.Name}' has no mandatory final line boundary.");
}

static void ValidateUnicodeShape(UnicodeRenderProbe probe, ShapedText shaped)
{
    Require(shaped.TextLengthUtf16 == probe.Text.Length && shaped.Runs.Length > 0,
        $"Unicode probe '{probe.Name}' produced no shaped runs.");
    var coveredGlyphCount = 0;
    for (var runIndex = 0; runIndex < shaped.Runs.Length; runIndex++)
    {
        var run = shaped.Runs.Span[runIndex];
        Require(run.SourceRange.StartUtf16 >= 0 && run.SourceRange.EndUtf16 <= probe.Text.Length
            && run.Glyphs.Length > 0,
            $"Unicode probe '{probe.Name}' produced an invalid shaped run.");
        Require(float.IsFinite(run.AdvanceX) && float.IsFinite(run.AdvanceY)
            && float.IsFinite(run.Bounds.Left) && float.IsFinite(run.Bounds.Top)
            && float.IsFinite(run.Bounds.Right) && float.IsFinite(run.Bounds.Bottom),
            $"Unicode probe '{probe.Name}' produced non-finite run metrics.");
        for (var glyphIndex = 0; glyphIndex < run.Glyphs.Length; glyphIndex++)
        {
            var glyph = run.Glyphs.Span[glyphIndex];
            coveredGlyphCount += glyph.GlyphId == 0 ? 0 : 1;
            Require(glyph.ClusterUtf16 >= run.SourceRange.StartUtf16
                && glyph.ClusterUtf16 < run.SourceRange.EndUtf16
                && glyph.ClusterUtf16 < probe.Text.Length
                && !char.IsLowSurrogate(probe.Text[glyph.ClusterUtf16]),
                $"Unicode probe '{probe.Name}' produced an invalid glyph cluster.");
            Require(float.IsFinite(glyph.AdvanceX) && float.IsFinite(glyph.AdvanceY)
                && float.IsFinite(glyph.OffsetX) && float.IsFinite(glyph.OffsetY),
                $"Unicode probe '{probe.Name}' produced non-finite glyph metrics.");
        }
    }

    if (probe.RequiresGlyphCoverage)
    {
        Require(coveredGlyphCount > 0,
            $"Unicode probe '{probe.Name}' produced only missing glyphs.");
    }
}

static void ValidateCpuImage(
    UnicodeRenderProbe probe,
    string fontName,
    GlyphImageMode mode,
    CpuTextImage image)
{
    Require(float.IsFinite(image.Bounds.Left) && float.IsFinite(image.Bounds.Top)
        && float.IsFinite(image.Bounds.Right) && float.IsFinite(image.Bounds.Bottom),
        $"{fontName} Unicode probe '{probe.Name}' returned non-finite CPU bounds for {mode}.");
    if (image.IsEmpty)
    {
        Require(image.Width == 0 && image.Height == 0 && image.Pixels.IsEmpty,
            $"{fontName} Unicode probe '{probe.Name}' returned malformed empty {mode} image.");
        return;
    }

    Require(image.Width > 0 && image.Height > 0
        && image.Pixels.Length == checked(image.Width * image.Height * 4),
        $"{fontName} Unicode probe '{probe.Name}' returned malformed {mode} image.");
}

static RenderComparison CheckImageSharpReference(
    SixLaborsTextService service,
    FontInstanceId font,
    ReadOnlySpan<byte> fontBytes,
    string outputDirectory,
    string renderText = text,
    float pixelsPerEm = 48,
    string outputName = "Doto-coverage")
{
    var request = new TextShapeRequest(renderText.AsMemory(), pixelsPerEm, new[] { font });
    var renderer = new CpuTextRenderer(service);
    var ours = renderer.Render(request, new CpuTextRenderOptions(
        GlyphImageMode.Coverage,
        0,
        new Rgba32(255, 255, 255, 255)));
    var reference = ReferenceFontRenderer.Render(fontBytes, renderText, pixelsPerEm);
    return ImageSharpComparison.Compare(ours, reference, outputDirectory, outputName);
}

static byte[] ReadVerifiedFont(string path, string expectedSha256, string name)
{
    Require(File.Exists(path), $"{name} font was not found: {path}");
    var bytes = File.ReadAllBytes(path);
    var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    Require(actualSha256 == expectedSha256,
        $"Unexpected {name} font SHA-256. Expected {expectedSha256}, got {actualSha256}.");
    return bytes;
}

static void ValidateSdfImage(GlyphImage image, FontInstanceId font, uint glyphId)
{
    Require(image.Font == font, $"Glyph {glyphId} returned an image for another font.");
    Require(image.GlyphId == glyphId, $"Glyph image returned the wrong glyph ID for {glyphId}.");
    Require(image.Encoding == GlyphImageEncoding.SdfR8, $"Glyph {glyphId} is not SdfR8.");
    Require(image.DistanceRange == distanceRange, $"Glyph {glyphId} returned the wrong distance range.");
    if (image.IsEmpty)
    {
        Require(image.Width == 0 && image.Height == 0 && image.Pixels.IsEmpty,
            $"Glyph {glyphId} returned malformed empty SDF data.");
        return;
    }

    Require(image.Width > 0 && image.Height > 0, $"Glyph {glyphId} returned an empty SDF image.");
    Require(image.Pixels.Length == checked(image.Width * image.Height),
        $"Glyph {glyphId} returned an invalid SDF payload length.");

    var minimum = byte.MaxValue;
    var maximum = byte.MinValue;
    foreach (var value in image.Pixels.Span)
    {
        minimum = Math.Min(minimum, value);
        maximum = Math.Max(maximum, value);
    }

    Require(minimum < 128 && maximum > 128, $"Glyph {glyphId} has no signed-distance range.");
}

static byte[] CompositeOnBackground(ReadOnlySpan<byte> source, Rgba32 background)
{
    var output = source.ToArray();
    for (var i = 0; i < output.Length; i += 4)
    {
        var alpha = output[i + 3];
        var inverse = 255 - alpha;
        output[i] = (byte)Math.Min(255, output[i] + background.Red * inverse / 255);
        output[i + 1] = (byte)Math.Min(255, output[i + 1] + background.Green * inverse / 255);
        output[i + 2] = (byte)Math.Min(255, output[i + 2] + background.Blue * inverse / 255);
        output[i + 3] = 255;
    }

    return output;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
{
    return AppendFnv1a64(14695981039346656037UL, bytes);
}

static ulong AppendFnv1a64(ulong initial, ReadOnlySpan<byte> bytes)
{
    var hash = initial;
    foreach (var value in bytes)
    {
        hash ^= value;
        hash *= 1099511628211UL;
    }

    return hash;
}

internal readonly record struct RenderSummary(
    int Width,
    int Height,
    int GlyphCount,
    ulong GlyphChecksum,
    ulong Checksum,
    string OutputPath);

internal readonly record struct FontFixtureSummary(
    string Name,
    string FontSha256,
    RenderSummary Small,
    RenderSummary Large,
    RenderComparison Comparison,
    RenderComparison LargeComparison);

internal readonly record struct UnicodeRenderSummary(
    int CaseCount,
    int ImageCount,
    ulong Checksum);

internal readonly record struct UnicodeRenderProbe(
    string Name,
    string Text,
    bool ExpectsShapeRejection = false,
    bool RequiresGlyphCoverage = false);

internal readonly record struct BidiProbe(
    string Name,
    string Text,
    TextDirection Direction,
    bool ExpectsLeftToRight,
    bool ExpectsRightToLeft);

internal readonly record struct BidiSummary(
    int CaseCount,
    string UnicodeDataVersion,
    bool OfficialCorpusRun,
    int CorpusCaseCount);

internal readonly record struct BidiTestSummary(
    int DataCaseCount,
    int VariantCount,
    bool OfficialCorpusRun);

internal readonly record struct BidiBracketSummary(int CaseCount, bool OfficialCorpusRun);

internal sealed class CheckOptions
{
    private CheckOptions(
        string fontPath,
        string outputDirectory,
        string? bidiCorpusPath,
        string? bidiTestPath,
        string? bidiBracketsPath,
        string luckiestGuyPath)
    {
        FontPath = fontPath;
        OutputDirectory = outputDirectory;
        BidiCorpusPath = bidiCorpusPath;
        BidiTestPath = bidiTestPath;
        BidiBracketsPath = bidiBracketsPath;
        LuckiestGuyPath = luckiestGuyPath;
    }

    internal string FontPath { get; }

    internal string OutputDirectory { get; }

    internal string? BidiCorpusPath { get; }

    internal string? BidiTestPath { get; }

    internal string? BidiBracketsPath { get; }

    internal string LuckiestGuyPath { get; }

    internal static CheckOptions Parse(string[] args)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "Doto.ttf");
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        string? bidiCorpusPath = null;
        string? bidiTestPath = null;
        string? bidiBracketsPath = null;
        var luckiestGuyPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "LuckiestGuy-Regular.ttf");
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--font":
                    fontPath = ReadValue(args, ref i, "--font");
                    break;
                case "--luckiest-guy":
                    luckiestGuyPath = ReadValue(args, ref i, "--luckiest-guy");
                    break;
                case "--output":
                    outputDirectory = ReadValue(args, ref i, "--output");
                    break;
                case "--bidi-corpus":
                    bidiCorpusPath = ReadValue(args, ref i, "--bidi-corpus");
                    break;
                case "--bidi-test":
                    bidiTestPath = ReadValue(args, ref i, "--bidi-test");
                    break;
                case "--bidi-brackets":
                    bidiBracketsPath = ReadValue(args, ref i, "--bidi-brackets");
                    break;
                case "--help":
                    Console.WriteLine("Usage: dotnet run --project Checks/FontCheck/FontCheck.csproj -c Release -- [--font PATH] [--luckiest-guy PATH] [--output DIRECTORY] [--bidi-corpus PATH] [--bidi-test PATH] [--bidi-brackets PATH]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new CheckOptions(
            Path.GetFullPath(fontPath),
            Path.GetFullPath(outputDirectory),
            bidiCorpusPath is null ? null : Path.GetFullPath(bidiCorpusPath),
            bidiTestPath is null ? null : Path.GetFullPath(bidiTestPath),
            bidiBracketsPath is null ? null : Path.GetFullPath(bidiBracketsPath),
            Path.GetFullPath(luckiestGuyPath));
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }
}

internal static class PngWriter
{
    internal static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var output = File.Create(path);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream(checked(height * (width * 4 + 1)));
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba.Slice(y * width * 4, width * 4));
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var checksum = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(checksum, 0);
        data.CopyTo(checksum.AsSpan(typeBytes.Length));
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(checksum));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}
