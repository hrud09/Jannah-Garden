using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets you switch the active language from the Editor while in Play Mode, without needing the Flutter
/// host to send SET_LOCALE. LocalizationManager only exists once the game is running, so these are
/// disabled outside Play Mode.
/// </summary>
public static class LocalizationPreviewMenu
{
    [MenuItem("Tools/Localization/Preview Locale/English", true)]
    [MenuItem("Tools/Localization/Preview Locale/Arabic", true)]
    [MenuItem("Tools/Localization/Preview Locale/Bengali", true)]
    private static bool ValidatePreview() => Application.isPlaying && LocalizationManager.Instance != null;

    [MenuItem("Tools/Localization/Preview Locale/English")]
    private static void PreviewEnglish() => SetLocale("en");

    [MenuItem("Tools/Localization/Preview Locale/Arabic")]
    private static void PreviewArabic() => SetLocale("ar");

    [MenuItem("Tools/Localization/Preview Locale/Bengali")]
    private static void PreviewBengali() => SetLocale("bn");

    private static void SetLocale(string code)
    {
        LocalizationManager.Instance.SetLocale(code);
        Debug.Log($"[LocalizationPreviewMenu] Previewing locale '{code}'.");
    }
}
