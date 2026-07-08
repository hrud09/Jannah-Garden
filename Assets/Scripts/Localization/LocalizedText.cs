using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JannahGarden.Localization
{
    [DisallowMultipleComponent]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private Text uiText;
        [SerializeField] private TMP_Text tmpText;

        [HideInInspector]
        public string defaultText;

        [HideInInspector]
        public List<TranslationEntry> translations = new List<TranslationEntry>();

        private string lastLocalizedText;

        private void Awake()
        {
            CacheComponents();
            // Subscribe to language change events
            LocalizationManager.OnLanguageChanged += UpdateText;
        }

        private void Start()
        {
            // Initial text update
            UpdateText(LocalizationManager.CurrentLanguage);
        }

        private void OnDestroy()
        {
            LocalizationManager.OnLanguageChanged -= UpdateText;
        }

        public void CacheComponents()
        {
            if (uiText == null) uiText = GetComponent<Text>();
            if (tmpText == null) tmpText = GetComponent<TMP_Text>();
        }

        // Grabs text directly from the attached Text/TMP_Text and sets as defaultText
        public void GrabOriginalText()
        {
            CacheComponents();
            if (uiText != null)
            {
                defaultText = uiText.text;
            }
            else if (tmpText != null)
            {
                defaultText = tmpText.text;
            }
            else
            {
                Debug.LogWarning($"[LocalizedText] No Text or TMP_Text component found on {gameObject.name} to grab text from.");
            }
        }

        public string GetTranslation(Language lang)
        {
            if (lang == Language.English) // Assuming English is our default
            {
                return defaultText;
            }

            foreach (var entry in translations)
            {
                if (entry.language == lang)
                {
                    return entry.text;
                }
            }

            return defaultText; // Fallback to default
        }

        private Text generatedLegacyText;

        private void EnsureLegacyTextExists()
        {
            if (uiText != null) return;
            if (generatedLegacyText != null) return;
            
            if (tmpText != null)
            {
                Transform fallback = tmpText.transform.Find("LegacyTextFallback");
                if (fallback != null)
                {
                    generatedLegacyText = fallback.GetComponent<Text>();
                }
                
                if (generatedLegacyText == null)
                {
                    GameObject go = new GameObject("LegacyTextFallback");
                    go.transform.SetParent(tmpText.transform, false);
                    
                    RectTransform rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    
                    generatedLegacyText = go.AddComponent<Text>();
                    // Approximate properties
                    generatedLegacyText.alignment = TextAnchor.MiddleCenter; 
                    generatedLegacyText.color = tmpText.color;
                    generatedLegacyText.fontSize = (int)tmpText.fontSize;
                    generatedLegacyText.raycastTarget = tmpText.raycastTarget;
                    generatedLegacyText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    generatedLegacyText.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }
        }

        private void LateUpdate()
        {
            if (LocalizationManager.Instance == null) return;
            bool useLegacy = LocalizationManager.Instance.ShouldUseLegacyText(LocalizationManager.CurrentLanguage);
            
            if (useLegacy)
            {
                if (tmpText != null) 
                {
                    tmpText.enabled = false;
                    var cr = tmpText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                
                Text targetText = uiText != null ? uiText : generatedLegacyText;
                if (targetText != null)
                {
                    targetText.enabled = true;
                    var cr = targetText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = false;
                }
            }
            else
            {
                if (generatedLegacyText != null) 
                {
                    generatedLegacyText.enabled = false;
                    var cr = generatedLegacyText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                if (uiText != null) 
                {
                    uiText.enabled = false;
                    var cr = uiText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                
                if (tmpText != null)
                {
                    tmpText.enabled = true;
                    var cr = tmpText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = false;
                }
            }
        }

        private string dynamicTextValue = null;

        public void SetDynamicText(string text)
        {
            dynamicTextValue = text;
            UpdateText(LocalizationManager.Instance != null ? LocalizationManager.CurrentLanguage : Language.English);
        }

        public void UpdateText(Language currentLanguage)
        {
            CacheComponents();
            string translatedText;
            if (!string.IsNullOrEmpty(dynamicTextValue))
            {
                translatedText = dynamicTextValue;
            }
            else
            {
                translatedText = GetTranslation(currentLanguage);

                // Fallback to global dictionary if no local translation is defined
                if (translatedText == defaultText && currentLanguage != Language.English)
                {
                    if (LocalizationManager.Instance != null)
                    {
                        translatedText = LocalizationManager.Instance.GetTranslation(defaultText, defaultText);
                    }
                }
            }

            lastLocalizedText = translatedText;

            bool useLegacy = false;
            if (LocalizationManager.Instance != null)
            {
                useLegacy = LocalizationManager.Instance.ShouldUseLegacyText(currentLanguage);
            }
            else
            {
                useLegacy = (currentLanguage == Language.Arabic || currentLanguage == Language.Bengali || currentLanguage == Language.Urdu);
            }

            if (useLegacy)
            {
                if (tmpText != null) 
                {
                    tmpText.enabled = false;
                    var cr = tmpText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                EnsureLegacyTextExists();
                
                if (uiText != null) 
                {
                    uiText.enabled = false;
                    var cr = uiText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                if (generatedLegacyText != null) 
                {
                    generatedLegacyText.enabled = false;
                    var cr = generatedLegacyText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }

                Text targetText = uiText != null ? uiText : generatedLegacyText;
                if (targetText != null)
                {
                    targetText.enabled = true;
                    var cr = targetText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = false;

                    targetText.text = translatedText;
                    targetText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    targetText.verticalOverflow = VerticalWrapMode.Overflow;
                    
                    if (LocalizationManager.Instance != null)
                    {
                        Font langFont = LocalizationManager.Instance.GetFontForLanguage(currentLanguage);
                        if (langFont != null)
                        {
                            targetText.font = langFont;
                        }
                    }

#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEditor.EditorUtility.SetDirty(targetText);
                        UnityEditor.EditorUtility.SetDirty(gameObject);
                    }
#endif
                }
            }
            else
            {
                if (generatedLegacyText != null) 
                {
                    generatedLegacyText.enabled = false;
                    var cr = generatedLegacyText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                if (uiText != null) 
                {
                    uiText.enabled = false;
                    var cr = uiText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = true;
                }
                
                if (tmpText != null)
                {
                    tmpText.enabled = true;
                    var cr = tmpText.GetComponent<CanvasRenderer>();
                    if (cr != null) cr.cull = false;

                    tmpText.text = translatedText;

                    if (LocalizationManager.Instance != null)
                    {
                        TMP_FontAsset langTMPFont = LocalizationManager.Instance.GetTMPFontForLanguage(currentLanguage);
                        if (langTMPFont != null)
                        {
                            tmpText.font = langTMPFont;
                        }
                    }

#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEditor.EditorUtility.SetDirty(tmpText);
                        UnityEditor.EditorUtility.SetDirty(gameObject);
                    }
#endif
                }
                else
                {
                    Debug.LogWarning($"[LocalizedText] No TMP_Text component found on {gameObject.name} for language {currentLanguage} which requires TextMeshPro.");
                }
            }
        }

        // Helper to apply translation in Editor preview
        public void PreviewLanguage(Language lang)
        {
            UpdateText(lang);
        }

        public void SetTranslation(Language lang, string value)
        {
            if (lang == Language.English)
            {
                defaultText = value;
                return;
            }

            for (int i = 0; i < translations.Count; i++)
            {
                if (translations[i].language == lang)
                {
                    var entry = translations[i];
                    entry.text = value;
                    translations[i] = entry;
                    return;
                }
            }

            translations.Add(new TranslationEntry { language = lang, text = value });
        }
    }

    [System.Serializable]
    public struct TranslationEntry
    {
        public Language language;
        [TextArea(3, 10)]
        public string text;
    }
}
