using Delta.Text.Contract;

namespace Delta.Text.Tests;

internal static class TestRunner
{
    private static readonly (string Name, Action Body)[] Tests =
    [
        ("open font owns source bytes and metrics", OpenFont),
        ("Latin shaping preserves clusters and ligatures", LatinShaping),
        ("Cyrillic and combining marks shape", CyrillicAndCombining),
        ("Arabic shaping resolves RTL", ArabicShaping),
        ("fallback produces font-specific runs", FontFallback),
        ("coverage and SDF images are unpacked", GlyphImages),
        ("MSDF image is optional and renderer-neutral", MsdfImage),
        ("font lifetime rejects closed instances", FontLifetime)
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
        AssertThrows<InvalidOperationException>(
            () => service.GetFontMetrics(font, 16),
            "closed font instance was accepted");
        service.Dispose();
        AssertThrows<ObjectDisposedException>(
            () => service.GetFontMetrics(font, 16),
            "disposed service was accepted");
    }

    private static FontInstanceId Open(HarfBuzzTextService service, string path, string sourceId)
        => service.OpenFont(new FontOpenRequest(
            new FontSourceId(Guid.Parse(sourceId)),
            File.ReadAllBytes(path),
            0));

    private static OpenTypeTag Tag(string tag)
        => new((uint)(tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3]));

    private static string LatinPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular.ttf");
    private static string ArabicPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSansArabic-Regular.ttf");
    private static bool RequireNativeSmoke() => string.Equals(Environment.GetEnvironmentVariable("DELTATEXT_REQUIRE_NATIVE_SMOKE"), "1", StringComparison.OrdinalIgnoreCase);

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
