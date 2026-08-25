using System;
using System.Runtime.InteropServices;

/// <summary>
/// Minimal P/Invoke surface over libharfbuzz's public C API — just enough to shape a run of text
/// into (glyph id, cluster, advance, offset) tuples using the font's own OpenType tables (hb-ot-font
/// backend only; built without FreeType/ICU/Graphite, see Tools/harfbuzz-build). We don't need
/// anything beyond this: the actual glyph outlines are already rasterized in TMP's SDF atlas
/// (<see cref="TMPro.TMP_FontAsset.glyphLookupTable"/>) — HarfBuzz only has to tell us which glyph
/// indices to draw and where.
/// </summary>
internal static class HarfBuzzNative
{
#if UNITY_IOS && !UNITY_EDITOR
    private const string Lib = "__Internal";
#else
    // Android device (IL2CPP, libharfbuzz.so packaged per-ABI under Plugins/Android/libs/<abi>/)
    // and the Editor (Mono, libharfbuzz.dylib under Plugins/macOS/) both resolve via this name.
    private const string Lib = "harfbuzz";
#endif

    // hb_memory_mode_t
    internal const int HB_MEMORY_MODE_DUPLICATE = 0;

    // hb_direction_t
    internal const int HB_DIRECTION_LTR = 4;
    internal const int HB_DIRECTION_RTL = 5;

    // hb_script_t — HB_TAG('B','e','n','g') / HB_TAG('A','r','a','b')
    internal const uint HB_SCRIPT_BENGALI = 0x42656e67;
    internal const uint HB_SCRIPT_ARABIC = 0x41726162;

    [StructLayout(LayoutKind.Sequential)]
    internal struct HbGlyphInfo
    {
        public uint codepoint; // post-shaping, this is a GLYPH INDEX, not a Unicode codepoint
        public uint mask;
        public uint cluster;   // index of the source character this glyph came from
        private uint var1;
        private uint var2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HbGlyphPosition
    {
        public int xAdvance;
        public int yAdvance;
        public int xOffset;
        public int yOffset;
        private uint var;
    }

    [DllImport(Lib)] internal static extern IntPtr hb_blob_create(byte[] data, uint length, int mode, IntPtr userData, IntPtr destroyFunc);
    [DllImport(Lib)] internal static extern IntPtr hb_face_create(IntPtr blob, uint index);
    [DllImport(Lib)] internal static extern IntPtr hb_font_create(IntPtr face);
    [DllImport(Lib)] internal static extern uint hb_face_get_upem(IntPtr face);
    [DllImport(Lib)] internal static extern void hb_font_set_scale(IntPtr font, int xScale, int yScale);

    [DllImport(Lib)] internal static extern IntPtr hb_buffer_create();

    // CharSet.Unicode is required here: without it, a `char[]` parameter marshals as 1-byte ANSI by
    // default, silently truncating every non-Latin (Bengali/Arabic) code unit before it reaches
    // HarfBuzz, which expects real UTF-16 (uint16_t) code units.
    [DllImport(Lib, CharSet = CharSet.Unicode)] internal static extern void hb_buffer_add_utf16(IntPtr buffer, char[] text, int textLength, uint itemOffset, int itemLength);
    [DllImport(Lib)] internal static extern void hb_buffer_set_direction(IntPtr buffer, int direction);
    [DllImport(Lib)] internal static extern void hb_buffer_set_script(IntPtr buffer, uint script);
    [DllImport(Lib)] internal static extern void hb_buffer_set_language(IntPtr buffer, IntPtr language);
    [DllImport(Lib)] internal static extern IntPtr hb_language_from_string(string str, int len);
    [DllImport(Lib)] internal static extern void hb_buffer_guess_segment_properties(IntPtr buffer);

    [DllImport(Lib)] internal static extern void hb_shape(IntPtr font, IntPtr buffer, IntPtr features, uint numFeatures);
    [DllImport(Lib)] internal static extern IntPtr hb_buffer_get_glyph_infos(IntPtr buffer, out uint length);
    [DllImport(Lib)] internal static extern IntPtr hb_buffer_get_glyph_positions(IntPtr buffer, out uint length);

    [DllImport(Lib)] internal static extern void hb_buffer_destroy(IntPtr buffer);
    [DllImport(Lib)] internal static extern void hb_font_destroy(IntPtr font);
    [DllImport(Lib)] internal static extern void hb_face_destroy(IntPtr face);
    [DllImport(Lib)] internal static extern void hb_blob_destroy(IntPtr blob);
}
