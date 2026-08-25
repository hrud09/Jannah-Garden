using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One shaped glyph: which glyph to draw, which source character it came from, and where
/// to place it. Advance/offset are in font design units (see <see cref="UnitsPerEm"/>) — the caller
/// scales them to whatever point size it's rendering at.</summary>
public struct ShapedGlyph
{
    public uint GlyphId;
    public uint Cluster;
    public int XAdvance;
    public int YAdvance;
    public int XOffset;
    public int YOffset;
}

/// <summary>
/// Shapes a single same-script, same-direction run of text via HarfBuzz's hb-ot-font backend,
/// against the actual source TTF bytes backing a locale's TMP_FontAsset (HarfBuzz needs the raw
/// OpenType tables — it can't shape from a TMP_FontAsset, which only stores rasterized glyph data).
///
/// One <see cref="hb_font_t"/> is created per font and kept for the process's lifetime — creating it
/// re-parses the font's cmap/GSUB/GPOS tables, which is wasted work to repeat per call given this
/// game only ever shapes against two faces (Bengali, Arabic).
/// </summary>
public static class HarfBuzzShaper
{
    private static readonly Dictionary<AppLocale, IntPtr> _fonts = new Dictionary<AppLocale, IntPtr>();
    private static readonly Dictionary<AppLocale, uint> _unitsPerEm = new Dictionary<AppLocale, uint>();

    private static readonly Dictionary<AppLocale, string> ResourceNameByLocale = new Dictionary<AppLocale, string>
    {
        { AppLocale.bn, "Fonts/NotoSansBengali-Regular" },
        { AppLocale.ar, "Fonts/NotoSansArabic-Regular" },
        { AppLocale.ur, "Fonts/NotoSansArabic-Regular" }, // Urdu content uses the Arabic face today
    };

    private static readonly Dictionary<AppLocale, uint> ScriptByLocale = new Dictionary<AppLocale, uint>
    {
        { AppLocale.bn, HarfBuzzNative.HB_SCRIPT_BENGALI },
        { AppLocale.ar, HarfBuzzNative.HB_SCRIPT_ARABIC },
        { AppLocale.ur, HarfBuzzNative.HB_SCRIPT_ARABIC },
    };

    private static readonly Dictionary<AppLocale, int> DirectionByLocale = new Dictionary<AppLocale, int>
    {
        { AppLocale.bn, HarfBuzzNative.HB_DIRECTION_LTR },
        { AppLocale.ar, HarfBuzzNative.HB_DIRECTION_RTL },
        { AppLocale.ur, HarfBuzzNative.HB_DIRECTION_RTL },
    };

    /// <summary>Design-space units per em for the font backing this locale — divide advance/offset
    /// values by this to get ems, then multiply by the desired point size.</summary>
    public static uint UnitsPerEm(AppLocale locale) => _unitsPerEm.TryGetValue(locale, out uint upem) ? upem : 1000;

    private static IntPtr GetOrCreateFont(AppLocale locale)
    {
        if (_fonts.TryGetValue(locale, out IntPtr existing)) return existing;

        if (!ResourceNameByLocale.TryGetValue(locale, out string resourceName))
            throw new ArgumentException($"No HarfBuzz font mapped for locale '{locale}'.");

        TextAsset fontAsset = Resources.Load<TextAsset>(resourceName);
        if (fontAsset == null)
            throw new InvalidOperationException($"Missing font byte asset at Resources/{resourceName}.bytes");

        byte[] fontBytes = fontAsset.bytes;
        IntPtr blob = HarfBuzzNative.hb_blob_create(fontBytes, (uint)fontBytes.Length, HarfBuzzNative.HB_MEMORY_MODE_DUPLICATE, IntPtr.Zero, IntPtr.Zero);
        IntPtr face = HarfBuzzNative.hb_face_create(blob, 0);
        IntPtr font = HarfBuzzNative.hb_font_create(face);
        uint upem = HarfBuzzNative.hb_face_get_upem(face);
        HarfBuzzNative.hb_font_set_scale(font, (int)upem, (int)upem);

        // The blob/face are only needed to construct the font; hb_font_t keeps its own references.
        HarfBuzzNative.hb_face_destroy(face);
        HarfBuzzNative.hb_blob_destroy(blob);

        _fonts[locale] = font;
        _unitsPerEm[locale] = upem;
        return font;
    }

    /// <summary>Shapes one run of same-script text. Callers are responsible for splitting mixed
    /// bidi/script content into runs first (see <see cref="BidiRunSplitter"/>) — HarfBuzz shapes
    /// exactly the direction/script it's told, it doesn't infer paragraph-level bidi itself here.</summary>
    public static List<ShapedGlyph> Shape(string text, AppLocale locale)
    {
        var result = new List<ShapedGlyph>(text.Length);
        if (string.IsNullOrEmpty(text)) return result;

        IntPtr font = GetOrCreateFont(locale);
        IntPtr buffer = HarfBuzzNative.hb_buffer_create();
        try
        {
            char[] chars = text.ToCharArray();
            HarfBuzzNative.hb_buffer_add_utf16(buffer, chars, chars.Length, 0, -1);
            HarfBuzzNative.hb_buffer_set_direction(buffer, DirectionByLocale[locale]);
            HarfBuzzNative.hb_buffer_set_script(buffer, ScriptByLocale[locale]);
            HarfBuzzNative.hb_shape(font, buffer, IntPtr.Zero, 0);

            IntPtr infosPtr = HarfBuzzNative.hb_buffer_get_glyph_infos(buffer, out uint count);
            IntPtr posPtr = HarfBuzzNative.hb_buffer_get_glyph_positions(buffer, out _);

            int infoSize = System.Runtime.InteropServices.Marshal.SizeOf<HarfBuzzNative.HbGlyphInfo>();
            int posSize = System.Runtime.InteropServices.Marshal.SizeOf<HarfBuzzNative.HbGlyphPosition>();

            for (int i = 0; i < count; i++)
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<HarfBuzzNative.HbGlyphInfo>(IntPtr.Add(infosPtr, i * infoSize));
                var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<HarfBuzzNative.HbGlyphPosition>(IntPtr.Add(posPtr, i * posSize));

                result.Add(new ShapedGlyph
                {
                    GlyphId = info.codepoint,
                    Cluster = info.cluster,
                    XAdvance = pos.xAdvance,
                    YAdvance = pos.yAdvance,
                    XOffset = pos.xOffset,
                    YOffset = pos.yOffset,
                });
            }
        }
        finally
        {
            HarfBuzzNative.hb_buffer_destroy(buffer);
        }

        return result;
    }
}
