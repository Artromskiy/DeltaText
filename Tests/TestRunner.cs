using System.Buffers.Binary;
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
        ("fallback produces font-specific runs", FontFallback),
        ("coverage, SDF and color images are unpacked", GlyphImages),
        ("color font table parsing is defensive", ColorFontParsing),
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
        using var service = new HarfBuzzTextService();
        var request = new FontOpenRequest(new FontSourceId(Guid.Parse("f6b70d83-6ab2-4b7b-9f28-7f6a5ecf69c1")), source, 0);
        var font = service.OpenFont(request);
        source[0] ^= 0xff;
        var metrics = service.GetFontMetrics(font, 24);
        Check(metrics.UnitsPerEm > 0 && metrics.Ascent > 0 && metrics.Descent >= 0, "font metrics are invalid");
        service.CloseFont(font);
    }

    private static void LatinShaping()
    {
        using var service = new HarfBuzzTextService();
        var font = Open(service, LatinPath(), "f6b70d83-6ab2-4b7b-9f28-7f6a5ecf69c1");
        var text = "office AV";
        var shaped = service.Shape(new TextShapeRequest(
            text.AsMemory(),
            32,
            new[] { font },
            TextDirection.LeftToRight,
            default,
            "en",
            new[] { new OpenTypeFeature(Tag("liga"), 1), new OpenTypeFeature(Tag("kern"), 1) }));
        Check(shaped.Runs.Length == 1, "Latin text was split unexpectedly");
        Check(shaped.Runs.Span[0].Glyphs.Length < text.Length, "ligature shaping did not compact the glyph sequence");
        Check(shaped.Runs.Span[0].Glyphs.Span[0].ClusterUtf16 == 0, "first cluster is not anchored at zero");
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

        using var service = new HarfBuzzTextService();
        var font = Open(service, LatinPath(), "b67b3c06-4c70-4cc7-bf52-47e8b0adf16b");
        var shaped = service.Shape(new TextShapeRequest(
            text.AsMemory(),
            pixelsPerEm,
            new[] { font },
            direction,
            Language: "en",
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
        using var service = new HarfBuzzTextService();
        var font = Open(service, LatinPath(), "a11e0e56-39c4-4486-9afc-0e5a8f15f87b");
        var cyrillic = service.Shape(new TextShapeRequest("Привет мир".AsMemory(), 24, new[] { font }, TextDirection.LeftToRight));
        Check(cyrillic.Runs.Length == 1 && cyrillic.Runs.Span[0].Glyphs.Length >= 9, "Cyrillic shaping failed");
        Check(cyrillic.Runs.Span[0].Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Cyrillic produced a missing glyph");

        var combining = service.Shape(new TextShapeRequest("e\u0301".AsMemory(), 24, new[] { font }, TextDirection.LeftToRight));
        Check(combining.Runs.Length == 1 && combining.Runs.Span[0].Glyphs.Length > 0, "combining mark produced no output");
        Check(combining.Runs.Span[0].Glyphs.Span.ToArray().All(static glyph => glyph.ClusterUtf16 == 0), "combining mark split its cluster");
    }

    private static void ArabicShaping()
    {
        using var service = new HarfBuzzTextService();
        var font = Open(service, ArabicPath(), "0c25a0f8-19cc-4841-bb8c-a9f9f8ea53d8");
        var shaped = service.Shape(new TextShapeRequest("سلام".AsMemory(), 28, new[] { font }, TextDirection.Auto));
        Check(shaped.Runs.Length == 1 && shaped.Runs.Span[0].Direction == TextDirection.RightToLeft, "Arabic direction was not resolved");
        Check(shaped.Runs.Span[0].BidiLevel % 2 == 1, "Arabic bidi level is not odd");
        Check(shaped.Runs.Span[0].Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Arabic produced a missing glyph");
    }

    private static void MixedBidirectionalText()
    {
        using var service = new HarfBuzzTextService();
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
        using var service = new HarfBuzzTextService();
        var latin = Open(service, LatinPath(), "4e71f35c-176c-4fdd-8973-8d1fd9ebd5d8");
        var arabic = Open(service, ArabicPath(), "f7a8aeb5-e315-45e9-8e5c-37f62de20ee6");
        var shaped = service.Shape(new TextShapeRequest("Aس".AsMemory(), 24, new[] { latin, arabic }));
        Check(shaped.Runs.Length == 2, "fallback did not split runs by font");
        Check(shaped.Runs.Span[0].Font == latin && shaped.Runs.Span[1].Font == arabic, "fallback selected the wrong font");
        Check(shaped.Runs.Span[1].SourceRange.StartUtf16 == 1, "fallback source range is not preserved");
    }

    private static void BidiControls()
    {
        using var service = new HarfBuzzTextService();
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
        using var service = new HarfBuzzTextService();
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
        using var service = new HarfBuzzTextService();
        var font = Open(service, LatinPath(), "7c2a2b33-1614-47f7-a3bb-24fc427693f0");
        var shaped = service.Shape(new TextShapeRequest("אבג 123".AsMemory(), 24, new[] { font }));
        var runs = shaped.Runs.Span.ToArray();
        Check(runs.Any(static run => run.Direction == TextDirection.RightToLeft), "RTL paragraph lost its RTL run");
        Check(runs.Any(static run => run.Direction == TextDirection.LeftToRight && run.BidiLevel % 2 == 0),
            "European numbers were not isolated as an even LTR run");
    }

    private static void GlyphImages()
    {
        using var service = new HarfBuzzTextService();
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

    private static void ColorFontParsing()
    {
        var font = new byte[86];
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4, 2), 2);
        WriteTable(font, 12, "COLR", 44, 24);
        WriteTable(font, 28, "CPAL", 68, 18);

        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(44, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(46, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(48, 4), 14);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(52, 4), 20);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(56, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(58, 2), 36);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(60, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(62, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(64, 2), 37);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(66, 2), 0);

        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(68, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(70, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(72, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(74, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(76, 4), 14);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(80, 2), 0);
        font[82] = 3;
        font[83] = 2;
        font[84] = 1;
        font[85] = 255;

        var layers = ColorFont.GetLayers(font, 36, new ColorGlyphOptions(0, new Rgba32(9, 8, 7, 6)));
        Check(layers.Length == 1 && layers[0].GlyphId == 37, "COLR layer record was not read");
        Check(layers[0].Color == new Rgba32(1, 2, 3, 255), "CPAL BGRA color was not converted to RGBA");
        font[66] = 0xff;
        font[67] = 0xff;
        var foreground = new Rgba32(9, 8, 7, 6);
        layers = ColorFont.GetLayers(font, 36, new ColorGlyphOptions(0, foreground));
        Check(layers.Length == 1 && layers[0].Color == foreground, "COLR foreground palette was not respected");
        Check(ColorFont.GetLayers(new byte[12], 0, null).Length == 0, "truncated color tables were accepted");
    }

    private static void InvalidRequests()
    {
        using var service = new HarfBuzzTextService();
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
        var malformed = service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), new byte[] { 0, 1, 2 }, 0));
        service.CloseFont(malformed);
        var unknownFace = service.OpenFont(new FontOpenRequest(new FontSourceId(Guid.NewGuid()), validData, uint.MaxValue));
        service.CloseFont(unknownFace);
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
        using var service = new HarfBuzzTextService();
        var font = Open(service, LatinPath(), "e23b2cc5-ec9e-41d0-b75d-7fc71a1f71cb");
        var shaped = service.Shape(new TextShapeRequest("A".AsMemory(), 32, new[] { font }));
        try
        {
            var image = service.GenerateGlyphImage(new GlyphImageRequest(
                font,
                shaped.Runs.Span[0].Glyphs.Span[0].GlyphId,
                32,
                GlyphImageMode.Msdf,
                8));
            Check(image.Encoding == GlyphImageEncoding.MsdfRgb8, "MSDF encoding is wrong");
            Check(image.Pixels.Length == image.Width * image.Height * 3, "MSDF image is not tightly packed");
        }
        catch (DllNotFoundException) when (!RequireNativeSmoke())
        {
            Console.WriteLine("SKIP MSDF native bridge is not present");
        }
    }

    private static void FontLifetime()
    {
        var service = new HarfBuzzTextService();
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
        var service = new HarfBuzzTextService();
        service.Dispose();
        service.Dispose();
        AssertThrows<ObjectDisposedException>(
            () => service.CloseFont(new FontInstanceId(999, 1)),
            "close on a disposed service did not fail explicitly");

        using var live = new HarfBuzzTextService();
        AssertThrows<ArgumentException>(
            () => live.CloseFont(new FontInstanceId(999, 1)),
            "unknown font id did not fail explicitly");
    }

    private static void ConcurrentServiceAccess()
    {
        using var service = new HarfBuzzTextService();
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
        using var service = new HarfBuzzTextService();
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
        using var service = new HarfBuzzTextService();
        var font = Open(service, LatinPath(), "c0db792f-4eb4-4c5f-a3c7-4c8efcde779a");
        var text = "A \u2067אבג\u2069 B";
        var shaped = service.Shape(new TextShapeRequest(text.AsMemory(), 20, new[] { font }));
        Check(shaped.Runs.Span.ToArray().All(static run => run.Glyphs.Length > 0), "isolates produced a zero-glyph run");
        Check(shaped.Runs.Span.ToArray().All(run => run.SourceRange.StartUtf16 >= 0 && run.SourceRange.EndUtf16 <= text.Length), "isolate range escaped the source text");

        var emptyGlyph = service.GenerateGlyphImage(new GlyphImageRequest(font, 0, 20, GlyphImageMode.Coverage));
        Check(emptyGlyph.Width >= 0 && emptyGlyph.Height >= 0 && emptyGlyph.Pixels.Length == emptyGlyph.Width * emptyGlyph.Height, "zero glyph image is malformed");
    }

    private static FontInstanceId Open(HarfBuzzTextService service, string path, string sourceId)
        => service.OpenFont(new FontOpenRequest(
            new FontSourceId(Guid.Parse(sourceId)),
            File.ReadAllBytes(path),
            0));

    private static OpenTypeTag Tag(string tag)
        => new((uint)(tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3]));

    private static void WriteTable(byte[] font, int offset, string tag, int tableOffset, int tableLength)
    {
        for (var i = 0; i < 4; i++)
        {
            font[offset + i] = (byte)tag[i];
        }

        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(offset + 8, 4), (uint)tableOffset);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(offset + 12, 4), (uint)tableLength);
    }

    private static string LatinPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
    private static string ArabicPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSansArabic-Regular.ttf");
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static bool RequireNativeSmoke() => string.Equals(Environment.GetEnvironmentVariable("DELTATEXT_REQUIRE_NATIVE_SMOKE"), "1", StringComparison.OrdinalIgnoreCase);

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
