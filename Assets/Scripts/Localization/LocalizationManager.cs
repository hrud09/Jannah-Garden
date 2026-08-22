using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The active languages the game ships with. Values must match the lowercase locale codes Flutter
/// sends in SET_LOCALE (see FlutterCommands.SetLocale) and the "ui_{locale}.json" file suffixes under
/// Resources/Localization.
/// </summary>
public enum AppLocale
{
    en,
    ar,
    bn,
    ur
}

/// <summary>
/// JsonUtility cannot deserialize a bare Dictionary, so each locale's UI string table is stored as an
/// array of key/value entries instead. See Resources/Localization/ui_en.json for the shape, and
/// Editor/LocalizationCsvImporter.cs for the tool that generates these files from a translator-facing CSV.
/// </summary>
[Serializable]
internal class LocalizationEntry
{
    public string key;
    public string value;
}

[Serializable]
internal class LocalizationTable
{
    public LocalizationEntry[] entries;
}

/// <summary>
/// Single source of truth for the active language and for every static/templated UI string in the game.
/// Bootstraps itself before the first scene loads (same pattern as FlutterBridge) so it is always ready
/// by the time any UI queries it.
///
/// Flutter is expected to drive the active locale via FlutterCommands.SetLocale — see
/// FlutterBridge.HandleCommand. The last locale is also cached to PlayerPrefs so the game has a sane
/// language before that first message arrives (e.g. on a cold start) and when testing outside Flutter.
/// </summary>
public class LocalizationManager : MonoBehaviour
{
    private const string LocalePrefsKey = "AppLocale";
    private const AppLocale FallbackLocale = AppLocale.en;

    public static LocalizationManager Instance { get; private set; }

    /// <summary>Fires after the active locale (and its string table) has finished changing.</summary>
    public static event Action OnLocaleChanged;

    public AppLocale CurrentLocale { get; private set; } = FallbackLocale;

    /// <summary>True for Arabic and Urdu (both Perso-Arabic script, right-to-left). Bengali is not Latin
    /// script either, but it is still left-to-right.</summary>
    public bool IsRightToLeft => CurrentLocale == AppLocale.ar || CurrentLocale == AppLocale.ur;

    private Dictionary<string, string> _activeTable = new Dictionary<string, string>();
    private Dictionary<string, string> _fallbackTable = new Dictionary<string, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        GameObject host = new GameObject("Localization Manager");
        host.AddComponent<LocalizationManager>(); // Awake wires up Instance + DontDestroyOnLoad
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        _fallbackTable = LoadTable(FallbackLocale);

        string savedLocale = PlayerPrefs.GetString(LocalePrefsKey, FallbackLocale.ToString());
        SetLocale(savedLocale, notify: false);
    }

    /// <summary>
    /// Switches the active locale and reloads its string table. Safe to call before any listener exists —
    /// pass notify:false (used on startup) to skip the event when nothing has subscribed yet regardless.
    /// </summary>
    public void SetLocale(string localeCode, bool notify = true)
    {
        AppLocale locale = ParseLocale(localeCode);

        CurrentLocale = locale;
        PlayerPrefs.SetString(LocalePrefsKey, locale.ToString());
        PlayerPrefs.Save();

        _activeTable = locale == FallbackLocale ? _fallbackTable : LoadTable(locale);

        Debug.Log($"[LocalizationManager] Locale set to '{locale}' (requested '{localeCode}').");

        if (notify) OnLocaleChanged?.Invoke();
    }

    /// <summary>
    /// Accepts both short codes ("ar") and the longer forms a host app might send ("ar-SA", "ar_AE").
    /// Anything unrecognised falls back to English rather than leaving the game in a broken state.
    /// </summary>
    private static AppLocale ParseLocale(string code)
    {
        if (!string.IsNullOrEmpty(code))
        {
            string normalized = code.Trim().ToLowerInvariant();
            int separator = normalized.IndexOfAny(new[] { '-', '_' });
            if (separator > 0) normalized = normalized.Substring(0, separator);

            if (Enum.TryParse(normalized, ignoreCase: true, out AppLocale locale)) return locale;
        }

        Debug.LogWarning($"[LocalizationManager] Unrecognised locale '{code}' — using {FallbackLocale}.");
        return FallbackLocale;
    }

    private Dictionary<string, string> LoadTable(AppLocale locale)
    {
        var table = new Dictionary<string, string>();
        TextAsset asset = Resources.Load<TextAsset>($"Localization/ui_{locale}");

        if (asset == null)
        {
            if (locale != FallbackLocale)
                Debug.LogWarning($"[LocalizationManager] No ui_{locale}.json in Resources/Localization — every key for this locale falls back to English.");
            return table;
        }

        try
        {
            LocalizationTable parsed = JsonUtility.FromJson<LocalizationTable>(asset.text);
            if (parsed?.entries != null)
            {
                foreach (LocalizationEntry entry in parsed.entries)
                {
                    if (!string.IsNullOrEmpty(entry.key)) table[entry.key] = entry.value ?? string.Empty;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LocalizationManager] Failed to parse ui_{locale}.json: {e.Message}");
        }

        return table;
    }

    /// <summary>
    /// Looks up a UI string by key: active locale first, then English, then the key itself (so a missing
    /// translation is visibly wrong in-game and searchable, instead of silently blank).
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        if (_activeTable.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)) return value;
        if (_fallbackTable.TryGetValue(key, out string fallback) && !string.IsNullOrEmpty(fallback)) return fallback;

        Debug.LogWarning($"[LocalizationManager] Missing localization key: '{key}'");
        return key;
    }

    /// <summary>
    /// Resolves "{resourceBaseName}_{locale}" in Resources for the active locale, falling back to the
    /// English variant if the active locale has no file yet. Used by content that ships as a whole
    /// locale-suffixed file rather than as UI string keys — see MCQManager.LoadQuestions and
    /// DhikrManager.LoadDhikrs, which translate incrementally without any code change: an id with no
    /// translated entry in questions_ar.txt still resolves (the whole file falls back), and a future
    /// per-id fallback can be layered on top of this without touching the callers.
    /// </summary>
    public static TextAsset LoadLocalizedTextAsset(string resourceBaseName)
    {
        string locale = (Instance != null ? Instance.CurrentLocale : FallbackLocale).ToString();

        TextAsset asset = Resources.Load<TextAsset>($"{resourceBaseName}_{locale}");
        if (asset == null && locale != FallbackLocale.ToString())
        {
            asset = Resources.Load<TextAsset>($"{resourceBaseName}_{FallbackLocale}");
        }
        return asset;
    }

    /// <summary>
    /// Looks up a format string and applies args via string.Format. Placeholders are indexed ({0}, {1}, …)
    /// rather than positional concatenation specifically so a translation can reorder them to fit its own
    /// grammar without any code change.
    /// </summary>
    public string Get(string key, params object[] args)
    {
        string format = Get(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException e)
        {
            Debug.LogError($"[LocalizationManager] Bad format string for key '{key}' (\"{format}\"): {e.Message}");
            return format;
        }
    }
}
