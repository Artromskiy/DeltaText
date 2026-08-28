using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal readonly record struct PlacedGlyph(GlyphImage Image, float2 Origin);

internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    internal int Width => checked(Right - Left);

    internal int Height => checked(Bottom - Top);
}
