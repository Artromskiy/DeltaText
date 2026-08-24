using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Delta.Text.Tests;

internal static class TestRunner
{
    private static readonly uint[] MissingGlyph = [0];
    private static readonly uint[] MsdfGlyph = [3];
    private static readonly uint[] MtsdfGlyph = [4];

    private static readonly (string Name, Action Body)[] Tests =
    [
        ("font metrics and glyph lookup", FontMetricsAndLookup),
        ("packaged HarfBuzz resolver", PackagedHarfBuzzResolver),
        ("Latin ligature and kerning shaping", LatinShaping),
        ("Cyrillic clusters", CyrillicShaping),
        ("combining mark cluster", CombiningMarkShaping),
        ("Arabic RTL ordering and clusters", ArabicShaping),
        ("positioned run and stable cache output", CacheAndPositionedRun),
        ("bounded staged handoff and MTSDF result", StagedHandoffAndBudget),
        ("public glyph bitmap factory contract", GlyphBitmapFactory),
        ("grayscale metrics scale with pixel size", GrayscaleMetricScaling),
        ("grayscale atlas generator and export smoke", AtlasSmoke),
        ("MSDF native atlas smoke", MsdfSmoke)
    ];

    public static void Run(string[] args)
    {
        if (TryExportAtlasFixture(args))
        {
            return;
        }

        var passed = 0;
        foreach (var test in Tests)
        {
            test.Body();
            Console.WriteLine($"PASS {test.Name}");
            passed++;
        }

        Console.WriteLine($"{passed}/{Tests.Length} tests passed.");
    }

    private static bool TryExportAtlasFixture(string[] args)
    {
        const string flag = "--export-atlas-fixture";
        var index = Array.IndexOf(args, flag);
        if (index < 0)
        {
            return false;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException("Missing export directory after --export-atlas-fixture.");
        }

        var outputDirectory = args[index + 1];
        Directory.CreateDirectory(outputDirectory);
        ExportAtlasFixture(outputDirectory);
        Console.WriteLine(outputDirectory);
        return true;
    }

    private static void ExportAtlasFixture(string outputDirectory)
    {
        using var face = LoadLatin();
        var generator = new GlyphAtlasGenerator();
        var request = new GlyphAtlasRequest(
            face.Key,
            new uint[] { face.GetGlyphId('A'), face.GetGlyphId('V'), face.GetGlyphId('g') },
            40,
            6,
            8,
            GlyphAtlasMode.Grayscale);
        var result = generator.Generate(face, request);

        var exportRoot = Path.Combine(outputDirectory, "DeltaTextAtlasFixture");
        Directory.CreateDirectory(exportRoot);

        foreach (var page in result.Pages.Span)
        {
            var pixelData = page.Pixels.ToArray();
            var handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
            try
            {
                var info = new SKImageInfo(page.Width, page.Height, SKColorType.Gray8, SKAlphaType.Opaque);
                using var image = SKImage.FromPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
                SaveImage(image, Path.Combine(exportRoot, $"page-{page.PageIndex:000}.png"));
            }
            finally
            {
                handle.Free();
            }
        }

        File.WriteAllText(Path.Combine(exportRoot, "atlas.json"), BuildAtlasSummary(result));
    }

    private static void SaveImage(SKImage image, string path)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);
    }

    private static string BuildAtlasSummary(GlyphAtlasResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"font\": \"{result.Request.Font.SourceId}\",");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"mode\": \"{result.Request.Mode}\",");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"pixelSize\": {result.Request.PixelSize},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"pages\": {result.Pages.Length},");
        builder.AppendLine("  \"glyphs\": [");
        for (var i = 0; i < result.Glyphs.Length; i++)
        {
            var glyph = result.Glyphs.Span[i];
            builder.Append("    {");
            builder.Append(
                CultureInfo.InvariantCulture,
                $"\"glyphId\": {glyph.GlyphId}, \"pageIndex\": {glyph.PageIndex}, \"u0\": {glyph.U0}, \"v0\": {glyph.V0}, \"u1\": {glyph.U1}, \"v1\": {glyph.V1}, \"width\": {glyph.Width}, \"height\": {glyph.Height}, \"stride\": {glyph.Stride}");
            builder.AppendLine(i + 1 == result.Glyphs.Length ? "}" : "},");
        }

        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void FontMetricsAndLookup()
    {
        using var face = LoadLatin();
        Check(face.UnitsPerEm > 0, "units per em must be positive");
        Check(face.Metrics.Ascender > 0, "ascender must be positive");
        Check(face.GetGlyphId('A') != 0, "Latin glyph lookup failed");
        var metrics = face.GetGlyphMetrics(face.GetGlyphId('A'));
        Check(metrics.AdvanceX > 0 && metrics.Width > 0, "glyph metrics are empty");
    }

    private static void PackagedHarfBuzzResolver()
    {
        var candidates = NativeLibraryResolver.CandidatePaths(AppContext.BaseDirectory, "libHarfBuzzSharp");
        Check(candidates.Count == candidates.Distinct(StringComparer.Ordinal).Count(), "native candidates are not unique");
        var packaged = candidates.FirstOrDefault(static path =>
            path.Contains($"{Path.DirectorySeparatorChar}runtimes{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && File.Exists(path));
        Check(packaged is not null, "packaged HarfBuzz runtime asset was not found in candidate paths");
        var packagedIndex = candidates.ToList().IndexOf(packaged ?? string.Empty);
        Check(packagedIndex > 0, "packaged runtime asset must be a fallback after local paths");
        using var face = LoadLatin();
        Check(face.UnitsPerEm > 0, $"packaged HarfBuzz failed to load from {packaged}");
    }

    private static void LatinShaping()
    {
        using var face = LoadLatin();
        var enabled = face.Shape(new TextShapingRequest("office AV", 32, CultureInfo.InvariantCulture));
        var disabled = face.Shape(new TextShapingRequest(
            "office AV",
            32,
            CultureInfo.InvariantCulture,
            TextDirection.LeftToRight,
            new[] { new TextFeature("liga", false), new TextFeature("kern", false) }));
        Check(enabled.Glyphs.Length < disabled.Glyphs.Length, "liga did not produce a compact glyph sequence");
        Check(enabled.AdvanceX <= disabled.AdvanceX, "enabled kerning/ligature advances grew unexpectedly");
        Check(enabled.PositionedGlyphs.Length == enabled.Glyphs.Length, "positioned glyph count mismatch");
    }

    private static void CyrillicShaping()
    {
        using var face = LoadLatin();
        var run = face.Shape(new TextShapingRequest("Привет мир", 24, new CultureInfo("ru-RU"), TextDirection.LeftToRight));
        Check(run.Glyphs.Length >= 9, "Cyrillic text was not shaped");
        Check(run.Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Cyrillic produced missing glyphs");
        Check(run.Glyphs.Span[0].Cluster == 0, "first Cyrillic cluster is not anchored at zero");
    }

    private static void CombiningMarkShaping()
    {
        using var face = LoadLatin();
        var run = face.Shape(new TextShapingRequest("e\u0301", 24, CultureInfo.InvariantCulture));
        Check(run.Glyphs.Length > 0, "combining mark produced no glyphs");
        Check(run.Glyphs.Span.ToArray().All(static glyph => glyph.Cluster == 0), "combining mark was split into separate clusters");
        Check(run.AdvanceX > 0, "combining mark run has no advance");
    }

    private static void ArabicShaping()
    {
        using var face = LoadArabic();
        var run = face.Shape(new TextShapingRequest("سلام", 28, new CultureInfo("ar"), TextDirection.RightToLeft));
        Check(run.Glyphs.Length > 0, "Arabic text was not shaped");
        Check(run.Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Arabic produced missing glyphs");
        for (var i = 1; i < run.Glyphs.Length; i++)
        {
            Check(run.Glyphs.Span[i - 1].Cluster >= run.Glyphs.Span[i].Cluster, "RTL clusters are not in visual order");
        }
    }

    private static void CacheAndPositionedRun()
    {
        using var face = LoadLatin();
        var shaper = new TextShaper();
        var request = new TextShapingRequest("cache me", 20, CultureInfo.InvariantCulture);
        var first = shaper.Shape(face, request);
        var second = shaper.Shape(face, request);
        Check(ReferenceEquals(first, second), "cache returned a replacement run");
        Check(first.Glyphs.Length == first.PositionedGlyphs.Length, "run arrays are not aligned");
        Check(first.PositionedGlyphs.Span[0].X == 0, "first glyph is not positioned at the origin");
        Check(first.PositionedGlyphs.Span[1].X >= first.PositionedGlyphs.Span[0].X, "glyph positions are not cumulative");
        Check(first.PositionedGlyphs.Span[1].AdvanceX > 0, "native glyph-position stride produced an empty advance");
    }

    private static void AtlasSmoke()
    {
        using var face = LoadLatin();
        var generator = new GlyphAtlasGenerator();
        var glyphs = new uint[] { face.GetGlyphId('A'), face.GetGlyphId('V'), face.GetGlyphId('g'), face.GetGlyphId('é') };
        var request = new GlyphAtlasRequest(face.Key, glyphs, 40, 6, 8, GlyphAtlasMode.Grayscale);
        var first = generator.Generate(face, request);
        var second = generator.Generate(face, request);
        Check(first.Pages.Span[0].Pixels.Span.SequenceEqual(second.Pages.Span[0].Pixels.Span), "atlas regeneration changed page pixels");
        Check(first.Glyphs.Span[0].Pixels.Span.SequenceEqual(second.Glyphs.Span[0].Pixels.Span), "atlas regeneration changed glyph pixels");
        Check(first.Pages.Length > 0, "atlas generator produced no pages");
        Check(first.Glyphs.Length == glyphs.Length, "atlas glyph count mismatch");
        Check(first.Glyphs.Span.ToArray().All(static glyph => glyph.PageIndex >= 0), "glyph page indices are invalid");
        Check(first.Glyphs.Span.ToArray().All(static glyph => glyph.U1 > glyph.U0 && glyph.V1 > glyph.V0), "glyph UVs are invalid");
        Check(first.Pages.Span[0].Pixels.Length > 0, "atlas page has no pixels");
        Check(first.Pages.Span[0].Pixels.Span.ToArray().Any(static b => b != 0), "atlas page is empty");
    }

    private static void StagedHandoffAndBudget()
    {
        using var face = LoadLatin();
        var shaper = new TextShaper(new TextCacheBudget(1, 4096));
        var run = shaper.Shape(face, new TextShapingRequest("A", 32, CultureInfo.InvariantCulture));
        var generator = new GlyphAtlasGenerator(new TextCacheBudget(2, 4096));
        var request = new GlyphAtlasRequest(face.Key, new[] { face.GetGlyphId('A') }, 32, 4, 8, GlyphAtlasMode.Grayscale);
        var bitmapResult = generator.TryGenerateGlyph(face, request, request.GlyphIds.Span[0]);
        var bitmap = bitmapResult.Bitmap;
        Check(bitmapResult.Succeeded && bitmap is not null, "staged bitmap generation failed");
        if (bitmap is null)
        {
            throw new InvalidOperationException("staged bitmap generation returned no bitmap");
        }
        var handoff = new GlyphRenderData(
            run,
            new[] { new PositionedGlyphBitmap(run.PositionedGlyphs.Span[0], bitmap) });
        Check(handoff.Glyphs.Length == 1, "renderer handoff lost the positioned glyph");
        Check(handoff.Glyphs.Span[0].Bitmap.Request.Mode == GlyphAtlasMode.Grayscale, "handoff changed pixel mode");

        var unsupported = generator.TryGenerateGlyph(face,
            new GlyphAtlasRequest(face.Key, new[] { face.GetGlyphId('A') }, 32, 4, 8, GlyphAtlasMode.Mtsdf),
            face.GetGlyphId('A'));
        Check(unsupported.Status == GlyphBitmapStatus.UnsupportedMode && !unsupported.Succeeded,
            "MTSDF must be an explicit unsupported result");
    }

    private static void GlyphBitmapFactory()
    {
        var font = new FontKey("fixture", "regular", "fixture:bitmap");
        var grayRequest = new GlyphAtlasRequest(font, MissingGlyph, 16, 1, 4, GlyphAtlasMode.Grayscale);
        var grayPixels = new byte[] { 7, 8, 9, 10 };
        var gray = GlyphBitmap.Create(grayRequest, 0, 2, 2, 2, 0, 1, 2, grayPixels);
        grayPixels[0] = 99;
        Check(gray.GlyphId == 0 && gray.Pixels.Span[0] == 7, "bitmap factory did not own its pixel copy");

        var msdfRequest = new GlyphAtlasRequest(font, MsdfGlyph, 16, 1, 4, GlyphAtlasMode.Msdf);
        var msdf = GlyphBitmap.Create(msdfRequest, 3, 1, 1, 3, 0, 1, 1, new byte[] { 1, 2, 3 });
        Check(msdf.Stride == 3 && msdf.Pixels.Length == 3, "MSDF bitmap contract is invalid");

        var mtsdfRequest = new GlyphAtlasRequest(font, MtsdfGlyph, 16, 1, 4, GlyphAtlasMode.Mtsdf);
        var mtsdf = GlyphBitmap.Create(mtsdfRequest, 4, 1, 1, 4, 0, 1, 1, new byte[] { 1, 2, 3, 4 });
        Check(mtsdf.Request.Mode == GlyphAtlasMode.Mtsdf, "MTSDF data factory rejected a representable mode");

        AssertThrows<ArgumentOutOfRangeException>(() => GlyphBitmap.Create(grayRequest, 1, 2, 2, 1, 0, 0, 0, new byte[4]), "short stride accepted");
        AssertThrows<ArgumentException>(() => GlyphBitmap.Create(grayRequest, 1, 2, 2, 2, 0, 0, 0, new byte[3]), "short pixel memory accepted");
        AssertThrows<ArgumentException>(() => GlyphBitmap.Create(grayRequest, 1, 1, 1, 1, float.NaN, 0, 0, new byte[1]), "nonfinite metrics accepted");
    }

    private static void GrayscaleMetricScaling()
    {
        using var face = LoadLatin();
        var glyphId = face.GetGlyphId('A');
        var generator = new GlyphAtlasGenerator();
        var small = generator.Generate(face, new GlyphAtlasRequest(face.Key, new[] { glyphId }, 32, 4, 8, GlyphAtlasMode.Grayscale)).Glyphs.Span[0];
        var large = generator.Generate(face, new GlyphAtlasRequest(face.Key, new[] { glyphId }, 64, 8, 16, GlyphAtlasMode.Grayscale)).Glyphs.Span[0];
        var metrics = face.GetGlyphMetrics(glyphId);
        var expectedSmallScale = 32f / face.UnitsPerEm;
        var expectedLargeScale = 64f / face.UnitsPerEm;
        Check(MathF.Abs(small.BearingX - metrics.BearingX * expectedSmallScale) < 0.001f, "small grayscale bearing X is not scaled");
        Check(MathF.Abs(large.BearingX - metrics.BearingX * expectedLargeScale) < 0.001f, "large grayscale bearing X is not scaled");
        Check(MathF.Abs(small.BearingY - metrics.BearingY * expectedSmallScale) < 0.001f, "small grayscale bearing Y is not scaled");
        Check(MathF.Abs(large.BearingY - metrics.BearingY * expectedLargeScale) < 0.001f, "large grayscale bearing Y is not scaled");
        Check(MathF.Abs(small.AdvanceX - metrics.AdvanceX * expectedSmallScale) < 0.001f, "small grayscale advance is not scaled");
        Check(MathF.Abs(large.AdvanceX - metrics.AdvanceX * expectedLargeScale) < 0.001f, "large grayscale advance is not scaled");
        Check(MathF.Abs(large.AdvanceX / small.AdvanceX - 2f) < 0.001f, "grayscale metrics are not proportional across sizes");
    }

    private static void MsdfSmoke()
    {
        using var face = LoadLatin();
        var generator = new GlyphAtlasGenerator();
        var glyphs = new uint[] { 0, face.GetGlyphId('A'), face.GetGlyphId('V'), face.GetGlyphId('g') };
        var request = new GlyphAtlasRequest(face.Key, glyphs, 40, 6, 8, GlyphAtlasMode.Msdf);
        try
        {
            var first = generator.Generate(face, request);
            var second = generator.Generate(face, request);
            Check(first.Pages.Length > 0 && first.Pages.Span[0].Pixels.Length == first.Pages.Span[0].Width * first.Pages.Span[0].Height * 3, "MSDF page is not RGB8");
            Check(first.Glyphs.Span.ToArray().All(static glyph => glyph.Stride == glyph.Width * 3), "MSDF glyph stride is not RGB8");
            Check(first.Pages.Span[0].Pixels.Span.ToArray().Any(static value => value != 0), "MSDF page is empty");
            Check(first.Pages.Span[0].Pixels.Span.SequenceEqual(second.Pages.Span[0].Pixels.Span), "MSDF cache output is not stable");

            var highDpi = generator.Generate(face, new GlyphAtlasRequest(face.Key, glyphs, 80, 10, 16, GlyphAtlasMode.Msdf));
            Check(highDpi.Pages.Length > 0 && highDpi.Glyphs.Span.ToArray().Max(static glyph => glyph.Width) > first.Glyphs.Span.ToArray().Max(static glyph => glyph.Width), "MSDF DPI scaling did not change geometry");

            using var arabicFace = LoadArabic();
            var arabicGlyph = arabicFace.GetGlyphId('س');
            var arabic = generator.Generate(arabicFace, new GlyphAtlasRequest(arabicFace.Key, new[] { arabicGlyph }, 40, 6, 8, GlyphAtlasMode.Msdf));
            Check(arabic.Glyphs.Span[0].Stride == arabic.Glyphs.Span[0].Width * 3, "Arabic MSDF glyph is not RGB8");
        }
        catch (DllNotFoundException)
        {
            // The managed package does not build native binaries implicitly. The
            // native smoke is run by the platform packaging job when the library
            // is present beside the test executable.
            if (RequireNativeSmoke())
            {
                throw new InvalidOperationException("Native MSDF smoke was required, but DeltaTextMsdf could not be loaded.");
            }
        }
        catch (EntryPointNotFoundException)
        {
            throw new InvalidOperationException("DeltaTextMsdf is present but its ABI is incomplete.");
        }
    }

    private static bool RequireNativeSmoke() => string.Equals(
        Environment.GetEnvironmentVariable("DELTATEXT_REQUIRE_NATIVE_SMOKE"),
        "1",
        StringComparison.OrdinalIgnoreCase);

    private static FontFace LoadLatin() => FontFace.LoadFile(
        new FontKey("NotoSans", "regular", "fixture:noto-sans"),
        Path.Combine(FixtureDirectory(), "NotoSans-Regular.ttf"));

    private static FontFace LoadArabic() => FontFace.LoadFile(
        new FontKey("NotoSansArabic", "regular", "fixture:noto-sans-arabic"),
        Path.Combine(FixtureDirectory(), "NotoSansArabic-Regular.ttf"));

    private static string FixtureDirectory() => Path.Combine(AppContext.BaseDirectory, "Fixtures");

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
