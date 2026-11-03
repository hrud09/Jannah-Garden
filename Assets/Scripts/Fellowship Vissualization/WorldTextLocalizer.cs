using TMPro;
using UnityEngine;

/// <summary>
/// Text rendering for the world-space fellow cards.
///
/// A fellow's NAME is user data, not UI copy: an Amal account called "মাহাদি হাসান" must render in
/// Bengali even while the UI language is English. So the script is detected from the string itself and
/// everything downstream — font choice and which shaping path runs — is keyed off that, not off the
/// active locale. That is the one thing this wrapper exists for; the rendering itself is
/// <see cref="LocalizedRendering"/>'s, exactly as for every other label in the game.
///
/// The cards used to be unable to use <see cref="LocalizedRendering"/> at all: for Bengali it swaps in
/// a <see cref="ShapedTextGraphic"/>, a <c>MaskableGraphic</c> that only draws under a Canvas, and the
/// card was a canvas-less world-space object (plain <c>TextMeshPro</c> + SpriteRenderers), so that path
/// made the label vanish instead of shaping it. Bengali names therefore rendered unshaped — conjuncts
/// (ক + ্ + ষ) as separate glyphs rather than the joined ক্ষ. The card prefab now carries a world-space
/// Canvas with <c>TextMeshProUGUI</c> labels, so that restriction is gone.
/// </summary>
public static class WorldTextLocalizer
{
    /// <summary>
    /// Sets <paramref name="label"/>'s text, picking the font and the shaping path from whichever
    /// script the string is written in. Safe on a null label.
    /// </summary>
    public static void SetText(TMP_Text label, string text)
    {
        if (label == null) return;

        text ??= string.Empty;

        AppLocale script = DetectScript(text);

        // LocalizedRendering only assigns a font on its Bengali path (the shaped child's atlas); on the
        // Arabic/Latin path it leaves the label's own font alone, so set it here from the detected
        // script rather than letting a card keep whichever face it last rendered.
        TMP_FontAsset font = HarfBuzzFontRegistry.GetFontAsset(script);
        if (font != null && label.font != font) label.font = font;

        if (script == AppLocale.bn) ResolveAutoSize(label, text);

        // Passing the DETECTED script where LocalizedRendering expects a locale is the whole trick: it
        // routes Bengali to the HarfBuzz ShapedTextGraphic child (real conjuncts), Arabic to the joining
        // shaper plus TMP's RTL mode, and Latin to plain TMP — each keyed off this name's own script
        // rather than the UI language.
        LocalizedRendering.SetText(label, text, script);
    }

    /// <summary>
    /// Lays the string out in plain TMP once, so TMP's Auto Size resolves <c>fontSize</c> before the
    /// shaped renderer copies it.
    ///
    /// <see cref="LocalizedRendering"/> hands <see cref="ShapedTextGraphic"/> the label's current
    /// <c>fontSize</c>, which with Auto Size on is "whatever TMP last shrank to". Every Canvas label in
    /// the game has already laid its English out by then, so that value is real. These cards have not:
    /// a Bengali name is shaped from the very first frame the card exists, so <c>fontSize</c> would
    /// still be the authored CEILING — and these labels are authored at a world-space ceiling far above
    /// what fits (size 5 in a rect one unit tall), because they were built to lean on TMP's own vertical
    /// shrink. The shaped renderer's own auto-size only shrinks to avoid breaking a word across lines,
    /// not to fit the rect's height, so without this the name rendered several times the height of the
    /// card it belongs to.
    /// </summary>
    private static void ResolveAutoSize(TMP_Text label, string text)
    {
        if (!label.enableAutoSizing) return;

        bool wasEnabled = label.enabled;
        label.enabled = true;          // a disabled TMP_Text will not lay out
        label.text = text;             // measure the real string: a long name shrinks further than a short one
        label.ForceMeshUpdate();
        label.enabled = wasEnabled;
    }

    /// <summary>
    /// Which script a string is written in, for font selection and shaping.
    ///
    /// Bengali wins over Arabic when both are present (an Arabic honorific inside a Bengali name is the
    /// common mix): the Bengali path is <see cref="ShapedTextGraphic"/>, which splits a line into
    /// same-script runs and shapes each against its own font, so it renders that mix correctly — while
    /// the Arabic path would shape and reverse the whole string, leaving the Bengali half as tofu.
    /// Latin-only and empty strings fall through to English.
    /// </summary>
    public static AppLocale DetectScript(string text)
    {
        if (string.IsNullOrEmpty(text)) return AppLocale.en;

        if (ContainsBengali(text)) return AppLocale.bn;
        if (ArabicTextShaper.ContainsArabic(text)) return AppLocale.ar;

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
