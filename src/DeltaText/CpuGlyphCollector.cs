using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuGlyphCollector
{
    internal static List<PlacedGlyph> Collect(
        ITextService textService,
        in TextShapeRequest request,
        ShapedText shaped,
        CpuTextRenderOptions options)
    {
        var placements = new List<PlacedGlyph>();
        var pen = float2.zero;
        for (var runIndex = 0; runIndex < shaped.Runs.Length; runIndex++)
        {
            var run = shaped.Runs.Span[runIndex];
            var runPen = pen;
            for (var glyphIndex = 0; glyphIndex < run.Glyphs.Length; glyphIndex++)
            {
                var glyph = run.Glyphs.Span[glyphIndex];
                var image = CpuGlyphImageLoader.Load(
                    textService,
                    run.Font,
                    glyph.GlyphId,
                    request.PixelsPerEm,
                    options);

                if (!image.IsEmpty)
                {
                    placements.Add(new PlacedGlyph(
                        image,
                        runPen + new float2(glyph.OffsetX, glyph.OffsetY)));
                }

                runPen += new float2(glyph.AdvanceX, glyph.AdvanceY);
            }

            pen += new float2(run.AdvanceX, run.AdvanceY);
        }

        return placements;
    }
}
