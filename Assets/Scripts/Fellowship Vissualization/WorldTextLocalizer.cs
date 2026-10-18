using TMPro;
using UnityEngine;

/// <summary>
/// Text rendering for the world-space fellow cards.
///
/// <see cref="LocalizedRendering"/> cannot be used here. For Bengali it disables the TMP_Text and swaps
/// in a <see cref="ShapedTextGraphic"/> child — a <c>MaskableGraphic</c>, which only draws under a
/// Canvas. The fellow card is a canvas-less world-space object (plain <c>TextMeshPro</c> + SpriteRenderers),
/// so going through that path makes the label vanish entirely instead of shaping it.
///
/// On top of that, a fellow's NAME is user data, not UI copy: an Amal account called "মাহাদি হাসান"
/// must render in Bengali even while the UI language is English. So the font is picked from the script
/// the string actually contains, not from the active locale.
///
/// What this gives up compared to the Canvas path: HarfBuzz shaping, i.e. Bengali conjuncts
/// (ক + ্ + ষ) render as separate glyphs rather than the joined ক্ষ. Arabic is unaffected — the
/// <see cref="ArabicTextShaper"/> path works on plain TMP and is applied below. Removing the Bengali
/// caveat means giving the card a world-space Canvas; see fellowship_flutter_integration_guide.md.
/// </summary>
public static class WorldTextLocalizer
{
    /// <summary>
    /// Sets <paramref name="label"/>'s text, swapping in the font for whichever script the string is
    /// written in and applying Arabic joining/RTL where needed. Safe on a null label.
    /// </summary>
    public static void SetText(TMP_Text label, string text)
    {
        if (label == null) return;

        text ??= string.Empty;

        AppLocale script = DetectScript(text);

        TMP_FontAsset font = HarfBuzzFontRegistry.GetFontAsset(script);
        if (font != null && label.font != font) label.font = font;

        bool arabic = script == AppLocale.ar;

        // Same pairing LocalizedRendering uses: the shaper emits joined letterforms in logical order and
        // leaves the right-to-left layout to TMP, and TMP's kerning table must be off because it looks
        // pairs up in string order but applies them to visually reversed glyphs.
        label.isRightToLeftText = arabic;
        label.enableKerning = !arabic;
        label.text = arabic ? ArabicTextShaper.Shape(text) : text;
    }

    /// <summary>
    /// Which script a string is written in, for font selection. Arabic wins over Bengali when both are
    /// present (an Arabic honorific inside a Bengali name is the common mix, and the Arabic font covers
    /// Latin too); Latin-only and empty strings fall through to English.
    /// </summary>
    public static AppLocale DetectScript(string text)
    {
        if (string.IsNullOrEmpty(text)) return AppLocale.en;

        if (ArabicTextShaper.ContainsArabic(text)) return AppLocale.ar;
        if (ContainsBengali(text)) return AppLocale.bn;

        return AppLocale.en;
    }

    /// <summary>U+0980–U+09FF is the Bengali block, which also covers Assamese.</summary>
    private static bool ContainsBengali(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] >= '\u0980' && text[i] <= '\u09FF') return true;
        }

        return false;
    }
}
