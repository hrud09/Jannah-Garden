using TMPro;
using UnityEngine;

/// <summary>
/// Drop this on any TMP_Text whose content should come from the localization table instead of being
/// hardcoded on the prefab. Set <see cref="key"/> in the Inspector to the string's key in
/// Resources/Localization/ui_en.json (and its ui_ar.json/ui_bn.json counterparts).
///
/// For Arabic, this also right-aligns the label and runs its text through <see cref="ArabicTextShaper"/>
/// so it renders as joined, right-to-left script rather than isolated LTR letterforms. The LTR baseline
/// alignment it mirrors from is whatever is already set on the TMP_Text in the editor (captured once in
/// <see cref="Awake"/>) — not a separate field to keep in sync, so every label keeps its designed
/// alignment (e.g. a centered button label) for English/Bengali and only flips L/R for Arabic/Urdu.
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

    private TMP_Text _label;
    private TextAlignmentOptions _editorAlignment;

    private void Awake()
    {
        _label = GetComponent<TMP_Text>();
        _editorAlignment = _label.alignment;
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

        _label.alignment = rtl ? LocalizedRendering.MirrorAlignment(_editorAlignment) : _editorAlignment;
        LocalizedRendering.SetText(_label, value, locale);
    }

    /// <summary>Changes which key this label shows at runtime (e.g. a shop item card being re-populated).</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Apply();
    }
}
