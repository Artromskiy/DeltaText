using System.Runtime.InteropServices;

namespace Delta.Text;

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
        if (!HasValidInput(contours, pixelSize, unitsPerEm, padding, distanceRange))
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
            if (status != 0)
            {
                FreePixels(bitmap.Pixels);
                return false;
            }

            if (!HasValidBitmap(bitmap))
            {
                FreePixels(bitmap.Pixels);
                return false;
            }

            try
            {
                var managed = new byte[bitmap.Length];
                Marshal.Copy(bitmap.Pixels, managed, 0, managed.Length);
                width = bitmap.Width; height = bitmap.Height; pixels = managed;
                return true;
            }
            finally { FreePixels(bitmap.Pixels); }
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

    private static bool HasValidInput(GlyphContours contours, int pixelSize, int unitsPerEm, int padding, float distanceRange)
    {
        if (contours is null || contours.Contours.Count == 0 || pixelSize <= 0 || unitsPerEm <= 0
            || padding < 0 || !float.IsFinite(distanceRange) || distanceRange <= 0)
        {
            return false;
        }

        foreach (var contour in contours.Contours)
        {
            if (contour.Count < 2)
            {
                return false;
            }

            foreach (var point in contour)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
                    || point.Kind > ContourPointKind.CubicEnd)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasValidBitmap(in Bitmap bitmap)
    {
        if (bitmap.Pixels == IntPtr.Zero || bitmap.Width <= 0 || bitmap.Height <= 0
            || bitmap.Stride <= 0 || bitmap.Length <= 0 || !float.IsFinite(bitmap.DistanceRange)
            || bitmap.DistanceRange <= 0)
        {
            return false;
        }

        var expectedStride = (long)bitmap.Width * 3;
        var expectedLength = expectedStride * bitmap.Height;
        return expectedStride <= int.MaxValue && expectedLength <= int.MaxValue
            && bitmap.Stride == expectedStride && bitmap.Length == expectedLength;
    }

    private static void FreePixels(IntPtr pixels)
    {
        if (pixels != IntPtr.Zero)
        {
            deltatext_msdf_free(pixels);
        }
    }
}
