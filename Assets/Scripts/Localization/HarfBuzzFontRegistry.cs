using TMPro;
using UnityEngine;

/// <summary>
/// Holds the TMP_FontAsset references <see cref="ShapedTextGraphic"/> needs at runtime. Exists purely
/// so those references survive into a build: the font assets themselves live under Assets/Font/... (not
/// a Resources folder), and AssetDatabase (used to load them during this session's Editor tooling) is
/// Editor-only. A ScriptableObject placed under Resources/ can still hold serialized references to
/// assets that aren't themselves in a Resources folder — Unity includes them in the build as
/// dependencies — so this is the one thing that needs to live at Resources/HarfBuzzFontRegistry.asset.
/// </summary>
public class HarfBuzzFontRegistry : ScriptableObject
{
    private const string ResourcePath = "HarfBuzzFontRegistry";

    [SerializeField] private TMP_FontAsset bengaliFontAsset;
    [SerializeField] private TMP_FontAsset arabicFontAsset;

    private static HarfBuzzFontRegistry _instance;

    private static HarfBuzzFontRegistry Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<HarfBuzzFontRegistry>(ResourcePath);
            if (_instance == null) Debug.LogError($"[HarfBuzzFontRegistry] Missing Resources/{ResourcePath}.asset");
            return _instance;
        }
    }

    public static TMP_FontAsset GetFontAsset(AppLocale locale)
    {
        HarfBuzzFontRegistry instance = Instance;
        if (instance == null) return null;

        switch (locale)
        {
            case AppLocale.bn: return instance.bengaliFontAsset;
            case AppLocale.ar:
            case AppLocale.ur: return instance.arabicFontAsset;
            default: return null;
        }
    }
}
