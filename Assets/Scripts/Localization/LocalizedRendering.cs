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
            shaped.FontSize = tmpText.fontSize;
            shaped.Alignment = tmpText.alignment;
            shaped.color = tmpText.color;
            shaped.PaddingTop = shapedTopPadding;
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
        tmpText.text = rtl ? ArabicTextShaper.Shape(text) : text;
    }

    /// <summary>A ContentSizeFitter sitting on <paramref name="tmpText"/>'s own GameObject (as the
    /// toast's "Message Text (TMP)" has, to grow its background bubble around the message) only ever
    /// looks at ILayoutElements on that same GameObject — and once <paramref name="tmpText"/> is disabled
    /// for shaped-text locales, its own contribution is skipped entirely (Unity's layout system ignores
    /// disabled Behaviours), which would otherwise collapse the fitter to zero size. A LayoutElement
    /// mirroring <paramref name="shaped"/>'s measured size stands in for it while shaped text is showing.
    /// No-op for callers whose text object isn't fitted (e.g. MCQ/Dhikr/shop labels, which wrap within a
    /// fixed box instead), so this only ever changes behavior for text that already opted into fitting.</summary>
    private static void SyncContentSizeFitter(TMP_Text tmpText, ShapedTextGraphic shaped)
    {
        if (tmpText.GetComponent<ContentSizeFitter>() == null) return;

        LayoutElement sizeOverride = tmpText.GetComponent<LayoutElement>();
        if (sizeOverride == null) sizeOverride = tmpText.gameObject.AddComponent<LayoutElement>();

        sizeOverride.enabled = true;
        sizeOverride.preferredWidth = shaped.PreferredWidth;
        sizeOverride.preferredHeight = shaped.PreferredHeight;
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
        return go.AddComponent<ShapedTextGraphic>();
    }
}
