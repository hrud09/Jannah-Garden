using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

namespace JannahGarden.Localization
{
    [CustomEditor(typeof(LocalizationManager))]
    public class LocalizationManagerEditor : Editor
    {
        private static int selectedScopeIndex = 0;

        public override void OnInspectorGUI()
        {
            LocalizationManager manager = (LocalizationManager)target;

            // Header Section
            EditorGUILayout.Space(5);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Localization Control", headerStyle);
            EditorGUILayout.Space(5);

            if (GUILayout.Button("Open Standalone Localization Window", GUILayout.Height(35)))
            {
                LocalizationEditorWindow.ShowWindow();
            }
            EditorGUILayout.Space(5);

            // Draw Default Inspector (for basic variables if any)
            DrawDefaultInspector();
        }

        private void ScanAndRegister(Transform root)
        {
            List<Text> uiTexts = new List<Text>();
            List<TMP_Text> tmpTexts = new List<TMP_Text>();

            if (root != null)
            {
                uiTexts.AddRange(root.GetComponentsInChildren<Text>(true));
                tmpTexts.AddRange(root.GetComponentsInChildren<TMP_Text>(true));
            }
            else
            {
                uiTexts.AddRange(FindObjectsOfType<Text>(true));
                tmpTexts.AddRange(FindObjectsOfType<TMP_Text>(true));
            }

            int addedCount = 0;
            int updatedCount = 0;
            List<LocalizedText> affectedComponents = new List<LocalizedText>();

            foreach (var uiText in uiTexts)
            {
                LocalizedText locText = uiText.GetComponent<LocalizedText>();
                if (locText == null)
                {
                    locText = Undo.AddComponent<LocalizedText>(uiText.gameObject);
                    addedCount++;
                }

                if (string.IsNullOrEmpty(locText.defaultText))
                {
                    Undo.RecordObject(locText, "Grab Original Text");
                    locText.defaultText = uiText.text;
                    updatedCount++;
                }

                if (!affectedComponents.Contains(locText))
                {
                    affectedComponents.Add(locText);
                }
                EditorUtility.SetDirty(locText);
            }

            foreach (var tmpText in tmpTexts)
            {
                LocalizedText locText = tmpText.GetComponent<LocalizedText>();
                if (locText == null)
                {
                    locText = Undo.AddComponent<LocalizedText>(tmpText.gameObject);
                    addedCount++;
                }

                if (string.IsNullOrEmpty(locText.defaultText))
                {
                    Undo.RecordObject(locText, "Grab Original Text");
                    locText.defaultText = tmpText.text;
                    updatedCount++;
                }

                if (!affectedComponents.Contains(locText))
                {
                    affectedComponents.Add(locText);
                }
                EditorUtility.SetDirty(locText);
            }

            Debug.Log($"[Localization Automation] Scan complete. Added LocalizedText to {addedCount} GameObjects. Registered {affectedComponents.Count} components in total. Starting automatic translation...");

            if (affectedComponents.Count > 0)
            {
                Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
                AutoTranslateAll(affectedComponents.ToArray(), languages, false);
            }
        }

        private void ExportToCSV(LocalizedText[] allTexts, Language[] languages)
        {
            string path = EditorUtility.SaveFilePanel("Export Translations to CSV", "", "SceneTranslations.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // Build headers
            sb.Append("Hierarchy Path,Default Text (English)");
            for (int i = 1; i < languages.Length; i++) // Skip default (English)
            {
                sb.Append(",");
                sb.Append(EscapeCSV(languages[i].ToString()));
            }
            sb.AppendLine();

            foreach (var textComp in allTexts)
            {
                sb.Append(EscapeCSV(GetGameObjectPath(textComp.gameObject)));
                sb.Append(",");
                sb.Append(EscapeCSV(textComp.defaultText));

                for (int i = 1; i < languages.Length; i++)
                {
                    sb.Append(",");
                    sb.Append(EscapeCSV(textComp.GetTranslation(languages[i])));
                }
                sb.AppendLine();
            }

            try
            {
                System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                Debug.Log($"[Localization Exporter] Successfully exported {allTexts.Length} components to CSV: {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Localization Exporter] Failed to export CSV: {ex.Message}");
            }
        }

        private void ImportFromCSV(LocalizedText[] allTexts, Language[] languages)
        {
            string path = EditorUtility.OpenFilePanel("Import Translations from CSV", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            if (!System.IO.File.Exists(path))
            {
                Debug.LogError($"[Localization Importer] File not found: {path}");
                return;
            }

            try
            {
                string csvContent = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                List<List<string>> rows = ParseCSV(csvContent);

                if (rows.Count < 2)
                {
                    Debug.LogError("[Localization Importer] Invalid CSV format. Missing headers or data.");
                    return;
                }

                List<string> headers = rows[0];
                
                // Map columns to languages
                Dictionary<int, Language> columnMap = new Dictionary<int, Language>();
                for (int col = 2; col < headers.Count; col++)
                {
                    string headerName = headers[col].Trim();
                    foreach (var lang in languages)
                    {
                        if (lang.ToString().Equals(headerName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            columnMap[col] = lang;
                            break;
                        }
                    }
                }

                // Create lookups
                Dictionary<string, LocalizedText> componentMap = new Dictionary<string, LocalizedText>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var textComp in allTexts)
                {
                    string textPath = GetGameObjectPath(textComp.gameObject);
                    if (!componentMap.ContainsKey(textPath))
                    {
                        componentMap.Add(textPath, textComp);
                    }
                }

                int updatedCount = 0;
                for (int r = 1; r < rows.Count; r++)
                {
                    List<string> row = rows[r];
                    if (row.Count < 2) continue;

                    string hierarchyPath = row[0];
                    string defaultText = row[1];

                    if (componentMap.TryGetValue(hierarchyPath, out LocalizedText targetComponent))
                    {
                        Undo.RecordObject(targetComponent, "Import CSV Translation");
                        targetComponent.defaultText = defaultText;

                        for (int col = 2; col < row.Count; col++)
                        {
                            if (columnMap.TryGetValue(col, out Language lang))
                            {
                                targetComponent.SetTranslation(lang, row[col]);
                            }
                        }
                        EditorUtility.SetDirty(targetComponent);
                        updatedCount++;
                    }
                }

                // Force refresh the active preview
                foreach (var text in allTexts)
                {
                    text.PreviewLanguage(LocalizationManager.CurrentLanguage);
                }

                Debug.Log($"[Localization Importer] Successfully updated {updatedCount} LocalizedText components from CSV.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Localization Importer] Failed to import CSV: {ex.Message}");
            }
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }

        private string EscapeCSV(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string escaped = text.Replace("\"", "\"\"");
            if (escaped.Contains(",") || escaped.Contains("\n") || escaped.Contains("\r") || escaped.Contains("\"\""))
            {
                return $"\"{escaped}\"";
            }
            return escaped;
        }

        private List<List<string>> ParseCSV(string text)
        {
            List<List<string>> lines = new List<List<string>>();
            List<string> currentLine = new List<string>();
            System.Text.StringBuilder currentField = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentLine.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        currentLine.Add(currentField.ToString());
                        currentField.Clear();

                        if (currentLine.Count > 0 && !(currentLine.Count == 1 && string.IsNullOrEmpty(currentLine[0])))
                        {
                            lines.Add(new List<string>(currentLine));
                        }
                        currentLine.Clear();

                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            i++;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }

            if (currentField.Length > 0 || currentLine.Count > 0)
            {
                currentLine.Add(currentField.ToString());
                lines.Add(currentLine);
            }

            return lines;
        }

        private void AutoTranslateAll(LocalizedText[] allTexts, Language[] languages, bool forceOverwrite)
        {
            int totalRequests = 0;
            
            // First count total translations needed
            foreach (var textComp in allTexts)
            {
                if (string.IsNullOrEmpty(textComp.defaultText)) continue;

                for (int i = 1; i < languages.Length; i++)
                {
                    Language targetLang = languages[i];
                    string existing = textComp.GetTranslation(targetLang);
                    if (forceOverwrite || string.IsNullOrEmpty(existing))
                    {
                        totalRequests++;
                    }
                }
            }

            if (totalRequests == 0)
            {
                EditorUtility.DisplayDialog("Auto-Translate", "No translations needed!", "OK");
                return;
            }

            int currentRequest = 0;
            using (var client = new System.Net.WebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;

                for (int compIdx = 0; compIdx < allTexts.Length; compIdx++)
                {
                    LocalizedText textComp = allTexts[compIdx];
                    if (string.IsNullOrEmpty(textComp.defaultText)) continue;

                    Undo.RecordObject(textComp, "Auto-Translate All");

                    bool modified = false;
                    for (int i = 1; i < languages.Length; i++)
                    {
                        Language targetLang = languages[i];
                        string existing = textComp.GetTranslation(targetLang);

                        if (forceOverwrite || string.IsNullOrEmpty(existing))
                        {
                            currentRequest++;
                            float progress = (float)currentRequest / totalRequests;
                            
                            if (EditorUtility.DisplayCancelableProgressBar(
                                "Auto-Translating UI...", 
                                $"Translating text to {targetLang} ({currentRequest}/{totalRequests})", 
                                progress))
                            {
                                EditorUtility.ClearProgressBar();
                                Debug.LogWarning("[Localization Auto-Translate] Translation cancelled by user.");
                                return;
                            }

                            string langCode = GetGoogleLanguageCode(targetLang);
                            try
                            {
                                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={langCode}&dt=t&q={System.Uri.EscapeDataString(textComp.defaultText)}";
                                string jsonResponse = client.DownloadString(url);
                                string translated = ParseTranslationJson(jsonResponse);

                                if (!string.IsNullOrEmpty(translated))
                                {
                                    textComp.SetTranslation(targetLang, translated);
                                    modified = true;
                                }

                                // Delay slightly to respect rate limits
                                System.Threading.Thread.Sleep(100);
                            }
                            catch (System.Exception ex)
                            {
                                EditorUtility.ClearProgressBar();
                                Debug.LogError($"[Localization Auto-Translate] Error translating to {targetLang}: {ex.Message}");
                                EditorUtility.DisplayDialog("Translation Error", $"Failed to translate text to {targetLang}.\nError: {ex.Message}", "OK");
                                return;
                            }
                        }
                    }

                    if (modified)
                    {
                        EditorUtility.SetDirty(textComp);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            
            // Refresh preview
            foreach (var text in allTexts)
            {
                text.PreviewLanguage(LocalizationManager.CurrentLanguage);
            }

            EditorUtility.DisplayDialog("Auto-Translate", $"Successfully auto-translated {totalRequests} items!", "OK");
        }

        public static string GetGoogleLanguageCode(Language lang)
        {
            switch (lang)
            {
                case Language.English: return "en";
                case Language.Arabic: return "ar";
                case Language.Bengali: return "bn";
                case Language.Urdu: return "ur";
                default: return "en";
            }
        }

        public static string ParseTranslationJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            
            int startIdx = json.IndexOf("[[[\"");
            if (startIdx != -1)
            {
                startIdx += 4;
                int endIdx = -1;
                for (int i = startIdx; i < json.Length; i++)
                {
                    if (json[i] == '"' && json[i - 1] != '\\')
                    {
                        endIdx = i;
                        break;
                    }
                }
                if (endIdx != -1)
                {
                    string result = json.Substring(startIdx, endIdx - startIdx);
                    result = System.Text.RegularExpressions.Regex.Unescape(result);
                    return result;
                }
            }
            return "";
        }

        private void GrabOriginalTextForScope(Transform root)
        {
            LocalizedText[] targets;
            if (root != null)
            {
                targets = root.GetComponentsInChildren<LocalizedText>(true);
            }
            else
            {
                targets = FindObjectsOfType<LocalizedText>(true);
            }

            int count = 0;
            foreach (var targetComp in targets)
            {
                Undo.RecordObject(targetComp, "Grab Original Text Global");
                targetComp.GrabOriginalText();
                EditorUtility.SetDirty(targetComp);
                count++;
            }
            Debug.Log($"[Localization Automation] Grabbed original text for {count} components.");
            EditorUtility.DisplayDialog("Grab Original Text", $"Successfully grabbed original text for {count} components!", "OK");
        }

        private void AutoTranslateForScope(Transform root, bool forceOverwrite)
        {
            LocalizedText[] targets;
            if (root != null)
            {
                targets = root.GetComponentsInChildren<LocalizedText>(true);
            }
            else
            {
                targets = FindObjectsOfType<LocalizedText>(true);
            }

            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
            AutoTranslateAll(targets, languages, forceOverwrite);
        }

        private void TranslateDatabases()
        {
            TextAsset questionsAsset = Resources.Load<TextAsset>("questions");
            TextAsset dhikrsAsset = Resources.Load<TextAsset>("dhikrs");
            TextAsset dictAsset = Resources.Load<TextAsset>("localization_dictionary");

            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
            
            // 1. Translate Questions
            if (questionsAsset != null)
            {
                try
                {
                    QuestionList engQuestions = JsonUtility.FromJson<QuestionList>(questionsAsset.text);
                    using (var client = new System.Net.WebClient())
                    {
                        client.Encoding = System.Text.Encoding.UTF8;

                        for (int i = 1; i < languages.Length; i++)
                        {
                            Language targetLang = languages[i];
                            string langCode = GetGoogleLanguageCode(targetLang);

                            float progress = (float)i / languages.Length;
                            if (EditorUtility.DisplayCancelableProgressBar("Translating Databases", $"Translating questions to {targetLang}...", progress))
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }

                            for (int q = 0; q < engQuestions.questions.Length; q++)
                            {
                                QuestionData qData = engQuestions.questions[q];
                                LocalizedQuestionData engData = qData.GetTranslation(Language.English);
                                if (engData == null) continue;

                                bool exists = false;
                                if (qData.translations != null)
                                {
                                    foreach (var t in qData.translations)
                                    {
                                        if (t.language == targetLang) { exists = true; break; }
                                    }
                                }
                                if (exists) continue; // Skip if already translated

                                LocalizedQuestionData transData = new LocalizedQuestionData();
                                transData.language = targetLang;
                                transData.questionText = TranslateString(client, engData.questionText, langCode);
                                
                                transData.options = new string[engData.options.Length];
                                for (int o = 0; o < engData.options.Length; o++)
                                {
                                    transData.options[o] = TranslateString(client, engData.options[o], langCode);
                                }

                                var list = new System.Collections.Generic.List<LocalizedQuestionData>();
                                if (qData.translations != null) list.AddRange(qData.translations);
                                list.Add(transData);
                                qData.translations = list.ToArray();

                                System.Threading.Thread.Sleep(50);
                            }
                        }

                        string json = JsonUtility.ToJson(engQuestions, true);
                        string savePath = UnityEditor.AssetDatabase.GetAssetPath(questionsAsset);
                        System.IO.File.WriteAllText(savePath, json, System.Text.Encoding.UTF8);
                        UnityEditor.AssetDatabase.Refresh();
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Database Translator] Questions translation failed: {ex.Message}");
                }
            }

            // 2. Translate Dhikrs
            if (dhikrsAsset != null)
            {
                try
                {
                    DhikrList engDhikrs = JsonUtility.FromJson<DhikrList>(dhikrsAsset.text);
                    using (var client = new System.Net.WebClient())
                    {
                        client.Encoding = System.Text.Encoding.UTF8;
                        for (int i = 1; i < languages.Length; i++)
                        {
                            Language targetLang = languages[i];
                            string langCode = GetGoogleLanguageCode(targetLang);

                            float progress = (float)i / languages.Length;
                            if (EditorUtility.DisplayCancelableProgressBar("Translating Databases", $"Translating dhikrs to {targetLang}...", progress))
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }

                            string[] engStrings = engDhikrs.GetDhikrs(Language.English);
                            if (engStrings == null || engStrings.Length == 0) continue;
                            
                            bool exists = false;
                            if (engDhikrs.translations != null)
                            {
                                foreach (var t in engDhikrs.translations)
                                {
                                    if (t.language == targetLang) { exists = true; break; }
                                }
                            }
                            if (exists) continue;

                            LocalizedDhikrData transData = new LocalizedDhikrData();
                            transData.language = targetLang;
                            transData.dhikrs = new string[engStrings.Length];

                            for (int d = 0; d < engStrings.Length; d++)
                            {
                                transData.dhikrs[d] = TranslateString(client, engStrings[d], langCode);
                                System.Threading.Thread.Sleep(50);
                            }

                            var list = new System.Collections.Generic.List<LocalizedDhikrData>();
                            if (engDhikrs.translations != null) list.AddRange(engDhikrs.translations);
                            list.Add(transData);
                            engDhikrs.translations = list.ToArray();
                        }

                        string json = JsonUtility.ToJson(engDhikrs, true);
                        string savePath = UnityEditor.AssetDatabase.GetAssetPath(dhikrsAsset);
                        System.IO.File.WriteAllText(savePath, json, System.Text.Encoding.UTF8);
                        UnityEditor.AssetDatabase.Refresh();
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Database Translator] Dhikrs translation failed: {ex.Message}");
                }
            }

            // 3. Translate UI Dictionary templates
            if (dictAsset != null)
            {
                try
                {
                    TranslationDictionary engDict = JsonUtility.FromJson<TranslationDictionary>(dictAsset.text);
                    using (var client = new System.Net.WebClient())
                    {
                        client.Encoding = System.Text.Encoding.UTF8;

                        for (int i = 1; i < languages.Length; i++)
                        {
                            Language targetLang = languages[i];
                            string langCode = GetGoogleLanguageCode(targetLang);

                            float progress = (float)i / languages.Length;
                            if (EditorUtility.DisplayCancelableProgressBar("Translating Databases", $"Translating UI phrases to {targetLang}...", progress))
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }

                            foreach (var entry in engDict.entries)
                            {
                                bool hasTranslation = false;
                                for (int j = 0; j < entry.translations.Count; j++)
                                {
                                    if (entry.translations[j].language == targetLang)
                                    {
                                        hasTranslation = true;
                                        break;
                                    }
                                }

                                if (!hasTranslation)
                                {
                                    string translatedVal = TranslateString(client, entry.key, langCode);
                                    entry.translations.Add(new TranslationEntry
                                    {
                                        language = targetLang,
                                        text = translatedVal
                                    });
                                    System.Threading.Thread.Sleep(50);
                                }
                            }
                        }

                        // Save dictionary back to the same file
                        string json = JsonUtility.ToJson(engDict, true);
                        string savePath = "Assets/Resources/localization_dictionary.txt";
                        System.IO.File.WriteAllText(savePath, json, System.Text.Encoding.UTF8);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Database Translator] Dictionary translation failed: {ex.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Database Translation", "Successfully generated localized files for questions, dhikrs, and localization dictionary templates!", "OK");
        }

        public static string TranslateString(System.Net.WebClient client, string source, string langCode)
        {
            if (string.IsNullOrEmpty(source)) return "";
            try
            {
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={langCode}&dt=t&q={System.Uri.EscapeDataString(source)}";
                string jsonResponse = client.DownloadString(url);
                return ParseTranslationJson(jsonResponse);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Database Translation Warning] Failed to translate '{source}' to {langCode}: {ex.Message}");
                return source; // Fallback to original
            }
        }
    }
}
