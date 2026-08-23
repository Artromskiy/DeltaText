using System.Runtime.InteropServices;

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
    private static extern void hb_shape(IntPtr font, IntPtr buffer, IntPtr features, uint featureCount);

    internal static IntPtr CreateFont(ReadOnlySpan<byte> data, uint faceIndex, out IntPtr blob, out IntPtr face, out int unitsPerEm)
    {
        NativeLibraryResolver.EnsureInitialized();
        face = IntPtr.Zero;
        unitsPerEm = 0;
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
            hb_blob_destroy(blob);
            throw new InvalidOperationException("HarfBuzz could not create a font face.");
        }

        unitsPerEm = checked((int)hb_face_get_upem(face));
        if (unitsPerEm <= 0)
        {
            hb_face_destroy(face);
            face = IntPtr.Zero;
            hb_blob_destroy(blob);
            throw new InvalidOperationException("The font has no usable units-per-em value.");
        }

        var font = hb_font_create(face);
        if (font == IntPtr.Zero)
        {
            hb_face_destroy(face);
            face = IntPtr.Zero;
            hb_blob_destroy(blob);
            throw new InvalidOperationException("HarfBuzz could not create a font object.");
        }

        hb_font_set_scale(font, unitsPerEm, unitsPerEm);
        hb_ot_font_set_funcs(font);
        return font;
    }

    internal static GlyphMetrics GetGlyphMetrics(IntPtr font, uint glyph, int unitsPerEm)
    {
        var advance = hb_font_get_glyph_h_advance(font, glyph);
        var extents = default(GlyphExtents);
        _ = hb_font_get_glyph_extents(font, glyph, out extents);
        return new GlyphMetrics(glyph, advance, 0, extents.XBearing, extents.YBearing, extents.Width, extents.Height, unitsPerEm);
    }

    internal static uint GetGlyph(IntPtr font, uint codepoint)
        => hb_font_get_nominal_glyph(font, codepoint, out var glyph) != 0 ? glyph : 0;

    internal static void Shape(
        IntPtr font,
        string text,
        TextDirection direction,
        ReadOnlySpan<TextFeature> requestedFeatures,
        List<ShapedGlyph> output)
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
                    Tag = MakeTag(requestedFeatures[i].Tag),
                    Value = requestedFeatures[i].Enabled ? 1u : 0u,
                    Start = 0,
                    End = uint.MaxValue
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
                output.Add(new ShapedGlyph(info.Codepoint, checked((int)info.Cluster), position.XAdvance, position.YAdvance, position.XOffset, position.YOffset));
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

    private static uint MakeTag(string tag)
        => ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
}
