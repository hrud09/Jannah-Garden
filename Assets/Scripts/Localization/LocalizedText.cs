using TMPro;
using UnityEngine;

/// <summary>
/// Drop this on any TMP_Text whose content should come from the localization table instead of being
/// hardcoded on the prefab. Set <see cref="key"/> in the Inspector to the string's key in
/// Resources/Localization/ui_en.json (and its ui_ar.json/ui_bn.json counterparts).
///
/// For Arabic, this also right-aligns the label and runs its text through <see cref="ArabicTextShaper"/>
/// so it renders as joined, right-to-left script rather than isolated LTR letterforms.
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
        bool rtl = LocalizationManager.Instance.IsRightToLeft;

        _label.text = rtl ? ArabicTextShaper.Shape(value) : value;
        _label.alignment = rtl ? MirrorAlignment(leftToRightAlignment) : leftToRightAlignment;
    }

    /// <summary>Changes which key this label shows at runtime (e.g. a shop item card being re-populated).</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Apply();
    }

    private static TextAlignmentOptions MirrorAlignment(TextAlignmentOptions alignment)
    {
        switch (alignment)
        {
            case TextAlignmentOptions.TopLeft: return TextAlignmentOptions.TopRight;
            case TextAlignmentOptions.Left: return TextAlignmentOptions.Right;
            case TextAlignmentOptions.BottomLeft: return TextAlignmentOptions.BottomRight;
            case TextAlignmentOptions.TopRight: return TextAlignmentOptions.TopLeft;
            case TextAlignmentOptions.Right: return TextAlignmentOptions.Left;
            case TextAlignmentOptions.BottomRight: return TextAlignmentOptions.BottomLeft;
            default: return alignment; // Center/Justified variants don't need mirroring.
        }
    }
}
