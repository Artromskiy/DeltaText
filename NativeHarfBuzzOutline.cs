using System.Runtime.InteropServices;

namespace Delta.Text;

internal static unsafe partial class NativeHarfBuzzOutline
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MoveToFunc(IntPtr drawFunctions, IntPtr drawData, IntPtr state, float toX, float toY, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LineToFunc(IntPtr drawFunctions, IntPtr drawData, IntPtr state, float toX, float toY, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void QuadraticToFunc(IntPtr drawFunctions, IntPtr drawData, IntPtr state, float controlX, float controlY, float toX, float toY, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CubicToFunc(IntPtr drawFunctions, IntPtr drawData, IntPtr state, float control1X, float control1Y, float control2X, float control2Y, float toX, float toY, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClosePathFunc(IntPtr drawFunctions, IntPtr drawData, IntPtr state, IntPtr userData);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hb_draw_funcs_create();

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_draw_funcs_destroy(IntPtr dfuncs);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_draw_funcs_set_move_to_func(IntPtr dfuncs, MoveToFunc func, IntPtr userData, IntPtr destroy);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_draw_funcs_set_line_to_func(IntPtr dfuncs, LineToFunc func, IntPtr userData, IntPtr destroy);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_draw_funcs_set_quadratic_to_func(IntPtr dfuncs, QuadraticToFunc func, IntPtr userData, IntPtr destroy);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_draw_funcs_set_cubic_to_func(IntPtr dfuncs, CubicToFunc func, IntPtr userData, IntPtr destroy);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void hb_draw_funcs_set_close_path_func(IntPtr dfuncs, ClosePathFunc func, IntPtr userData, IntPtr destroy);

    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_draw_glyph(IntPtr font, uint glyph, IntPtr dfuncs, IntPtr drawData);

    internal static bool TryRead(IntPtr font, uint glyph, GlyphContours output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var callbacks = new CallbackState(output);
        var funcs = hb_draw_funcs_create();
        if (funcs == IntPtr.Zero) throw new InvalidOperationException("HarfBuzz could not create draw functions.");
        try
        {
            hb_draw_funcs_set_move_to_func(funcs, Move, callbacks.Handle, IntPtr.Zero);
            hb_draw_funcs_set_line_to_func(funcs, Line, callbacks.Handle, IntPtr.Zero);
            hb_draw_funcs_set_quadratic_to_func(funcs, Quadratic, callbacks.Handle, IntPtr.Zero);
            hb_draw_funcs_set_cubic_to_func(funcs, Cubic, callbacks.Handle, IntPtr.Zero);
            hb_draw_funcs_set_close_path_func(funcs, Close, callbacks.Handle, IntPtr.Zero);
            hb_font_draw_glyph(font, glyph, funcs, IntPtr.Zero);
            return output.Contours.Count != 0;
        }
        finally { hb_draw_funcs_destroy(funcs); }
    }

    private sealed class CallbackState : IDisposable
    {
        internal readonly GlyphContours Contours;
        internal readonly GCHandle Pin;
        internal IntPtr Handle => GCHandle.ToIntPtr(Pin);
        internal CallbackState(GlyphContours contours) { Contours = contours; Pin = GCHandle.Alloc(this); }
        public void Dispose() => Pin.Free();
    }

    private static CallbackState State(IntPtr p)
    {
        var target = GCHandle.FromIntPtr(p).Target;
        return target as CallbackState ?? throw new InvalidOperationException("HarfBuzz callback state was not available.");
    }
    private static readonly MoveToFunc Move = static (_, _, _, x, y, data) => State(data).Contours.BeginContour(x, y);
    private static readonly LineToFunc Line = static (_, _, _, x, y, data) => State(data).Contours.LineTo(x, y);
    private static readonly QuadraticToFunc Quadratic = static (_, _, _, cx, cy, x, y, data) => State(data).Contours.QuadraticTo(cx, cy, x, y);
    private static readonly CubicToFunc Cubic = static (_, _, _, c1x, c1y, c2x, c2y, x, y, data) => State(data).Contours.CubicTo(c1x, c1y, c2x, c2y, x, y);
    private static readonly ClosePathFunc Close = static (_, _, _, data) => State(data).Contours.Close();
}
