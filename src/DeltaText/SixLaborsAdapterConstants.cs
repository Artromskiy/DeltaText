namespace Delta.Text;

/// <summary>Constants that bridge DeltaText pixel units to SixLabors layout units.</summary>
internal static class SixLaborsAdapterConstants
{
    // SixLabors stores font sizes in points. At 72 DPI, one point is one layout pixel.
    // DeltaText passes the requested pixel size as the point size, so changing this
    // value would scale outlines and metrics inconsistently with PixelsPerEm.
    internal const float LayoutDpi = 72F;
}
