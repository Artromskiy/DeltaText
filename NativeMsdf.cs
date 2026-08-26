using System.Runtime.InteropServices;

namespace DeltaText;

internal static class NativeMsdf
{
    private const string Library = "DeltaTextMsdf";
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public float X, Y; public byte Kind; }
    [StructLayout(LayoutKind.Sequential)] internal struct Contour { public IntPtr Points; public int Count; }
    [StructLayout(LayoutKind.Sequential)] internal struct Bitmap { public IntPtr Pixels; public int Length, Width, Height, Stride; public float DistanceRange; }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int deltatext_generate_msdf_from_contours(Contour[] contours, int contourCount, int pixelSize, int unitsPerEm, int padding, float distanceRange, uint edgeSeed, out Bitmap bitmap);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void deltatext_msdf_free(IntPtr pixels);

    internal static bool TryGenerate(GlyphContours contours, int pixelSize, int unitsPerEm, int padding, float distanceRange, out int width, out int height, out ReadOnlyMemory<byte> pixels)
    {
        NativeLibraryResolver.EnsureInitialized();
        width = height = 0; pixels = default;
        if (contours.Contours.Count == 0)
        {
            return false;
        }

        var pins = new GCHandle[contours.Contours.Count];
        var nativeContours = new Contour[pins.Length];
        try
        {
            for (var i = 0; i < pins.Length; i++)
            {
                var source = contours.Contours[i];
                var points = new Point[source.Count];
                for (var j = 0; j < points.Length; j++)
                {
                    points[j] = new Point { X = source[j].X, Y = source[j].Y, Kind = (byte)source[j].Kind };
                }

                pins[i] = GCHandle.Alloc(points, GCHandleType.Pinned);
                nativeContours[i] = new Contour { Points = pins[i].AddrOfPinnedObject(), Count = points.Length };
            }
            var status = deltatext_generate_msdf_from_contours(nativeContours, nativeContours.Length, pixelSize, unitsPerEm, padding, distanceRange, 0xD37A5EEDu, out var bitmap);
            if (status != 0 || bitmap.Pixels == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var managed = new byte[bitmap.Length];
                Marshal.Copy(bitmap.Pixels, managed, 0, managed.Length);
                width = bitmap.Width; height = bitmap.Height; pixels = managed;
                return true;
            }
            finally { deltatext_msdf_free(bitmap.Pixels); }
        }
        finally
        {
            for (var i = 0; i < pins.Length; i++)
            {
                if (pins[i].IsAllocated)
                {
                    pins[i].Free();
                }
            }
        }
    }
}
