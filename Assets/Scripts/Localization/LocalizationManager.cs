using UnityEngine;

namespace JannahGarden.Localization
{
    public class LocalizationManager : MonoBehaviour
    {
        private static LocalizationManager instance;
        public static LocalizationManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<LocalizationManager>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("LocalizationManager");
                        instance = go.AddComponent<LocalizationManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        public System.Collections.Generic.List<LanguageFontConfig> fontConfigs = new System.Collections.Generic.List<LanguageFontConfig>();


        private static Language currentLanguage = Language.English;
        private static bool isInitialized = false;

        public static Language CurrentLanguage
        {
            get
            {
                if (!isInitialized)
                {
                    // Load saved language or fall back to system language if never saved
                    if (PlayerPrefs.HasKey("SelectedLanguage"))
                    {
                        currentLanguage = (Language)PlayerPrefs.GetInt("SelectedLanguage", 0);
                    }
                    else
                    {
                        currentLanguage = GetSystemLanguageFallback();
                    }
                    isInitialized = true;
                }
                return currentLanguage;
            }
            private set
            {
                currentLanguage = value;
                isInitialized = true;
            }
        }

        public static event System.Action<Language> OnLanguageChanged;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }

            // Force initialization of language
            Language init = CurrentLanguage;
        }

        public void SetLanguage(Language newLanguage)
        {
            CurrentLanguage = newLanguage;
            PlayerPrefs.SetInt("SelectedLanguage", (int)newLanguage);
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke(newLanguage);
            Debug.Log($"[LocalizationManager] Language changed to: {newLanguage} ({GetLanguageNativeName(newLanguage)})");
        }

        #if UNITY_EDITOR
        // Utility for editor scripting to test translations instantly in Edit Mode
        public void EditorSetLanguage(Language newLanguage)
        {
            CurrentLanguage = newLanguage;
            OnLanguageChanged?.Invoke(newLanguage);
        }
        #endif

        private static Language GetSystemLanguageFallback()
        {
            SystemLanguage systemLang = Application.systemLanguage;
            switch (systemLang)
            {
                case SystemLanguage.Arabic: return Language.Arabic;
                case SystemLanguage.Spanish: return Language.Spanish;
                case SystemLanguage.French: return Language.French;
                case SystemLanguage.German: return Language.German;
                case SystemLanguage.Turkish: return Language.Turkish;
                case SystemLanguage.Indonesian: return Language.BahasaIndonesia;
                default: return Language.English;
            }
        }

        public static string GetLanguageCode(Language lang)
        {
            switch (lang)
            {
                case Language.English: return "en";
                case Language.Arabic: return "ar";
                case Language.Spanish: return "es";
                case Language.French: return "fr";
                case Language.German: return "de";
                case Language.Turkish: return "tr";
                case Language.BahasaIndonesia: return "id";
                case Language.Bengali: return "bn";
                default: return "en";
            }
        }

        public static string GetLanguageNativeName(Language lang)
        {
            switch (lang)
            {
                case Language.English: return "English";
                case Language.Arabic: return "العربية";
                case Language.Spanish: return "Español";
                case Language.French: return "Français";
                case Language.German: return "Deutsch";
                case Language.Turkish: return "Türkçe";
                case Language.BahasaIndonesia: return "Bahasa Indonesia";
                case Language.Bengali: return "বাংলা";
                default: return "English";
            }
        }

        // --- Dictionary Translation system ---
        private static TranslationDictionary dictionary;
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<Language, string>> dictLookup;

        private static void LoadDictionary()
        {
            dictLookup = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<Language, string>>(System.StringComparer.OrdinalIgnoreCase);
            TextAsset dictAsset = Resources.Load<TextAsset>("localization_dictionary");
            if (dictAsset != null)
            {
                try
                {
                    dictionary = JsonUtility.FromJson<TranslationDictionary>(dictAsset.text);
                    if (dictionary != null && dictionary.entries != null)
                    {
                        foreach (var entry in dictionary.entries)
                        {
                            var langMap = new System.Collections.Generic.Dictionary<Language, string>();
                            if (entry.translations != null)
                            {
                                foreach (var trans in entry.translations)
                                {
                                    langMap[trans.language] = trans.text;
                                }
                            }
                            dictLookup[entry.key] = langMap;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[LocalizationManager] Failed to load translation dictionary: {ex.Message}");
                }
            }
        }

        public string GetTranslation(string key, string defaultValue = "")
        {
            if (dictLookup == null)
            {
                LoadDictionary();
            }

            if (CurrentLanguage == Language.English)
            {
                return !string.IsNullOrEmpty(defaultValue) ? defaultValue : key;
            }

            if (dictLookup.TryGetValue(key, out var langMap))
            {
                if (langMap.TryGetValue(CurrentLanguage, out string val))
                {
                    return val;
                }
            }

            return !string.IsNullOrEmpty(defaultValue) ? defaultValue : key;
        }

        public Font GetFontForLanguage(Language lang)
        {
            foreach (var config in fontConfigs)
            {
                if (config.language == lang && config.font != null)
                {
                    return config.font;
                }
            }
            return null; // or a default font
        }

        public TMPro.TMP_FontAsset GetTMPFontForLanguage(Language lang)
        {
            foreach (var config in fontConfigs)
            {
                if (config.language == lang && config.tmpFont != null)
                {
                    return config.tmpFont;
                }
            }
            return null; // or a default TMP font
        }

        public bool ShouldUseLegacyText(Language lang)
        {
            foreach (var config in fontConfigs)
            {
                if (config.language == lang)
                {
                    return config.useLegacyText;
                }
            }
            // Hardcode Bengali and Arabic to use legacy text by default if not configured
            if (lang == Language.Bengali || lang == Language.Arabic)
                return true;
                
            return false;
        }
    }

    [System.Serializable]
    public class TranslationDictEntry
    {
        public string key;
        public System.Collections.Generic.List<TranslationEntry> translations = new System.Collections.Generic.List<TranslationEntry>();
    }

    [System.Serializable]
    public class TranslationDictionary
    {
        public System.Collections.Generic.List<TranslationDictEntry> entries = new System.Collections.Generic.List<TranslationDictEntry>();
    }

    [System.Serializable]
    public struct LanguageFontConfig
    {
        public Language language;
        public Font font;
        public TMPro.TMP_FontAsset tmpFont;
        public bool useLegacyText;
    }
}
