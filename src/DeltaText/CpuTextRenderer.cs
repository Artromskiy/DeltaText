using Delta.Text.Contract;

namespace Delta.Text;

/// <summary>Options for composing shaped glyph images into a CPU bitmap.</summary>
public readonly record struct CpuTextRenderOptions(
    GlyphImageMode Mode,
    float DistanceRange,
    Rgba32 Foreground);

/// <summary>Owned top-to-bottom RGBA8 CPU rendering result.</summary>
/// <remarks>
/// Pixels are premultiplied sRGB and tightly packed. <see cref="Bounds"/> is
/// relative to the text baseline and describes the pixel rectangle returned by
/// this object. The renderer does not retain the service, shaped text or glyph
/// images after rendering.
/// </remarks>
public sealed class CpuTextImage
{
    internal CpuTextImage(int width, int height, TextBounds bounds, byte[] pixels)
    {
        Width = width;
        Height = height;
        Bounds = bounds;
        Pixels = pixels;
    }

    /// <summary>Bitmap width in pixels.</summary>
    public int Width { get; }

    /// <summary>Bitmap height in pixels.</summary>
    public int Height { get; }

    /// <summary>Pixel rectangle relative to the text baseline.</summary>
    public TextBounds Bounds { get; }

    /// <summary>Bytes per row. The value is always <c>Width * 4</c>.</summary>
    public int StrideBytes => checked(Width * 4);

    /// <summary>Owned tightly packed premultiplied RGBA8 sRGB pixels.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Whether the text produced no visible pixels.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;
}

/// <summary>Composes DeltaText glyph images into an owned CPU bitmap.</summary>
/// <remarks>
/// This is a convenience authoring API over the frozen <see cref="ITextService"/>
/// contract. It owns no font instances and must be used while the supplied
/// service and font IDs remain valid. Output is transparent outside the glyphs;
/// atlas packing and GPU upload remain consumer responsibilities.
/// </remarks>
public sealed class CpuTextRenderer
{
    private static readonly Rgba32 DefaultForeground = new(255, 255, 255, 255);
    private readonly ITextService _textService;

    /// <summary>Creates a renderer that borrows the supplied text service.</summary>
    public CpuTextRenderer(ITextService textService)
    {
        ArgumentNullException.ThrowIfNull(textService);
        _textService = textService;
    }

    /// <summary>Shapes and renders text as coverage with an opaque white color.</summary>
    public CpuTextImage Render(in TextShapeRequest request)
        => Render(request, new CpuTextRenderOptions(GlyphImageMode.Coverage, 0, DefaultForeground));

    /// <summary>Shapes and renders text into an owned RGBA8 CPU bitmap.</summary>
    /// <param name="request">Text and font shaping input forwarded to the service.</param>
    /// <param name="options">Glyph representation, distance range and foreground color.</param>
    public CpuTextImage Render(in TextShapeRequest request, CpuTextRenderOptions options)
    {
        // The output bitmap is intentionally new for every call. The text
        // service owns the bounded glyph-image cache; shaped runs remain the
        // caller's responsibility when a render loop needs to reuse them.
        ValidateOptions(options);
        var shaped = _textService.Shape(request);
        return Render(shaped, options);
    }

    /// <summary>Renders an already shaped result without running shaping again.</summary>
    /// <param name="shaped">Immutable shaped result retained by the caller.</param>
    /// <param name="options">Glyph representation, distance range and foreground color.</param>
    /// <remarks>
    /// Use this overload when the text and shaping inputs are unchanged across
    /// frames. The supplied <see cref="ITextService"/> and font instances must
    /// remain open while this method loads glyph images.
    /// </remarks>
    public CpuTextImage Render(ShapedText shaped, CpuTextRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(shaped);
        ValidateOptions(options);
        var placements = CpuGlyphCollector.Collect(_textService, shaped, options);
        return CpuBitmapComposer.Compose(placements, options);
    }

    private static void ValidateOptions(CpuTextRenderOptions options)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CPU text image mode must be specified.");
        }

        if (options.Mode is GlyphImageMode.Sdf or GlyphImageMode.Msdf)
        {
            if (!float.IsFinite(options.DistanceRange) || options.DistanceRange <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Distance range must be finite and greater than zero.");
            }
        }
        else if (options.DistanceRange != 0)
        {
            throw new ArgumentException("Distance range is valid only for SDF and MSDF output.", nameof(options));
        }
    }
}
