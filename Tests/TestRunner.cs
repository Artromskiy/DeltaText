using System.Text.Json;

using Delta.Text.Contract;

namespace Delta.Text.Tests;

internal static class TestRunner
{
    private static readonly (string Name, Action Body)[] Tests =
    [
        ("open font owns source bytes and metrics", OpenFont),
        ("Latin shaping preserves clusters and ligatures", LatinShaping),
        ("deterministic Latin UI smoke fixture", LatinUiSmokeFixture),
        ("Cyrillic and combining marks shape", CyrillicAndCombining),
        ("Arabic shaping resolves RTL", ArabicShaping),
        ("mixed bidi text keeps visual directional runs", MixedBidirectionalText),
        ("bidi controls preserve source mapping", BidiControls),
        ("bidi boundaries preserve source offsets", BidiBoundaries),
        ("RTL paragraphs keep European numbers in an LTR run", BidiNumbers),
        ("Unicode bidi classes and explicit overflow stay bounded", BidiUnicodeClasses),
        ("paired brackets resolve without losing source ranges", BidiPairedBrackets),
        ("Unicode 17 bidi conformance regressions stay stable", BidiConformanceRegressions),
        ("fallback produces font-specific runs", FontFallback),
        ("coverage, SDF and color images are unpacked", GlyphImages),
        ("CPU renderer composes owned RGBA text images", CpuTextRendering),
        ("managed MSDF is deterministic and channel-separated", ManagedMsdfGeneration),
        ("MSDF image is optional and renderer-neutral", MsdfImage),
        ("invalid requests are rejected", InvalidRequests),
        ("font lifetime rejects closed instances", FontLifetime),
        ("repeated dispose and unknown ids are safe", RepeatedDisposeAndUnknownIds),
        ("concurrent service access is serialized", ConcurrentServiceAccess),
        ("empty text and surrogate boundaries are guarded", EmptyAndSurrogateBoundaries),
        ("isolates and zero-glyph output stay bounded", IsolatesAndZeroGlyphOutput)
    ];

    public static void Run(string[] args)
    {
        var passed = 0;
        foreach (var test in Tests)
        {
            test.Body();
            Console.WriteLine($"PASS {test.Name}");
            passed++;
        }

        Console.WriteLine($"{passed}/{Tests.Length} tests passed.");
    }

    private static void OpenFont()
    {
        var source = File.ReadAllBytes(LatinPath());
        using var service = new SixLaborsTextService();
        var request = new FontOpenRequest(new FontSourceId(Guid.Parse("f6b70d83-6ab2-4b7b-9f28-7f6a5ecf69c1")), source, 0);
        var font = service.OpenFont(request);
        source[0] ^= 0xff;
        var metrics = service.GetFontMetrics(font, 24);
        Check(metrics.UnitsPerEm > 0 && metrics.Ascent > 0 && metrics.Descent >= 0, "font metrics are invalid");
        service.CloseFont(font);
    }

    private static void LatinShaping()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "f6b70d83-6ab2-4b7b-9f28-7f6a5ecf69c1");
        var text = "office AV";
        var shaped = service.Shape(new TextShapeRequest(
            text.AsMemory(),
            32,
            new[] { font },
            TextDirection.LeftToRight,
            default,
            Features: new[] { new OpenTypeFeature(Tag("liga"), 1), new OpenTypeFeature(Tag("kern"), 1) }));
        Check(shaped.Runs.Length == 1, "Latin text was split unexpectedly");
        Check(shaped.Runs.Span[0].Glyphs.Length < text.Length, "ligature shaping did not compact the glyph sequence");
        Check(shaped.Runs.Span[0].Glyphs.Span[0].ClusterUtf16 == 0, "first cluster is not anchored at zero");
        Check(shaped.Runs.Span[0].Glyphs.Span.ToArray().Any(static glyph =>
                (glyph.Safety & GlyphSafety.UnsafeToBreak) != 0),
            "ligature safety was not reported");
    }

    private static void LatinUiSmokeFixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(FixturePath("LatinUiSmokeFixture.json")));
        var root = fixture.RootElement;
        var text = root.GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Latin UI fixture has no text.");
        var pixelsPerEm = root.GetProperty("pixelsPerEm").GetSingle();
        var directionName = root.GetProperty("direction").GetString()
            ?? throw new InvalidOperationException("Latin UI fixture has no direction.");
        if (!Enum.TryParse<TextDirection>(directionName, out var direction) || !Enum.IsDefined(direction))
        {
            throw new InvalidOperationException("Latin UI fixture has an invalid direction.");
        }

        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "b67b3c06-4c70-4cc7-bf52-47e8b0adf16b");
        var shaped = service.Shape(new TextShapeRequest(
            text.AsMemory(),
            pixelsPerEm,
            new[] { font },
            direction,
            Features: new[] { new OpenTypeFeature(Tag("liga"), 1), new OpenTypeFeature(Tag("kern"), 1) }));
        Check(shaped.TextLengthUtf16 == root.GetProperty("sourceLengthUtf16").GetInt32(), "Latin fixture source length changed");
        Check(shaped.Runs.Length == 1, "Latin fixture did not produce one run");

        var run = shaped.Runs.Span[0];
        Check(run.Font == font, "Latin fixture font identity changed");
        Check(run.Direction == direction, "Latin fixture direction changed");
        Check(run.SourceRange.StartUtf16 == 0 && run.SourceRange.LengthUtf16 == text.Length, "Latin fixture source range changed");
        Check(run.BidiLevel == root.GetProperty("bidiLevel").GetByte(), "Latin fixture bidi level changed");
        var expectedGlyphs = root.GetProperty("glyphs");
        Check(run.Glyphs.Length == expectedGlyphs.GetArrayLength(), "Latin fixture glyph count changed");
        for (var i = 0; i < run.Glyphs.Length; i++)
        {
            var actual = run.Glyphs.Span[i];
            var expected = expectedGlyphs[i];
            Check(actual.GlyphId == expected.GetProperty("glyphId").GetUInt32(), $"Latin fixture glyph {i} changed");
            Check(actual.ClusterUtf16 == expected.GetProperty("clusterUtf16").GetInt32(), $"Latin fixture cluster {i} changed");
            Check(MathF.Abs(actual.AdvanceX - expected.GetProperty("advanceX").GetSingle()) < 0.00001f, $"Latin fixture advance {i} changed");
            Check(MathF.Abs(actual.AdvanceY - expected.GetProperty("advanceY").GetSingle()) < 0.00001f, $"Latin fixture vertical advance {i} changed");
            Check(MathF.Abs(actual.OffsetX - expected.GetProperty("offsetX").GetSingle()) < 0.00001f, $"Latin fixture offset X {i} changed");
            Check(MathF.Abs(actual.OffsetY - expected.GetProperty("offsetY").GetSingle()) < 0.00001f, $"Latin fixture offset Y {i} changed");
        }

        var imageSpec = root.GetProperty("image");
        var image = service.GenerateGlyphImage(new GlyphImageRequest(
            font,
            imageSpec.GetProperty("glyphId").GetUInt32(),
            pixelsPerEm,
            GlyphImageMode.Coverage));
        Check(image.Encoding == GlyphImageEncoding.CoverageR8, "Latin fixture image encoding changed");
        Check(image.Width == imageSpec.GetProperty("width").GetInt32(), "Latin fixture image width changed");
        Check(image.Height == imageSpec.GetProperty("height").GetInt32(), "Latin fixture image height changed");
        Check(image.Pixels.Length == image.Width * image.Height, "Latin fixture image payload is not tightly packed");
        var expectedChecksum = imageSpec.GetProperty("fnv1a64").GetUInt64();
        Check(Fnv1a64(image.Pixels.Span) == expectedChecksum, "Latin fixture image pixels changed");
        var retainedPixels = image.Pixels;
        service.CloseFont(font);
        Check(Fnv1a64(retainedPixels.Span) == expectedChecksum, "Latin fixture pixels did not outlive the font instance");
    }

    private static void CyrillicAndCombining()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "a11e0e56-39c4-4486-9afc-0e5a8f15f87b");
        var cyrillic = service.Shape(new TextShapeRequest("Привет мир".AsMemory(), 24, new[] { font }, TextDirection.LeftToRight));
        Check(cyrillic.Runs.Length == 1 && cyrillic.Runs.Span[0].Glyphs.Length >= 9, "Cyrillic shaping failed");
        Check(cyrillic.Runs.Span[0].Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Cyrillic produced a missing glyph");

        var combining = service.Shape(new TextShapeRequest("e\u0301".AsMemory(), 24, new[] { font }, TextDirection.LeftToRight));
        Check(combining.Runs.Length == 1 && combining.Runs.Span[0].Glyphs.Length > 0, "combining mark produced no output");
        Check(combining.Runs.Span[0].Glyphs.Span.ToArray().All(static glyph => glyph.ClusterUtf16 == 0), "combining mark split its cluster");
        Check(combining.Runs.Span[0].Glyphs.Span.ToArray().Any(static glyph =>
                (glyph.Safety & GlyphSafety.UnsafeToBreak) != 0),
            "combining mark safety was not reported");
    }

    private static void ArabicShaping()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, ArabicPath(), "0c25a0f8-19cc-4841-bb8c-a9f9f8ea53d8");
        var shaped = service.Shape(new TextShapeRequest("سلام".AsMemory(), 28, new[] { font }, TextDirection.Auto));
        Check(shaped.Runs.Length == 1 && shaped.Runs.Span[0].Direction == TextDirection.RightToLeft, "Arabic direction was not resolved");
        Check(shaped.Runs.Span[0].BidiLevel % 2 == 1, "Arabic bidi level is not odd");
        Check(shaped.Runs.Span[0].Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Arabic produced a missing glyph");
        Check(shaped.Runs.Span[0].Glyphs.Span.ToArray().Any(static glyph =>
                (glyph.Safety & GlyphSafety.UnsafeToConcat) != 0),
            "Arabic joining safety was not reported");
    }

    private static void MixedBidirectionalText()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "9c87085b-0b86-4c8d-bf5f-8a31ca2485c2");
        var shaped = service.Shape(new TextShapeRequest("abc אבג 123".AsMemory(), 24, new[] { font }));
        Check(shaped.Runs.Length >= 3, "mixed bidi text was not split into visual directional runs");
        Check(shaped.Runs.Span.ToArray().Any(static run => run.Direction == TextDirection.RightToLeft && run.BidiLevel % 2 == 1),
            "mixed bidi text has no odd RTL run");
        Check(shaped.Runs.Span[0].Direction == TextDirection.LeftToRight, "mixed bidi paragraph did not keep its LTR base run");
        Check(shaped.Runs.Span.ToArray().All(static run => run.Glyphs.Length > 0), "mixed bidi output contains an empty run");
    }

    private static void FontFallback()
    {
        using var service = new SixLaborsTextService();
        var latin = Open(service, LatinPath(), "4e71f35c-176c-4fdd-8973-8d1fd9ebd5d8");
        var arabic = Open(service, ArabicPath(), "f7a8aeb5-e315-45e9-8e5c-37f62de20ee6");
        var shaped = service.Shape(new TextShapeRequest("Aس".AsMemory(), 24, new[] { latin, arabic }));
        Check(shaped.Runs.Length == 2, "fallback did not split runs by font");
        Check(shaped.Runs.Span[0].Font == latin && shaped.Runs.Span[1].Font == arabic, "fallback selected the wrong font");
        Check(shaped.Runs.Span[1].SourceRange.StartUtf16 == 1, "fallback source range is not preserved");
    }

    private static void BidiControls()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "1deefea5-197a-4d34-969d-f169ae2c09ee");
        var text = "A \u202Bאבג\u202C B";
        var shaped = service.Shape(new TextShapeRequest(text.AsMemory(), 24, new[] { font }));
        var runs = shaped.Runs.Span.ToArray();
        Check(runs.Any(static run => run.Direction == TextDirection.RightToLeft && run.BidiLevel % 2 == 1),
            "explicit RTL embedding did not produce an odd run");
        Check(runs.All(run => run.SourceRange.StartUtf16 >= 0 && run.SourceRange.EndUtf16 <= text.Length),
            "bidi run source range is outside the original text");
        Check(runs.All(static run => run.Glyphs.Length > 0), "bidi controls produced an empty shaped run");
    }

    private static void BidiBoundaries()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "f5f2d6b7-1d0a-4f5b-89b7-c57d38fa4d23");

        var text = "Delta Editor.";
        var shaped = service.Shape(new TextShapeRequest(text.AsMemory(), 24, new[] { font }));
        Check(shaped.TextLengthUtf16 == text.Length, "bidi boundary changed the source length");
        Check(shaped.Runs.Span.ToArray().All(run => run.SourceRange.EndUtf16 <= text.Length),
            "terminal neutral run escaped the source range");
        Check(shaped.Runs.Span.ToArray().All(static run => run.Glyphs.Length > 0),
            "terminal neutral run produced empty output");

        var source = "xxDelta Editor.yy";
        var slice = source.AsMemory(2, text.Length);
        var sliced = service.Shape(new TextShapeRequest(slice, 24, new[] { font }));
        Check(sliced.TextLengthUtf16 == text.Length, "sliced input retained an external source offset");
        Check(sliced.Runs.Span.ToArray().All(run => run.SourceRange.StartUtf16 >= 0 && run.SourceRange.EndUtf16 <= text.Length),
            "sliced input produced an external source range");
    }

    private static void BidiNumbers()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "7c2a2b33-1614-47f7-a3bb-24fc427693f0");
        var shaped = service.Shape(new TextShapeRequest("אבג 123".AsMemory(), 24, new[] { font }));
        var runs = shaped.Runs.Span.ToArray();
        Check(runs.Any(static run => run.Direction == TextDirection.RightToLeft), "RTL paragraph lost its RTL run");
        Check(runs.Any(static run => run.Direction == TextDirection.LeftToRight && run.BidiLevel % 2 == 0),
            "European numbers were not isolated as an even LTR run");
    }

    private static void BidiUnicodeClasses()
    {
        var expectedClasses = new (int CodePoint, BidiClass Class)[]
        {
            ('A', BidiClass.L), ('\u05D0', BidiClass.R), ('\u0627', BidiClass.Al), ('1', BidiClass.En),
            ('\u0661', BidiClass.An), ('+', BidiClass.Es), ('$', BidiClass.Et), (',', BidiClass.Cs),
            ('\u0301', BidiClass.Nsm), ('\u00AD', BidiClass.Bn), ('\u2029', BidiClass.B), ('\t', BidiClass.S),
            (' ', BidiClass.Ws), ('!', BidiClass.On), ('\u202A', BidiClass.Lre), ('\u202B', BidiClass.Rle),
            ('\u202C', BidiClass.Pdf), ('\u2066', BidiClass.Lri), ('\u2067', BidiClass.Rli),
            ('\u2068', BidiClass.Fsi), ('\u2069', BidiClass.Pdi)
        };
        foreach (var expected in expectedClasses)
        {
            Check(UnicodeBidiData.Get(expected.CodePoint) == expected.Class,
                $"Unicode bidi table classified U+{expected.CodePoint:X4} incorrectly");
        }

        var text = "A\u05D0\u0627\u0661+,$\u0301\u00AD\t \u2066B\u2069\u202Aאב\u202C";
        var runs = BidiResolver.Resolve(text, TextDirection.Auto);
        Check(runs.Length > 0, "Unicode bidi classes produced no runs");
        Check(runs.All(run => run.Start >= 0 && run.Length >= 0 && run.Start + run.Length <= text.Length),
            "Unicode bidi class resolution escaped the source range");

        var overflow = new string('\u202A', 140) + "A" + new string('\u202C', 140);
        runs = BidiResolver.Resolve(overflow, TextDirection.LeftToRight);
        Check(runs.Length > 0 && runs.Any(run => run.Length > 0)
            && runs.All(run => run.Start >= 0 && run.Start + run.Length <= overflow.Length),
            "explicit-level overflow escaped the bounded source mapping");
    }

    private static void BidiPairedBrackets()
    {
        var text = "אב (12) ג";
        var runs = BidiResolver.Resolve(text, TextDirection.Auto);
        Check(runs.Length > 0, "paired bracket text produced no runs");
        Check(runs.All(run => run.Start >= 0 && run.Start + run.Length <= text.Length),
            "paired bracket resolution escaped the source range");
    }

    private static void BidiConformanceRegressions()
    {
        CheckBidiConformance(
            "\u05D0\u2066\u202A\u2069\u05D1",
            TextDirection.LeftToRight,
            [1, 1, -1, 1, 1],
            [4, 3, 1, 0]);
        CheckBidiConformance(
            "\u0661\u0009(\u0662)",
            TextDirection.Auto,
            [2, 0, 1, 2, 1],
            [0, 1, 4, 3, 2]);
        CheckBidiConformance(
            "a \u2329b.1\u3009",
            TextDirection.RightToLeft,
            [2, 2, 2, 2, 2, 2, 2],
            [0, 1, 2, 3, 4, 5, 6]);
        CheckBidiConformance(
            "א \u2329ב.1\u3009",
            TextDirection.LeftToRight,
            [1, 1, 1, 1, 1, 2, 1],
            [6, 5, 4, 3, 2, 1, 0]);
    }

    private static void CheckBidiConformance(
        string text,
        TextDirection direction,
        int[] expectedLevels,
        int[] expectedVisualOrder)
    {
        var actual = BidiResolver.ResolveForConformance(text, direction);
        Check(actual.Levels.AsSpan().SequenceEqual(expectedLevels),
            $"bidi levels changed for '{text}'");
        Check(actual.VisualOrder.AsSpan().SequenceEqual(expectedVisualOrder),
            $"bidi visual order changed for '{text}'");
    }

    private static void GlyphImages()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "7f5d6f1f-c6a6-46b8-9d0e-5cc28bdf1bf7");
        var shaped = service.Shape(new TextShapeRequest("A".AsMemory(), 32, new[] { font }));
        var glyph = shaped.Runs.Span[0].Glyphs.Span[0].GlyphId;
        var coverage = service.GenerateGlyphImage(new GlyphImageRequest(font, glyph, 32, GlyphImageMode.Coverage));
        Check(coverage.Encoding == GlyphImageEncoding.CoverageR8, "coverage encoding is wrong");
        Check(coverage.Pixels.Length == coverage.Width * coverage.Height, "coverage image is not tightly packed");
        var sdf = service.GenerateGlyphImage(new GlyphImageRequest(font, glyph, 64, GlyphImageMode.Sdf, 8));
        Check(sdf.Encoding == GlyphImageEncoding.SdfR8 && sdf.Pixels.Length == sdf.Width * sdf.Height, "SDF image contract is wrong");
        Check(sdf.Width > coverage.Width && sdf.Height > coverage.Height, "SDF size did not scale");

        var color = service.GenerateGlyphImage(new GlyphImageRequest(
            font,
            glyph,
            32,
            GlyphImageMode.Color,
            Color: new ColorGlyphOptions(0, new Rgba32(12, 34, 56, 255))));
        Check(color.Encoding == GlyphImageEncoding.ColorRgba8PremultipliedSrgb, "color encoding is wrong");
        Check(color.Pixels.Length == color.Width * color.Height * 4, "color image is not tightly packed");
        Check(color.Pixels.Span.ToArray().Any(static value => value != 0), "color image is empty");

        var repeated = service.GenerateGlyphImage(new GlyphImageRequest(
            font,
            glyph,
            32,
            GlyphImageMode.Color,
            Color: new ColorGlyphOptions(0, new Rgba32(12, 34, 56, 255))));
        Check(color.Width == repeated.Width && color.Height == repeated.Height, "color output is not deterministic in size");
        Check(color.Pixels.Span.SequenceEqual(repeated.Pixels.Span), "color output is not deterministic in pixels");

        var alternate = service.GenerateGlyphImage(new GlyphImageRequest(
            font,
            glyph,
            32,
            GlyphImageMode.Color,
            Color: new ColorGlyphOptions(0, new Rgba32(210, 30, 40, 255))));
        Check(!color.Pixels.Span.SequenceEqual(alternate.Pixels.Span), "foreground color was ignored");
    }

    private static void CpuTextRendering()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "f6c5b2a7-2e5c-4e0e-a827-2e932e68b1c2");
        var request = new TextShapeRequest("CPU".AsMemory(), 32, new[] { font });
        var renderer = new CpuTextRenderer(service);

        var coverage = renderer.Render(request);
        Check(!coverage.IsEmpty, "CPU coverage render is empty");
        Check(coverage.Pixels.Length == coverage.Width * coverage.Height * 4, "CPU coverage image is not RGBA8");
        Check(coverage.StrideBytes == coverage.Width * 4, "CPU coverage stride is wrong");
        Check(coverage.Pixels.Span.ToArray().Any(static value => value != 0), "CPU coverage pixels are empty");
        Check(coverage.Bounds.Width == coverage.Width && coverage.Bounds.Height == coverage.Height,
            "CPU coverage bounds do not describe the bitmap");

        var sdf = renderer.Render(request, new CpuTextRenderOptions(
            GlyphImageMode.Sdf,
            4,
            new Rgba32(24, 48, 96, 220)));
        Check(!sdf.IsEmpty && sdf.Pixels.Span.ToArray().Any(static value => value != 0), "CPU SDF render is empty");

        var msdf = renderer.Render(request, new CpuTextRenderOptions(
            GlyphImageMode.Msdf,
            4,
            new Rgba32(220, 96, 24, 255)));
        Check(!msdf.IsEmpty && msdf.Pixels.Span.ToArray().Any(static value => value != 0), "CPU MSDF render is empty");

        var color = renderer.Render(request, new CpuTextRenderOptions(
            GlyphImageMode.Color,
            0,
            new Rgba32(24, 220, 96, 255)));
        Check(!color.IsEmpty && color.Pixels.Span.ToArray().Any(static value => value != 0), "CPU color render is empty");

        var empty = renderer.Render(new TextShapeRequest(ReadOnlyMemory<char>.Empty, 32, new[] { font }));
        Check(empty.IsEmpty && empty.Pixels.IsEmpty, "CPU empty text produced pixels");

        AssertThrows<ArgumentOutOfRangeException>(
            () => renderer.Render(request, new CpuTextRenderOptions(GlyphImageMode.Unknown, 0, default)),
            "CPU renderer accepted an unknown mode");
        AssertThrows<ArgumentOutOfRangeException>(
            () => renderer.Render(request, new CpuTextRenderOptions(GlyphImageMode.Msdf, 0, default)),
            "CPU renderer accepted a zero distance range");
    }

    private static void InvalidRequests()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "c5b55ba2-62e2-4cb4-8411-7f5af44e749c");
        var validData = File.ReadAllBytes(LatinPath());
        AssertThrows<ArgumentException>(
            () => service.OpenFont(new FontOpenRequest(default, validData, 0)),
            "empty font source was accepted");
        AssertThrows<ArgumentException>(
            () => service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), ReadOnlyMemory<byte>.Empty, 0)),
            "empty font data was accepted");
        AssertThrows<ArgumentException>(
            () => service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), validData, 0,
                new[] { new FontVariation(Tag("wght"), float.NaN) })),
            "non-finite variation was accepted");
        AssertThrows<ArgumentException>(
            () => service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), validData, 0,
                new[] { new FontVariation(OpenTypeTag.Auto, 1) })),
            "automatic variation axis was accepted");
        AssertThrows<ArgumentException>(
            () => service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), new byte[] { 0, 1, 2 }, 0)),
            "malformed font data was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), validData, uint.MaxValue)),
            "unknown font face was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GetFontMetrics(font, float.NaN),
            "NaN pixels-per-em was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GetFontMetrics(font, 4097),
            "excessive metrics size was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GetFontMetrics(font, 0),
            "zero pixels-per-em was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, Array.Empty<FontInstanceId>())),
            "empty fallback chain was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, (TextDirection)99)),
            "unknown text direction was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Language: "   ")),
            "blank language was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("\uD800".AsMemory(), 16, new[] { font })),
            "malformed UTF-16 was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("\uDC00".AsMemory(), 16, new[] { font })),
            "unpaired low surrogate was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Features: new[] {
                new OpenTypeFeature(OpenTypeTag.Auto, 1) })),
            "automatic feature tag was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Features: new[] {
                new OpenTypeFeature(Tag("liga"), 1, new TextRange(int.MaxValue, 1)) })),
            "overflowing feature range was accepted");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Features: new[] {
                new OpenTypeFeature(Tag("liga"), 1, new TextRange(int.MinValue, int.MaxValue)) })),
            "underflowing feature range was accepted");
        AssertThrows<NotSupportedException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Language: "en")),
            "explicit language was silently ignored");
        AssertThrows<NotSupportedException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Script: Tag("Latn"))),
            "explicit script was silently ignored");
        AssertThrows<NotSupportedException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Features: new[] {
                new OpenTypeFeature(Tag("liga"), 0) })),
            "feature disable was silently ignored");
        AssertThrows<NotSupportedException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Features: new[] {
                new OpenTypeFeature(Tag("liga"), 2) })),
            "non-Boolean feature value was silently ignored");
        AssertThrows<NotSupportedException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, Features: new[] {
                new OpenTypeFeature(Tag("liga"), 1, new TextRange(0, 1)) })),
            "ranged feature was silently ignored");
        AssertThrows<NotSupportedException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(
                font,
                1,
                16,
                GlyphImageMode.Color,
                Color: new ColorGlyphOptions(1, new Rgba32(255, 255, 255, 255)))),
            "non-default color palette was silently ignored");
        foreach (var direction in Enum.GetValues<TextDirection>())
        {
            var shaped = service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { font }, direction));
            Check(shaped.Runs.Length > 0, $"direction {direction} produced no output");
        }
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A".AsMemory(), 16, new[] { new FontInstanceId(99, 1) })),
            "unknown fallback handle was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(font, 1, 16, GlyphImageMode.Sdf)),
            "missing SDF distance range was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(font, 1, 16, GlyphImageMode.Sdf, float.PositiveInfinity)),
            "non-finite distance range was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(font, 1, 16, GlyphImageMode.Sdf, 4097)),
            "excessive distance range was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(font, ushort.MaxValue + 1u, 16, GlyphImageMode.Coverage)),
            "out-of-range glyph ID was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(font, 1, 5000, GlyphImageMode.Coverage)),
            "excessive image size was accepted");
        AssertThrows<ArgumentOutOfRangeException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(font, 1, 16, (GlyphImageMode)99)),
            "unknown glyph image mode was accepted");
        AssertThrows<ArgumentException>(
            () => service.GenerateGlyphImage(new GlyphImageRequest(new FontInstanceId(99, 1), 1, 16, GlyphImageMode.Coverage)),
            "unknown image handle was accepted");
        AssertThrows<ArgumentException>(
            () => service.CloseFont(default),
            "invalid close handle was accepted");
    }

    private static void MsdfImage()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "e23b2cc5-ec9e-41d0-b75d-7fc71a1f71cb");
        var shaped = service.Shape(new TextShapeRequest("A".AsMemory(), 32, new[] { font }));
        var image = service.GenerateGlyphImage(new GlyphImageRequest(
            font,
            shaped.Runs.Span[0].Glyphs.Span[0].GlyphId,
            32,
            GlyphImageMode.Msdf,
            8));
        Check(image.Encoding == GlyphImageEncoding.MsdfRgb8, "MSDF encoding is wrong");
        Check(image.Pixels.Length == image.Width * image.Height * 3, "MSDF image is not tightly packed");
    }

    private static void ManagedMsdfGeneration()
    {
        var contours = new GlyphContours();
        contours.BeginContour(0, 0);
        contours.LineTo(100, 0);
        contours.LineTo(100, 100);
        contours.LineTo(0, 100);
        contours.Close();

        if (!ManagedMsdf.TryGenerate(contours, 32, 100, 2, 4, 0xD37A5EEDu,
                out var width, out var height, out var first) || first is null)
        {
            throw new InvalidOperationException("Managed MSDF did not generate a valid image.");
        }

        if (!ManagedMsdf.TryGenerate(contours, 32, 100, 2, 4, 0xD37A5EEDu,
                out var repeatedWidth, out var repeatedHeight, out var second) || second is null)
        {
            throw new InvalidOperationException("Managed MSDF could not repeat generation.");
        }

        Check(width == repeatedWidth && height == repeatedHeight, "managed MSDF dimensions are not deterministic");
        Check(first.AsSpan().SequenceEqual(second), "managed MSDF pixels are not deterministic");
        Check(first.Length == width * height * 3, "managed MSDF payload is not RGB8");

        var hasChannelSeparation = false;
        var hasOutsideDistance = false;
        var hasInsideDistance = false;
        for (var i = 0; i < first.Length; i += 3)
        {
            if (first[i] != first[i + 1] || first[i + 1] != first[i + 2])
            {
                hasChannelSeparation = true;
            }

            hasOutsideDistance |= first[i] < 32 || first[i + 1] < 32 || first[i + 2] < 32;
            hasInsideDistance |= first[i] > 224 || first[i + 1] > 224 || first[i + 2] > 224;
        }

        Check(hasChannelSeparation, "managed MSDF channels did not separate a sharp corner");
        Check(hasOutsideDistance, "managed MSDF has no outside distances");
        Check(hasInsideDistance, "managed MSDF has no inside distances");

        var curved = new GlyphContours();
        curved.BeginContour(0, 50);
        curved.QuadraticTo(0, 0, 50, 0);
        curved.QuadraticTo(100, 0, 100, 50);
        curved.QuadraticTo(100, 100, 50, 100);
        curved.QuadraticTo(0, 100, 0, 50);
        curved.Close();
        if (!ManagedMsdf.TryGenerate(curved, 32, 100, 2, 4, 0xD37A5EEDu,
                out var curvedWidth, out var curvedHeight, out var curvedPixels) || curvedPixels is null)
        {
            throw new InvalidOperationException("Managed MSDF could not flatten a quadratic contour.");
        }

        Check(curvedWidth > 0 && curvedHeight > 0 && curvedPixels.Length == curvedWidth * curvedHeight * 3,
            "managed MSDF quadratic contour payload is malformed");
    }

    private static void FontLifetime()
    {
        var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "3c2d81dd-7343-4580-8b44-6ed7033bb704");
        service.CloseFont(font);
        AssertThrows<ArgumentException>(
            () => service.GetFontMetrics(font, 16),
            "closed font instance was accepted");
        service.Dispose();
        AssertThrows<ObjectDisposedException>(
            () => service.GetFontMetrics(font, 16),
            "disposed service was accepted");
    }

    private static void RepeatedDisposeAndUnknownIds()
    {
        var service = new SixLaborsTextService();
        service.Dispose();
        service.Dispose();
        AssertThrows<ObjectDisposedException>(
            () => service.CloseFont(new FontInstanceId(999, 1)),
            "close on a disposed service did not fail explicitly");

        using var live = new SixLaborsTextService();
        AssertThrows<ArgumentException>(
            () => live.CloseFont(new FontInstanceId(999, 1)),
            "unknown font id did not fail explicitly");
    }

    private static void ConcurrentServiceAccess()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "d6e52f26-0a6a-4c5c-92c7-3a82b7ca7c1f");
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 16, _ =>
        {
            try
            {
                var shaped = service.Shape(new TextShapeRequest("parallel".AsMemory(), 20, new[] { font }));
                Check(shaped.Runs.Length == 1 && shaped.Runs.Span[0].Glyphs.Length > 0, "concurrent shape returned empty output");
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        });

        Check(failures.IsEmpty, $"concurrent access failed: {failures.FirstOrDefault()?.Message}");
    }

    private static void EmptyAndSurrogateBoundaries()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "1e44c96f-5d3d-4f29-888d-e1e5fef75a40");
        var empty = service.Shape(new TextShapeRequest(ReadOnlyMemory<char>.Empty, 20, new[] { font }));
        Check(empty.TextLengthUtf16 == 0 && empty.Runs.Length == 0, "empty text produced a phantom run");

        var valid = service.Shape(new TextShapeRequest("A😀B".AsMemory(), 20, new[] { font }));
        Check(valid.TextLengthUtf16 == 4 && valid.Runs.Span.ToArray().All(static run => run.SourceRange.EndUtf16 <= 4), "surrogate range was split incorrectly");
        AssertThrows<ArgumentException>(
            () => service.Shape(new TextShapeRequest("A\ud83dB".AsMemory(), 20, new[] { font })),
            "unpaired surrogate was passed to the shaping backend");
    }

    private static void IsolatesAndZeroGlyphOutput()
    {
        using var service = new SixLaborsTextService();
        var font = Open(service, LatinPath(), "c0db792f-4eb4-4c5f-a3c7-4c8efcde779a");
        var text = "A \u2067אבג\u2069 B";
        var shaped = service.Shape(new TextShapeRequest(text.AsMemory(), 20, new[] { font }));
        Check(shaped.Runs.Span.ToArray().All(static run => run.Glyphs.Length > 0), "isolates produced a zero-glyph run");
        Check(shaped.Runs.Span.ToArray().All(run => run.SourceRange.StartUtf16 >= 0 && run.SourceRange.EndUtf16 <= text.Length), "isolate range escaped the source text");

        var emptyGlyph = service.GenerateGlyphImage(new GlyphImageRequest(font, 0, 20, GlyphImageMode.Coverage));
        Check(emptyGlyph.Width >= 0 && emptyGlyph.Height >= 0 && emptyGlyph.Pixels.Length == emptyGlyph.Width * emptyGlyph.Height, "zero glyph image is malformed");
    }

    private static FontInstanceId Open(SixLaborsTextService service, string path, string sourceId)
        => service.OpenFont(new FontOpenRequest(
            new FontSourceId(Guid.Parse(sourceId)),
            File.ReadAllBytes(path),
            0));

    private static OpenTypeTag Tag(string tag)
        => new((uint)(tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3]));

    private static string LatinPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
    private static string ArabicPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSansArabic-Regular.ttf");
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
    {
        var hash = 1469598103934665603UL;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
