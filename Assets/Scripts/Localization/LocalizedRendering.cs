using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared "TMP vs HarfBuzz-shaped" toggle logic, factored out so <see cref="LocalizedText"/>,
/// <see cref="MCQManager"/>, and <see cref="DhikrManager"/> don't each duplicate it. For Bengali, swaps
/// in a lazily-created <see cref="ShapedTextGraphic"/> child (see that class for why a separate
/// component is needed instead of extending TMP_Text); for Arabic/Urdu, keeps the existing
/// <see cref="ArabicTextShaper"/> path; English needs no shaping at all.
/// </summary>
public static class LocalizedRendering
{
    private const string ShapedChildName = "Shaped Text (HarfBuzz)";

    /// <summary>Convenience overload that reads the active locale from <see cref="LocalizationManager"/>
    /// (falling back to English if it isn't ready yet) instead of making every caller check for it.</summary>
    public static void SetText(TMP_Text tmpText, string text)
    {
        AppLocale locale = LocalizationManager.Instance != null ? LocalizationManager.Instance.CurrentLocale : AppLocale.en;
        SetText(tmpText, text, locale);
    }

    /// <summary>Sets <paramref name="tmpText"/>'s displayed content for <paramref name="locale"/>,
    /// switching to a shaped renderer child for Bengali and back to plain TMP otherwise. Uses
    /// <paramref name="tmpText"/>'s current <c>alignment</c> for the shaped label too — callers that
    /// need RTL alignment mirroring (as <see cref="LocalizedText"/> does) should set that on
    /// <paramref name="tmpText"/> before calling this. <paramref name="shapedTopPadding"/> is applied to
    /// the shaped child only (plain TMP is unaffected) — callers whose shaped label needs breathing room
    /// above the glyphs (e.g. <see cref="DhikrManager"/>'s dhikr/count labels) pass it explicitly.</summary>
    public static void SetText(TMP_Text tmpText, string text, AppLocale locale, float shapedTopPadding = 0f)
    {
        if (locale == AppLocale.bn)
        {
            tmpText.enabled = false;
            ShapedTextGraphic shaped = EnsureShapedChild(tmpText.transform);
            shaped.gameObject.SetActive(true);
            shaped.FontAsset = HarfBuzzFontRegistry.GetFontAsset(locale);

            // Match the size the label renders at in English rather than running an independent search
            // up to TMP's ceiling. tmpText.fontSize is exactly that size: with Auto Size on it holds what
            // TMP resolved for the string it last laid out (the English one, since the shaped path only
            // takes over afterwards), and before TMP has ever laid out — Bengali active from the first
            // frame — it is still the size authored in the prefab, which is the English design size too.
            // Using fontSizeMax instead let Bengali render far larger than English wherever a label
            // authored a generous ceiling (the world-space treasure-box name label tops out at 1.0 but
            // renders English around 0.3). Auto Size stays on as a shrink-only safety net, so a Bengali
            // string that is genuinely longer than its rect still fits — it just never grows past English.
            shaped.SetAutoSize(tmpText.enableAutoSizing, tmpText.fontSizeMin);
            shaped.FontSize = tmpText.fontSize;
            shaped.Alignment = tmpText.alignment;
            shaped.color = tmpText.color;
            // TMP's own Margins (left, top, right, bottom) inset its text the same way this padding does — without
            // mirroring them, a button label designed with breathing room renders edge-to-edge in Bengali.
            Vector4 margin = tmpText.margin;
            shaped.SetPadding(margin.x, margin.z, margin.y + shapedTopPadding, margin.w);
            shaped.SetText(text, locale);
            SyncContentSizeFitter(tmpText, shaped);
            return;
        }

        Transform existingShaped = tmpText.transform.Find(ShapedChildName);
        if (existingShaped != null) existingShaped.gameObject.SetActive(false);

        LayoutElement sizeOverride = tmpText.GetComponent<LayoutElement>();
        if (sizeOverride != null) sizeOverride.enabled = false;

        tmpText.enabled = true;
        bool rtl = locale == AppLocale.ar || locale == AppLocale.ur;

        // The shaper emits joined letterforms in logical order and relies on TMP's RTL mode for the
        // right-to-left layout (that split is what keeps word-wrapped lines in the correct order — see
        // ArabicTextShaper). RTL mode is gated on the text actually containing Arabic script, because an
        // untranslated English fallback string under an RTL locale must not be rendered reversed.
        bool rtlRender = rtl && ArabicTextShaper.ContainsArabic(text);
        tmpText.isRightToLeftText = rtlRender;

        // TMP looks kerning pairs up in string order but applies the adjustment to the visually reversed
        // glyphs in RTL mode, which shoves some presentation-form pairs (e.g. reh + alef) into each other.
        tmpText.enableKerning = !rtlRender;

        tmpText.text = rtl ? ArabicTextShaper.Shape(text) : text;
    }

    /// <summary>
    /// Forces plain, unshaped TMP rendering regardless of the active locale — for content that's
    /// inherently script-neutral (pure digits, a currency symbol, an icon glyph like the coin symbol) and
    /// would only risk a missing-glyph tofu box if routed through Bengali's shaped renderer, which draws
    /// from a Bengali-only font atlas.
    ///
    /// Still deactivates any shaped child left over from a previous <see cref="SetText(TMP_Text,string)"/>
    /// call on this same label — needed because <see cref="ShopItemUI"/>/<see cref="InventoryItemUI"/>
    /// reuse (pool) their card labels across items, so a label previously showing shaped Bengali text
    /// (e.g. "Free") must not stay stuck in shaped mode when a later item's price is plain digits.
    /// </summary>
    public static void SetPlainText(TMP_Text tmpText, string text)
    {
        Transform existingShaped = tmpText.transform.Find(ShapedChildName);
        if (existingShaped != null) existingShaped.gameObject.SetActive(false);

        LayoutElement sizeOverride = tmpText.GetComponent<LayoutElement>();
        if (sizeOverride != null) sizeOverride.enabled = false;

        tmpText.enabled = true;
        tmpText.text = text;
    }

    /// <summary>Swaps the Left/Right half of a TMP alignment (encoded in the low byte) while leaving the
    /// vertical component (high byte) untouched — Center/Justified/Flush/Geometry read the same in both
    /// directions and pass through. Used by every label that mirrors its layout for RTL locales.</summary>
    public static TextAlignmentOptions MirrorAlignment(TextAlignmentOptions alignment)
    {
        int horizontal = (int)alignment & 0xFF;
        int vertical = (int)alignment & 0xFF00;

        int mirroredHorizontal = horizontal switch
        {
            (int)HorizontalAlignmentOptions.Left => (int)HorizontalAlignmentOptions.Right,
            (int)HorizontalAlignmentOptions.Right => (int)HorizontalAlignmentOptions.Left,
            _ => horizontal,
        };

        return (TextAlignmentOptions)(mirroredHorizontal | vertical);
    }

    /// <summary>A ContentSizeFitter sitting on <paramref name="tmpText"/>'s own GameObject (as the
    /// toast's "Message Text (TMP)" has, to grow its background bubble around the message) only ever
    /// looks at ILayoutElements on that same GameObject — and once <paramref name="tmpText"/> is disabled
    /// for shaped-text locales, its own contribution is skipped entirely (Unity's layout system ignores
    /// disabled Behaviours), which would otherwise collapse the fitter to zero size. A parent
    /// Horizontal/VerticalLayoutGroup that controls its children's size reads the same ILayoutElements and
    /// collapses the label the same way. A LayoutElement mirroring <paramref name="shaped"/>'s measured
    /// size stands in for it while shaped text is showing — kept current from
    /// <see cref="ShapedTextGraphic.LayoutRebuilt"/>, since the shaped label re-measures on every resize.
    /// No-op for text that is neither fitted nor layout-driven (e.g. labels inside fixed-size buttons),
    /// so this only ever changes behavior for text that already opted into automatic sizing.</summary>
    private static void SyncContentSizeFitter(TMP_Text tmpText, ShapedTextGraphic shaped)
    {
        if (tmpText == null || shaped == null || tmpText.enabled || !shaped.gameObject.activeSelf) return;
        if (tmpText.GetComponent<ContentSizeFitter>() == null && !IsSizedByParentLayout(tmpText.transform)) return;

        LayoutElement sizeOverride = tmpText.GetComponent<LayoutElement>();
        if (sizeOverride == null) sizeOverride = tmpText.gameObject.AddComponent<LayoutElement>();

        sizeOverride.enabled = true;
        sizeOverride.preferredWidth = shaped.PreferredWidth;
        sizeOverride.preferredHeight = shaped.PreferredHeight;
    }

    private static bool IsSizedByParentLayout(Transform t)
    {
        var group = t.parent != null ? t.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        return group != null && group.enabled && (group.childControlWidth || group.childControlHeight);
    }

    /// <summary>What <paramref name="tmpText"/> currently measures on screen — the shaped child's
    /// natural size while Bengali is showing (TMP's own GetPreferredValues would measure the hidden,
    /// unrendered string against a font that has no Bengali glyphs), TMP's otherwise.</summary>
    public static Vector2 GetPreferredValues(TMP_Text tmpText)
    {
        if (!tmpText.enabled)
        {
            Transform shapedTransform = tmpText.transform.Find(ShapedChildName);
            if (shapedTransform != null && shapedTransform.gameObject.activeSelf)
            {
                ShapedTextGraphic shaped = shapedTransform.GetComponent<ShapedTextGraphic>();
                if (shaped != null) return new Vector2(shaped.PreferredWidth, shaped.PreferredHeight);
            }
        }
        return tmpText.GetPreferredValues();
    }

    private static ShapedTextGraphic EnsureShapedChild(Transform parent)
    {
        Transform existing = parent.Find(ShapedChildName);
        if (existing != null) return existing.GetComponent<ShapedTextGraphic>();

        var go = new GameObject(ShapedChildName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        ShapedTextGraphic shaped = go.AddComponent<ShapedTextGraphic>();

        TMP_Text owner = parent.GetComponent<TMP_Text>();
        shaped.LayoutRebuilt += () => SyncContentSizeFitter(owner, shaped);
        return shaped;
    }
}
