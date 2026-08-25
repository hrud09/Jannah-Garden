using TMPro;
using UnityEngine;

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
    /// <paramref name="tmpText"/> before calling this.</summary>
    public static void SetText(TMP_Text tmpText, string text, AppLocale locale)
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
            shaped.SetText(text, locale);
            return;
        }

        Transform existingShaped = tmpText.transform.Find(ShapedChildName);
        if (existingShaped != null) existingShaped.gameObject.SetActive(false);

        tmpText.enabled = true;
        bool rtl = locale == AppLocale.ar || locale == AppLocale.ur;
        tmpText.text = rtl ? ArabicTextShaper.Shape(text) : text;
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
