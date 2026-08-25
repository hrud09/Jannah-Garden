using TMPro;
using UnityEngine;

/// <summary>
/// Drop this on any TMP_Text whose content should come from the localization table instead of being
/// hardcoded on the prefab. Set <see cref="key"/> in the Inspector to the string's key in
/// Resources/Localization/ui_en.json (and its ui_ar.json/ui_bn.json counterparts).
///
/// For Arabic, this also right-aligns the label and runs its text through <see cref="ArabicTextShaper"/>
/// so it renders as joined, right-to-left script rather than isolated LTR letterforms.
///
/// Bengali is handled differently: instead of the hand-rolled <see cref="BengaliTextShaper"/> (which only
/// reorders pre-base vowel signs and can't form conjuncts), this swaps in a lazily-created
/// <see cref="ShapedTextGraphic"/> child that renders via real HarfBuzz shaping — see
/// Assets/Scripts/Localization/HarfBuzzShaper.cs. Arabic/English stay on the TMP_Text path for now;
/// only Bengali had the conjunct-rendering problem HarfBuzz was brought in to fix.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("Key looked up in Resources/Localization/ui_{locale}.json.")]
    public string key;

    [Tooltip("Alignment to use for left-to-right locales (English, Bengali). Arabic uses the mirrored alignment automatically.")]
    public TextAlignmentOptions leftToRightAlignment = TextAlignmentOptions.TopLeft;

    private TMP_Text _label;

    private void Awake()
    {
        _label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        LocalizationManager.OnLocaleChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLocaleChanged -= Apply;
    }

    /// <summary>Re-reads the current key from the active locale. Also called by OnLocaleChanged.</summary>
    public void Apply()
    {
        if (_label == null || LocalizationManager.Instance == null || string.IsNullOrEmpty(key)) return;

        string value = LocalizationManager.Instance.Get(key);
        AppLocale locale = LocalizationManager.Instance.CurrentLocale;
        bool rtl = LocalizationManager.Instance.IsRightToLeft;

        _label.alignment = rtl ? MirrorAlignment(leftToRightAlignment) : leftToRightAlignment;
        LocalizedRendering.SetText(_label, value, locale);
    }

    /// <summary>Changes which key this label shows at runtime (e.g. a shop item card being re-populated).</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Apply();
    }

    /// <summary>Swaps the Left/Right half of the alignment (TMP encodes it in the low byte) while leaving
    /// the vertical component (high byte — Top/Middle/Bottom/Baseline/Geometry/Capline) untouched. Handles
    /// every Left/Right pairing this way — TopLeft/TopRight, BaselineLeft/BaselineRight, MidlineLeft/
    /// MidlineRight, CaplineLeft/CaplineRight, etc. — instead of only the three enumerated by hand
    /// previously. Center/Justified/Flush/Geometry read the same in both directions, so they pass through.</summary>
    private static TextAlignmentOptions MirrorAlignment(TextAlignmentOptions alignment)
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
}
