using System.Runtime.InteropServices;
using Delta.Text;
using Delta.Text.Contract;

namespace FontCheck;

/// <summary>
/// Renders DeltaText's shaped glyph IDs through the macOS system font stack.
/// </summary>
/// <remarks>
/// This adapter deliberately does not shape text. It consumes the glyph IDs and
/// baseline positions produced by DeltaText, then asks CoreText/CoreGraphics to
/// rasterize them into the same top-to-bottom premultiplied RGBA8 frame. That
/// makes this a rasterization/placement baseline rather than a shaping oracle.
/// The adapter is only available on macOS and owns every native handle it opens.
/// </remarks>
internal sealed class NativeCoreTextRenderer : IDisposable
{
    private readonly NativeCoreTextFont _font;

    internal NativeCoreTextRenderer(string fontPath)
    {
        _font = new NativeCoreTextFont(fontPath);
    }

    public void Dispose() => _font.Dispose();

    internal NativeTextImage Render(CpuTextImage frame, ShapedText shaped, float pixelsPerEm)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(shaped);
        if (frame.IsEmpty)
        {
            throw new ArgumentException("A non-empty DeltaText frame is required.", nameof(frame));
        }

        return Render(shaped, frame.Bounds, frame.Width, frame.Height, pixelsPerEm);
    }

    internal NativeTextImage Render(
        ShapedText shaped,
        TextBounds canvasBounds,
        int width,
        int height,
        float pixelsPerEm)
    {
        ArgumentNullException.ThrowIfNull(shaped);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The CoreText canvas must be non-empty.");
        }

        var context = NativeApi.CreateBitmapContext(width, height);
        try
        {
            NativeApi.ConfigureForGrayscaleFontRasterization(context);
            NativeApi.FlipToTopDownPixels(context, width, height);
            var baselineY = height + canvasBounds.Top;
            var penX = 0f;
            var penY = 0f;

            for (var runIndex = 0; runIndex < shaped.Runs.Length; runIndex++)
            {
                var run = shaped.Runs.Span[runIndex];
                if (run.Glyphs.IsEmpty)
                {
                    penX += run.AdvanceX;
                    penY += run.AdvanceY;
                    continue;
                }

                var glyphs = new ushort[run.Glyphs.Length];
                var positions = new NativeCGPoint[run.Glyphs.Length];
                var runPenX = penX;
                var runPenY = penY;
                for (var glyphIndex = 0; glyphIndex < run.Glyphs.Length; glyphIndex++)
                {
                    var glyph = run.Glyphs.Span[glyphIndex];
                    if (glyph.GlyphId > ushort.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"Glyph ID {glyph.GlyphId} cannot be represented by CoreText CGGlyph.");
                    }

                    glyphs[glyphIndex] = checked((ushort)glyph.GlyphId);
                    positions[glyphIndex] = new NativeCGPoint(
                        runPenX + glyph.OffsetX - canvasBounds.Left,
                        baselineY - runPenY - glyph.OffsetY);
                    runPenX += glyph.AdvanceX;
                    runPenY += glyph.AdvanceY;
                }

                var nativeFont = _font.GetFont(run.PixelsPerEm);
                NativeApi.DrawGlyphs(nativeFont, glyphs, positions, context);
                penX += run.AdvanceX;
                penY += run.AdvanceY;
            }

            NativeApi.Flush(context);
            return new NativeTextImage(width, height, NativeApi.CopyBitmapBytes(context));
        }
        finally
        {
            NativeApi.Release(context);
        }
    }

    internal static bool IsSupported => OperatingSystem.IsMacOS();
}

internal sealed class NativeCoreTextFont : IDisposable
{
    private readonly Dictionary<float, IntPtr> _fonts = new();
    private IntPtr _provider;
    private IntPtr _graphicsFont;
    private int _disposed;

    internal NativeCoreTextFont(string path)
    {
        if (!NativeCoreTextRenderer.IsSupported)
        {
            throw new PlatformNotSupportedException("The CoreText baseline is available only on macOS.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The native baseline font was not found.", path);
        }

        _provider = NativeApi.CreateDataProvider(path);
        if (_provider == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CoreGraphics could not open the font data provider '{path}'.");
        }

        try
        {
            _graphicsFont = NativeApi.CreateGraphicsFont(_provider);
            if (_graphicsFont == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CoreGraphics could not create a font from '{path}'.");
            }
        }
        catch
        {
            NativeApi.Release(_provider);
            _provider = IntPtr.Zero;
            throw;
        }
    }

    internal IntPtr GetFont(float pixelsPerEm)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!float.IsFinite(pixelsPerEm) || pixelsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerEm));
        }

        if (_fonts.TryGetValue(pixelsPerEm, out var font))
        {
            return font;
        }

        font = NativeApi.CreateTextFont(_graphicsFont, pixelsPerEm);
        if (font == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CoreText could not create a {pixelsPerEm:0.###}px font instance.");
        }

        _fonts.Add(pixelsPerEm, font);
        return font;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var font in _fonts.Values)
        {
            NativeApi.Release(font);
        }

        _fonts.Clear();
        NativeApi.Release(_graphicsFont);
        NativeApi.Release(_provider);
        _graphicsFont = IntPtr.Zero;
        _provider = IntPtr.Zero;
    }
}

internal sealed class NativeTextImage
{
    internal NativeTextImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Pixels { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeCGPoint(double x, double y)
{
    internal double X { get; } = x;

    internal double Y { get; } = y;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeCGRect(double x, double y, double width, double height)
{
    internal double X { get; } = x;

    internal double Y { get; } = y;

    internal double Width { get; } = width;

    internal double Height { get; } = height;
}

internal static class NativeApi
{
    private const uint PremultipliedLast = 1;
    private const uint ByteOrder32Big = 0x4000;

    internal static IntPtr CreateDataProvider(string path)
    {
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(path + '\0');
        var pathPointer = Marshal.AllocHGlobal(pathBytes.Length);
        try
        {
            Marshal.Copy(pathBytes, 0, pathPointer, pathBytes.Length);
            return CGDataProviderCreateWithFilename(pathPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    internal static IntPtr CreateGraphicsFont(IntPtr provider)
        => CGFontCreateWithDataProvider(provider);

    internal static IntPtr CreateTextFont(IntPtr graphicsFont, float pixelsPerEm)
        => CTFontCreateWithGraphicsFont(graphicsFont, pixelsPerEm, IntPtr.Zero, IntPtr.Zero);

    internal static IntPtr CreateBitmapContext(int width, int height)
    {
        var colorSpace = CGColorSpaceCreateDeviceRGB();
        if (colorSpace == IntPtr.Zero)
        {
            throw new InvalidOperationException("CoreGraphics could not create the device RGB color space.");
        }

        try
        {
            var context = CGBitmapContextCreate(
                IntPtr.Zero,
                checked((nuint)width),
                checked((nuint)height),
                8,
                checked((nuint)(width * 4)),
                colorSpace,
                ByteOrder32Big | PremultipliedLast);
            if (context == IntPtr.Zero)
            {
                throw new InvalidOperationException("CoreGraphics could not create an RGBA8 bitmap context.");
            }

            return context;
        }
        finally
        {
            Release(colorSpace);
        }
    }

    internal static void ConfigureForGrayscaleFontRasterization(IntPtr context)
    {
        CGContextSetRGBFillColor(context, 1, 1, 1, 1);
        CGContextSetShouldAntialias(context, 1);
        CGContextSetAllowsAntialiasing(context, 1);
        CGContextSetShouldSmoothFonts(context, 0);
        CGContextSetAllowsFontSmoothing(context, 0);
        CGContextSetShouldSubpixelPositionFonts(context, 1);
        CGContextSetAllowsFontSubpixelPositioning(context, 1);
    }

    internal static void FlipToTopDownPixels(IntPtr context, int width, int height)
    {
        CGContextTranslateCTM(context, 0, height);
        CGContextScaleCTM(context, 1, -1);
        CGContextClearRect(context, new NativeCGRect(0, 0, width, height));
    }

    internal static void DrawGlyphs(
        IntPtr font,
        ushort[] glyphs,
        NativeCGPoint[] positions,
        IntPtr context)
        => CTFontDrawGlyphs(font, glyphs, positions, checked((nuint)glyphs.Length), context);

    internal static void Flush(IntPtr context) => CGContextFlush(context);

    internal static byte[] CopyBitmapBytes(IntPtr context)
    {
        var data = CGBitmapContextGetData(context);
        if (data == IntPtr.Zero)
        {
            throw new InvalidOperationException("CoreGraphics returned no bitmap memory.");
        }

        var width = checked((int)CGBitmapContextGetWidth(context));
        var height = checked((int)CGBitmapContextGetHeight(context));
        var rowBytes = checked(width * 4);
        var coreGraphicsBytes = new byte[checked(rowBytes * height)];
        Marshal.Copy(data, coreGraphicsBytes, 0, coreGraphicsBytes.Length);
        var bytes = new byte[coreGraphicsBytes.Length];
        for (var row = 0; row < height; row++)
        {
            coreGraphicsBytes.AsSpan(row * rowBytes, rowBytes)
                .CopyTo(bytes.AsSpan((height - row - 1) * rowBytes, rowBytes));
        }

        return bytes;
    }

    internal static void Release(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            CFRelease(handle);
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern IntPtr CGDataProviderCreateWithFilename(IntPtr filename);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern IntPtr CGFontCreateWithDataProvider(IntPtr provider);

    [DllImport("/System/Library/Frameworks/CoreText.framework/CoreText", ExactSpelling = true)]
    private static extern IntPtr CTFontCreateWithGraphicsFont(
        IntPtr graphicsFont,
        double size,
        IntPtr matrix,
        IntPtr attributes);

    [DllImport("/System/Library/Frameworks/CoreText.framework/CoreText", ExactSpelling = true)]
    private static extern void CTFontDrawGlyphs(
        IntPtr font,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] ushort[] glyphs,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] NativeCGPoint[] positions,
        nuint count,
        IntPtr context);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern IntPtr CGColorSpaceCreateDeviceRGB();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern IntPtr CGBitmapContextCreate(
        IntPtr data,
        nuint width,
        nuint height,
        nuint bitsPerComponent,
        nuint bytesPerRow,
        IntPtr colorSpace,
        uint bitmapInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern IntPtr CGBitmapContextGetData(IntPtr context);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern nuint CGBitmapContextGetWidth(IntPtr context);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern nuint CGBitmapContextGetHeight(IntPtr context);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetRGBFillColor(IntPtr context, double red, double green, double blue, double alpha);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetShouldAntialias(IntPtr context, byte shouldAntialias);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetAllowsAntialiasing(IntPtr context, byte allowsAntialiasing);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetShouldSmoothFonts(IntPtr context, byte shouldSmoothFonts);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetAllowsFontSmoothing(IntPtr context, byte allowsFontSmoothing);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetShouldSubpixelPositionFonts(IntPtr context, byte shouldSubpixelPositionFonts);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextSetAllowsFontSubpixelPositioning(IntPtr context, byte allowsFontSubpixelPositioning);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextTranslateCTM(IntPtr context, double tx, double ty);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextScaleCTM(IntPtr context, double sx, double sy);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextClearRect(IntPtr context, NativeCGRect rect);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGContextFlush(IntPtr context);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", ExactSpelling = true)]
    private static extern void CFRelease(IntPtr handle);
}
