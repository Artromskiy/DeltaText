using System.Globalization;
using Delta.Text;

var tests = new (string Name, Action Body)[]
{
    ("font metrics and glyph lookup", FontMetricsAndLookup),
    ("Latin ligature and kerning shaping", LatinShaping),
    ("Cyrillic clusters", CyrillicShaping),
    ("combining mark cluster", CombiningMarkShaping),
    ("Arabic RTL ordering and clusters", ArabicShaping),
    ("positioned run and stable cache output", CacheAndPositionedRun),
    ("font catalog and atlas boundary", CatalogAndAtlasBoundary)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception error)
    {
        Console.WriteLine($"FAIL {test.Name}: {error.Message}");
        Console.WriteLine(error);
        Environment.ExitCode = 1;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed.");

void FontMetricsAndLookup()
{
    using var face = LoadLatin();
    Check(face.UnitsPerEm > 0, "units per em must be positive");
    Check(face.Metrics.Ascender > 0, "ascender must be positive");
    Check(face.GetGlyphId('A') != 0, "Latin glyph lookup failed");
    var metrics = face.GetGlyphMetrics(face.GetGlyphId('A'));
    Check(metrics.AdvanceX > 0 && metrics.Width > 0, "glyph metrics are empty");
}

void LatinShaping()
{
    using var face = LoadLatin();
    var enabled = face.Shape(new TextShapingRequest("office AV", 32, CultureInfo.InvariantCulture));
    var disabled = face.Shape(new TextShapingRequest(
        "office AV", 32, CultureInfo.InvariantCulture, TextDirection.LeftToRight,
        new[] { new TextFeature("liga", false), new TextFeature("kern", false) }));
    Check(enabled.Glyphs.Length < disabled.Glyphs.Length, "liga did not produce a compact glyph sequence");
    Check(enabled.AdvanceX <= disabled.AdvanceX, "enabled kerning/ligature advances grew unexpectedly");
    Check(enabled.PositionedGlyphs.Length == enabled.Glyphs.Length, "positioned glyph count mismatch");
}

void CyrillicShaping()
{
    using var face = LoadLatin();
    var run = face.Shape(new TextShapingRequest("Привет мир", 24, new CultureInfo("ru-RU"), TextDirection.LeftToRight));
    Check(run.Glyphs.Length >= 9, "Cyrillic text was not shaped");
    Check(run.Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Cyrillic produced missing glyphs");
    Check(run.Glyphs.Span[0].Cluster == 0, "first Cyrillic cluster is not anchored at zero");
}

void CombiningMarkShaping()
{
    using var face = LoadLatin();
    var run = face.Shape(new TextShapingRequest("e\u0301", 24, CultureInfo.InvariantCulture));
    Check(run.Glyphs.Length > 0, "combining mark produced no glyphs");
    Check(run.Glyphs.Span.ToArray().All(static glyph => glyph.Cluster == 0), "combining mark was split into separate clusters");
    Check(run.AdvanceX > 0, "combining mark run has no advance");
}

void ArabicShaping()
{
    using var face = LoadArabic();
    var run = face.Shape(new TextShapingRequest("سلام", 28, new CultureInfo("ar"), TextDirection.RightToLeft));
    Check(run.Glyphs.Length > 0, "Arabic text was not shaped");
    Check(run.Glyphs.Span.ToArray().All(static glyph => glyph.GlyphId != 0), "Arabic produced missing glyphs");
    for (var i = 1; i < run.Glyphs.Length; i++)
        Check(run.Glyphs.Span[i - 1].Cluster >= run.Glyphs.Span[i].Cluster, "RTL clusters are not in visual order");
}

void CacheAndPositionedRun()
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
}

void CatalogAndAtlasBoundary()
{
    var catalog = FileFontCatalog.Scan(new[] { FixtureDirectory() });
    Check(catalog.Sources.Length >= 2, "font catalog did not discover fixtures");
    var request = new GlyphAtlasRequest(
        new FontKey("NotoSans-Regular", "regular", "fixture"),
        new uint[] { 1, 2 }, 32, 4, 8, GlyphAtlasMode.Msdf);
    Check(request.GlyphIds.Length == 2 && request.Mode == GlyphAtlasMode.Msdf, "atlas request lost its value contract");
}

FontFace LoadLatin() => FontFace.LoadFile(
    new FontKey("NotoSans", "regular", "fixture:noto-sans"),
    Path.Combine(FixtureDirectory(), "NotoSans-Regular.ttf"));

FontFace LoadArabic() => FontFace.LoadFile(
    new FontKey("NotoSansArabic", "regular", "fixture:noto-sans-arabic"),
    Path.Combine(FixtureDirectory(), "NotoSansArabic-Regular.ttf"));

string FixtureDirectory() => Path.Combine(AppContext.BaseDirectory, "Fixtures");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
