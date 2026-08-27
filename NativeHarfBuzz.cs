using System.Runtime.InteropServices;
using System.Text;
using Delta.Text.Contract;

namespace Delta.Text;

internal static unsafe class NativeHarfBuzz
{
    private const string Library = "libHarfBuzzSharp";

    internal const uint MemoryDuplicate = 0;
    internal const uint DirectionLtr = 4;
    internal const uint DirectionRtl = 5;
    internal const uint DirectionTtb = 6;
    internal const uint DirectionBtt = 7;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Feature
    {
        public uint Tag;
        public uint Value;
        public uint Start;
        public uint End;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphInfo
    {
        public uint Codepoint;
        public uint Mask;
        public uint Cluster;
        public uint Var1;
        public uint Var2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphPosition
    {
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
        // hb_glyph_position_t also carries hb_var_int_t var. Keep the
        // managed stride identical to HarfBuzz so the next position is not
        // read from the middle of the native record.
        public uint Var;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphExtents
    {
        public int XBearing;
        public int YBearing;
        public int Width;
        public int Height;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_blob_create(IntPtr data, uint length, uint mode, IntPtr userData, IntPtr destroy);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_blob_destroy(IntPtr blob);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_face_create(IntPtr blob, uint index);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_face_destroy(IntPtr face);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint hb_face_get_upem(IntPtr face);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_font_create(IntPtr face);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_destroy(IntPtr font);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_set_scale(IntPtr font, int xScale, int yScale);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_ot_font_set_funcs(IntPtr font);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void hb_font_set_variations(IntPtr font, byte* variations, int length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint hb_font_get_glyph_extents(IntPtr font, uint glyph, out GlyphExtents extents);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int hb_font_get_glyph_h_advance(IntPtr font, uint glyph);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint hb_font_get_nominal_glyph(IntPtr font, uint unicode, out uint glyph);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_buffer_create();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_buffer_destroy(IntPtr buffer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_buffer_add_utf16(IntPtr buffer, char* text, int textLength, uint itemOffset, int itemLength);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_buffer_guess_segment_properties(IntPtr buffer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_buffer_set_direction(IntPtr buffer, uint direction);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint hb_buffer_get_length(IntPtr buffer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_buffer_get_glyph_infos(IntPtr buffer, out uint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_buffer_get_glyph_positions(IntPtr buffer, out uint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint hb_glyph_info_get_glyph_flags(IntPtr glyphInfo);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_shape(IntPtr font, IntPtr buffer, IntPtr features, uint featureCount);

    internal static IntPtr CreateFont(ReadOnlySpan<byte> data, uint faceIndex, ReadOnlySpan<FontVariation> variations, out IntPtr blob, out IntPtr face, out int unitsPerEm)
    {
        NativeLibraryResolver.EnsureInitialized();
        blob = IntPtr.Zero;
        face = IntPtr.Zero;
        unitsPerEm = 0;
        var font = IntPtr.Zero;
        try
        {
            fixed (byte* dataPtr = data)
            {
                blob = hb_blob_create((IntPtr)dataPtr, checked((uint)data.Length), MemoryDuplicate, IntPtr.Zero, IntPtr.Zero);
            }

            if (blob == IntPtr.Zero)
            {
                throw new InvalidOperationException("HarfBuzz could not create a font blob.");
            }

            face = hb_face_create(blob, faceIndex);
            if (face == IntPtr.Zero)
            {
                throw new InvalidOperationException("HarfBuzz could not create a font face.");
            }

            var rawUnitsPerEm = hb_face_get_upem(face);
            if (rawUnitsPerEm == 0 || rawUnitsPerEm > int.MaxValue)
            {
                throw new InvalidOperationException("The font has no usable units-per-em value.");
            }

            unitsPerEm = (int)rawUnitsPerEm;
            font = hb_font_create(face);
            if (font == IntPtr.Zero)
            {
                throw new InvalidOperationException("HarfBuzz could not create a font object.");
            }

            hb_font_set_scale(font, unitsPerEm, unitsPerEm);
            hb_ot_font_set_funcs(font);
            SetVariations(font, variations);
            return font;
        }
        catch
        {
            DestroyFont(font, face, blob);
            blob = IntPtr.Zero;
            face = IntPtr.Zero;
            unitsPerEm = 0;
            throw;
        }
    }

    internal static RawGlyphMetrics GetGlyphMetrics(IntPtr font, uint glyph, int unitsPerEm)
    {
        var advance = hb_font_get_glyph_h_advance(font, glyph);
        var extents = default(GlyphExtents);
        _ = hb_font_get_glyph_extents(font, glyph, out extents);
        return new RawGlyphMetrics(glyph, advance, 0, extents.XBearing, extents.YBearing, extents.Width, extents.Height, unitsPerEm);
    }

    internal static uint GetGlyph(IntPtr font, uint codepoint)
        => hb_font_get_nominal_glyph(font, codepoint, out var glyph) != 0 ? glyph : 0;

    internal static void Shape(
        IntPtr font,
        string text,
        int clusterOffset,
        TextDirection direction,
        ReadOnlySpan<OpenTypeFeature> requestedFeatures,
        List<RawShapedGlyph> output)
    {
        var buffer = hb_buffer_create();
        if (buffer == IntPtr.Zero)
        {
            throw new InvalidOperationException("HarfBuzz could not create a shaping buffer.");
        }

        try
        {
            fixed (char* textPtr = text)
            {
                hb_buffer_add_utf16(buffer, textPtr, text.Length, 0, text.Length);
            }

            if (direction != TextDirection.Auto)
            {
                hb_buffer_set_direction(buffer, ToDirection(direction));
            }

            hb_buffer_guess_segment_properties(buffer);

            var features = stackalloc Feature[requestedFeatures.Length];
            for (var i = 0; i < requestedFeatures.Length; i++)
            {
                features[i] = new Feature
                {
                    Tag = requestedFeatures[i].Tag.Value,
                    Value = requestedFeatures[i].Value,
                    Start = FeatureStart(requestedFeatures[i], clusterOffset),
                    End = FeatureEnd(requestedFeatures[i], clusterOffset, text.Length)
                };
            }

            hb_shape(font, buffer, (IntPtr)features, checked((uint)requestedFeatures.Length));
            var length = hb_buffer_get_length(buffer);
            var infoPtr = hb_buffer_get_glyph_infos(buffer, out var infoLength);
            var positionPtr = hb_buffer_get_glyph_positions(buffer, out var positionLength);
            var count = checked((int)Math.Min(length, Math.Min(infoLength, positionLength)));
            var infos = (GlyphInfo*)infoPtr;
            var nativePositions = (GlyphPosition*)positionPtr;
            for (var i = 0; i < count; i++)
            {
                var info = infos[i];
                var position = nativePositions[i];
                var safety = (GlyphSafety)hb_glyph_info_get_glyph_flags((IntPtr)(infos + i));
                output.Add(new RawShapedGlyph(info.Codepoint, checked(clusterOffset + (int)info.Cluster), position.XAdvance, position.YAdvance, position.XOffset, position.YOffset, safety));
            }
        }
        finally
        {
            hb_buffer_destroy(buffer);
        }
    }

    internal static void DestroyFont(IntPtr font, IntPtr face, IntPtr blob)
    {
        if (font != IntPtr.Zero)
        {
            hb_font_destroy(font);
        }

        if (face != IntPtr.Zero)
        {
            hb_face_destroy(face);
        }

        if (blob != IntPtr.Zero)
        {
            hb_blob_destroy(blob);
        }
    }

    private static uint ToDirection(TextDirection direction) => direction switch
    {
        TextDirection.LeftToRight => DirectionLtr,
        TextDirection.RightToLeft => DirectionRtl,
        TextDirection.TopToBottom => DirectionTtb,
        TextDirection.BottomToTop => DirectionBtt,
        _ => 0
    };

    private static uint FeatureStart(OpenTypeFeature feature, int clusterOffset)
    {
        if (feature.Range is not { } range)
        {
            return 0;
        }

        return checked((uint)Math.Max(0, range.StartUtf16 - clusterOffset));
    }

    private static uint FeatureEnd(OpenTypeFeature feature, int clusterOffset, int textLength)
    {
        if (feature.Range is not { } range)
        {
            return uint.MaxValue;
        }

        return checked((uint)Math.Clamp(range.EndUtf16 - clusterOffset, 0, textLength));
    }

    private static unsafe void SetVariations(IntPtr font, ReadOnlySpan<FontVariation> variations)
    {
        if (variations.IsEmpty)
        {
            return;
        }

        var builder = new StringBuilder(variations.Length * 12);
        for (var i = 0; i < variations.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(TagToString(variations[i].Axis)).Append('=').Append(variations[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        fixed (byte* pointer = bytes)
        {
            hb_font_set_variations(font, pointer, bytes.Length);
        }
    }

    private static string TagToString(OpenTypeTag tag)
    {
        var value = tag.Value;
        return new string(new[]
        {
            (char)(value >> 24),
            (char)(value >> 16),
            (char)(value >> 8),
            (char)value
        });
    }
}
