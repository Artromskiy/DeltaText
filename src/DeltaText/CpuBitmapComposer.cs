using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuBitmapComposer
{
    internal static CpuTextImage Compose(List<PlacedGlyph> placements, CpuTextRenderOptions options)
    {
        if (placements.Count == 0)
        {
            return new CpuTextImage(0, 0, default, Array.Empty<byte>());
        }

        var bounds = CpuTextBounds.Calculate(placements);
        var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
        for (var i = 0; i < placements.Count; i++)
        {
            CpuGlyphBlender.Blend(pixels, bounds, placements[i], options);
        }

        return new CpuTextImage(
            bounds.Width,
            bounds.Height,
            new TextBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            pixels);
    }

}
