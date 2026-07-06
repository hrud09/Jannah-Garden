#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace JannahGarden.Localization
{
    public class LocalizationEditorWindow : EditorWindow
    {
        private int activeTab = 0;
        private readonly string[] tabTitles = { "Scene Translations", "Tools & Automation" };

        private int selectedScopeIndex = 0;

        // Search & Filtering
        private string searchString = "";
        private bool showOnlyMissing = false;

        // Language visibility toggles
        private Dictionary<Language, bool> visibleLanguages = new Dictionary<Language, bool>();

        // Scrolling
        private Vector2 scrollPos;
        private Vector2 toolsScrollPos;

        // Active scene components cache
        private List<LocalizedText> sceneTexts = new List<LocalizedText>();

        // Highlight/focus target from outside
        private LocalizedText focusTarget = null;

        [MenuItem("Tools/Jannah Garden/Localization Editor Window")]
        public static void ShowWindow()
        {
            LocalizationEditorWindow window = GetWindow<LocalizationEditorWindow>("Localization Editor");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        public static void ShowWindowAndFocus(LocalizedText target)
        {
            LocalizationEditorWindow window = GetWindow<LocalizationEditorWindow>("Localization Editor");
            window.minSize = new Vector2(600, 400);
            window.focusTarget = target;
            if (target != null)
            {
                window.searchString = target.gameObject.name;
            }
            window.activeTab = 0; // Scene Translations tab
            window.Show();
        }

        private void OnEnable()
        {
            // Initialize language visibility
            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
            foreach (var lang in languages)
            {
                if (!visibleLanguages.ContainsKey(lang))
                {
                    // Default to showing all
                    visibleLanguages[lang] = true;
                }
            }
            RefreshSceneTexts();
        }

        private void OnFocus()
        {
            RefreshSceneTexts();
        }

        private void RefreshSceneTexts()
        {
            sceneTexts.Clear();
            LocalizedText[] found = FindObjectsOfType<LocalizedText>(true);
            sceneTexts.AddRange(found);
        }

        private void OnGUI()
        {
            // Title Header
            EditorGUILayout.Space(10);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft
            };
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Jannah Garden Localization Manager", titleStyle, GUILayout.Height(25));
            if (GUILayout.Button("Refresh Scene List", GUILayout.Width(130), GUILayout.Height(25)))
            {
                RefreshSceneTexts();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);

            // Tab Selection
            activeTab = GUILayout.Toolbar(activeTab, tabTitles, GUILayout.Height(25));
            EditorGUILayout.Space(10);

            if (activeTab == 0)
            {
                DrawSceneTranslationsTab();
            }
            else
            {
                DrawToolsAndAutomationTab();
            }
        }

        private void DrawSceneTranslationsTab()
        {
            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));

            // Search and Filters Bar
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            searchString = EditorGUILayout.TextField(searchString, GUILayout.ExpandWidth(true));
            
            showOnlyMissing = EditorGUILayout.ToggleLeft("Show Only Missing Translations", showOnlyMissing, GUILayout.Width(220));

            EditorGUILayout.EndHorizontal();

            // Language Visibilities
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Visible Languages in Editor:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            
            int togglesPerRow = 4;
            for (int i = 0; i < languages.Length; i++)
            {
                Language lang = languages[i];
                visibleLanguages[lang] = EditorGUILayout.ToggleLeft($"{lang} ({LocalizationManager.GetLanguageNativeName(lang)})", visibleLanguages[lang], GUILayout.Width(140));

                if ((i + 1) % togglesPerRow == 0 && i < languages.Length - 1)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Filter sceneTexts
            List<LocalizedText> filteredTexts = new List<LocalizedText>();
            foreach (var item in sceneTexts)
            {
                if (item == null) continue;

                // Search string filter
                bool matchesSearch = string.IsNullOrEmpty(searchString) || 
                                     item.gameObject.name.Contains(searchString, System.StringComparison.OrdinalIgnoreCase) ||
                                     item.defaultText.Contains(searchString, System.StringComparison.OrdinalIgnoreCase);

                if (!matchesSearch)
                {
                    // Check translations too
                    foreach (var entry in item.translations)
                    {
                        if (entry.text.Contains(searchString, System.StringComparison.OrdinalIgnoreCase))
                        {
                            matchesSearch = true;
                            break;
                        }
                    }
                }

                if (!matchesSearch) continue;

                // Missing translations filter
                if (showOnlyMissing)
                {
                    bool isMissingAny = false;
                    for (int i = 1; i < languages.Length; i++) // Skip English as default/source
                    {
                        if (string.IsNullOrEmpty(item.GetTranslation(languages[i])))
                        {
                            isMissingAny = true;
                            break;
                        }
                    }
                    if (!isMissingAny) continue;
                }

                filteredTexts.Add(item);
            }

            // Results count
            EditorGUILayout.LabelField($"Showing {filteredTexts.Count} of {sceneTexts.Count} localizations in scene", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);

            // Scroll view for results
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

            if (filteredTexts.Count == 0)
            {
                EditorGUILayout.HelpBox("No localized text elements match your filters.", MessageType.Info);
            }
            else
            {
                foreach (var textComp in filteredTexts)
                {
                    DrawLocalizedTextCard(textComp, languages);
                }
            }

            EditorGUILayout.EndScrollView();

            // Handle focused element scrolling if requested from outside
            if (focusTarget != null)
            {
                // We clear it after drawing once
                focusTarget = null;
            }
        }

        private void DrawLocalizedTextCard(LocalizedText textComp, Language[] languages)
        {
            // Highlight focused target if we are focusing it
            bool isFocus = (focusTarget == textComp);
            GUIStyle cardStyle = new GUIStyle(EditorStyles.helpBox);
            if (isFocus)
            {
                // Highlight yellow/orange-ish in box style
                cardStyle.normal.textColor = Color.yellow;
            }

            EditorGUILayout.BeginVertical(cardStyle);

            // Card Header
            EditorGUILayout.BeginHorizontal();
            
            // Link icon or select button
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                Selection.activeGameObject = textComp.gameObject;
                EditorGUIUtility.PingObject(textComp.gameObject);
            }

            var labelStyle = new GUIStyle(EditorStyles.boldLabel);
            if (isFocus) labelStyle.normal.textColor = Color.red;

            EditorGUILayout.LabelField(textComp.gameObject.name, labelStyle, GUILayout.MinWidth(150));
            
            GUILayout.FlexibleSpace();

            // Quick Automation Actions
            if (GUILayout.Button("Grab Original", GUILayout.Width(100)))
            {
                Undo.RecordObject(textComp, "Grab Original Text");
                textComp.GrabOriginalText();
                EditorUtility.SetDirty(textComp);
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(textComp.gameObject.scene);
            }

            if (GUILayout.Button("Auto-Translate", GUILayout.Width(110)))
            {
                AutoTranslateSingle(textComp);
            }

            EditorGUILayout.EndHorizontal();

            // Show Path
            EditorGUILayout.LabelField($"Path: {GetGameObjectPath(textComp.gameObject)}", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);

            // English / Default
            if (visibleLanguages[Language.English])
            {
                EditorGUI.BeginChangeCheck();
                string newDefault = EditorGUILayout.TextField("Default Text (English)", textComp.defaultText);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(textComp, "Edit Default Text");
                    textComp.defaultText = newDefault;
                    EditorUtility.SetDirty(textComp);
                    if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(textComp.gameObject.scene);
                }
            }

            // Other visible languages
            for (int i = 1; i < languages.Length; i++)
            {
                Language lang = languages[i];
                if (visibleLanguages[lang])
                {
                    string existing = textComp.GetTranslation(lang);
                    EditorGUI.BeginChangeCheck();
                    string newTrans = EditorGUILayout.TextField($"{lang} ({LocalizationManager.GetLanguageNativeName(lang)})", existing);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(textComp, $"Edit {lang} Translation");
                        textComp.SetTranslation(lang, newTrans);
                        EditorUtility.SetDirty(textComp);
                        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(textComp.gameObject.scene);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void DrawToolsAndAutomationTab()
        {
            toolsScrollPos = EditorGUILayout.BeginScrollView(toolsScrollPos);

            // Get standard types for scope
            LocalizationManager manager = FindObjectOfType<LocalizationManager>();
            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));

            // --- 1. Language Swapping Panel ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Language Preview Selector", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Switch the active preview language in the Editor or at Runtime. Swapping in Edit Mode updates the text display on all objects immediately.", MessageType.Info);

            Language selectedLang = (Language)EditorGUILayout.EnumPopup("Active Language", LocalizationManager.CurrentLanguage);
            if (selectedLang != LocalizationManager.CurrentLanguage)
            {
                ApplyLanguagePreview(selectedLang, manager);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Quick Switch:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            int buttonsPerRow = 4;
            for (int i = 0; i < languages.Length; i++)
            {
                Language lang = languages[i];
                string btnLabel = $"{LocalizationManager.GetLanguageNativeName(lang)} ({lang})";
                
                GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
                if (LocalizationManager.CurrentLanguage == lang)
                {
                    btnStyle.fontStyle = FontStyle.Bold;
                    btnStyle.normal.textColor = Color.yellow;
                }

                if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Height(25)))
                {
                    ApplyLanguagePreview(lang, manager);
                }

                if ((i + 1) % buttonsPerRow == 0 && i < languages.Length - 1)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- 2. Automation Control Panel ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Automation Control Panel", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Perform batch operations on either the active scene or currently selected hierarchies.", MessageType.Info);

            // Scope Toolbar
            selectedScopeIndex = GUILayout.Toolbar(selectedScopeIndex, new string[] { "Active Scene Scope", "Selection Scope" });
            EditorGUILayout.Space(5);

            Transform targetScopeRoot = null;
            bool isSelectionScope = selectedScopeIndex == 1;
            bool hasValidSelection = true;

            if (isSelectionScope)
            {
                GameObject selectedGo = Selection.activeGameObject;
                if (selectedGo == null)
                {
                    EditorGUILayout.HelpBox("Please select a GameObject in the Hierarchy to use Selection Scope.", MessageType.Warning);
                    hasValidSelection = false;
                }
                else
                {
                    targetScopeRoot = selectedGo.transform;
                    EditorGUILayout.HelpBox($"Targeting selection: '{selectedGo.name}' and all its children.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Targeting all localized texts in the active scene.", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(!hasValidSelection);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan & Auto-Translate New UI", GUILayout.Height(30)))
            {
                ScanAndRegisterForWindow(targetScopeRoot);
            }
            if (GUILayout.Button("Grab Original Text", GUILayout.Height(30)))
            {
                GrabOriginalTextForScope(targetScopeRoot);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            if (GUILayout.Button("Force Re-Translate Scope (Overwrite)", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("Confirm Re-translation", "Are you sure you want to overwrite all existing translations with auto-translated values?", "Yes", "No"))
                {
                    AutoTranslateForScope(targetScopeRoot, true);
                }
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- 3. CSV Import/Export ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("CSV Export / Import Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Export all translations in the scene to CSV for editing in Excel/Google Sheets, and import them back.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export to CSV...", GUILayout.Height(30)))
            {
                ExportToCSVForWindow(languages);
            }
            if (GUILayout.Button("Import from CSV...", GUILayout.Height(30)))
            {
                ImportFromCSVForWindow(languages);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- 4. Database Translators ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Database & Template Localization Translator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Auto-translate text databases like questions, dhikrs, and localization dictionary templates to Resources files.", MessageType.Info);

            if (GUILayout.Button("Translate JSON Databases & UI Dictionaries", GUILayout.Height(35)))
            {
                TranslateDatabasesForWindow();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        private void ApplyLanguagePreview(Language lang, LocalizationManager manager)
        {
            if (Application.isPlaying)
            {
                if (manager != null)
                {
                    manager.SetLanguage(lang);
                }
                else
                {
                    LocalizationManager.Instance.SetLanguage(lang);
                }
            }
            else
            {
                LocalizedText[] allTexts = FindObjectsOfType<LocalizedText>(true);
                Undo.RecordObjects(allTexts, "Change Active Preview Language");
                
                if (manager != null)
                {
                    manager.EditorSetLanguage(lang);
                }
                
                foreach (var text in allTexts)
                {
                    text.PreviewLanguage(lang);
                    EditorUtility.SetDirty(text);
                }
                Debug.Log($"[Localization Editor] Preview language updated to {lang} for {allTexts.Length} components.");
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

        // --- Single Element Auto Translation ---
        private void AutoTranslateSingle(LocalizedText textComp)
        {
            if (string.IsNullOrEmpty(textComp.defaultText))
            {
                EditorUtility.DisplayDialog("Auto-Translate", "Please grab or enter Default Text first.", "OK");
                return;
            }

            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
            int count = 0;

            using (var client = new System.Net.WebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;

                for (int i = 1; i < languages.Length; i++)
                {
                    Language targetLang = languages[i];
                    string langCode = LocalizationManagerEditor.GetGoogleLanguageCode(targetLang);

                    try
                    {
                        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={langCode}&dt=t&q={System.Uri.EscapeDataString(textComp.defaultText)}";
                        string jsonResponse = client.DownloadString(url);
                        string translated = LocalizationManagerEditor.ParseTranslationJson(jsonResponse);

                        if (!string.IsNullOrEmpty(translated))
                        {
                            Undo.RecordObject(textComp, "Auto-Translate Component");
                            textComp.SetTranslation(targetLang, translated);
                            count++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[Localization Auto-Translate] Error translating to {targetLang}: {ex.Message}");
                    }
                }
            }

            EditorUtility.SetDirty(textComp);
            textComp.PreviewLanguage(LocalizationManager.CurrentLanguage);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(textComp.gameObject.scene);
            EditorUtility.DisplayDialog("Auto-Translate", $"Successfully auto-translated {count} languages for this text element!", "OK");
        }

        // --- Batch Tools adapted from LocalizationManagerEditor ---
        private void ScanAndRegisterForWindow(Transform root = null)
        {
            List<UnityEngine.UI.Text> uiTexts = new List<UnityEngine.UI.Text>();
            List<TMPro.TMP_Text> tmpTexts = new List<TMPro.TMP_Text>();

            if (root != null)
            {
                uiTexts.AddRange(root.GetComponentsInChildren<UnityEngine.UI.Text>(true));
                tmpTexts.AddRange(root.GetComponentsInChildren<TMPro.TMP_Text>(true));
            }
            else
            {
                uiTexts.AddRange(FindObjectsOfType<UnityEngine.UI.Text>(true));
                tmpTexts.AddRange(FindObjectsOfType<TMPro.TMP_Text>(true));
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
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(locText.gameObject.scene);
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
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(locText.gameObject.scene);
            }

            Debug.Log($"[Localization Window] Scan complete. Added LocalizedText to {addedCount} GameObjects. Registered {affectedComponents.Count} components in total.");
            RefreshSceneTexts();

            if (affectedComponents.Count > 0)
            {
                Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
                AutoTranslateBatch(affectedComponents.ToArray(), languages, false);
            }
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
                RefreshSceneTexts();
                targets = sceneTexts.ToArray();
            }

            int count = 0;
            foreach (var textComp in targets)
            {
                Undo.RecordObject(textComp, "Grab Original Text Global");
                textComp.GrabOriginalText();
                EditorUtility.SetDirty(textComp);
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(textComp.gameObject.scene);
                count++;
            }
            Debug.Log($"[Localization Window] Grabbed original text for {count} components.");
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
                RefreshSceneTexts();
                targets = sceneTexts.ToArray();
            }

            Language[] languages = (Language[])System.Enum.GetValues(typeof(Language));
            AutoTranslateBatch(targets, languages, forceOverwrite);
        }

        private void AutoTranslateBatch(LocalizedText[] allTexts, Language[] languages, bool forceOverwrite)
        {
            int totalRequests = 0;
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

                            string langCode = LocalizationManagerEditor.GetGoogleLanguageCode(targetLang);
                            try
                            {
                                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={langCode}&dt=t&q={System.Uri.EscapeDataString(textComp.defaultText)}";
                                string jsonResponse = client.DownloadString(url);
                                string translated = LocalizationManagerEditor.ParseTranslationJson(jsonResponse);

                                if (!string.IsNullOrEmpty(translated))
                                {
                                    textComp.SetTranslation(targetLang, translated);
                                    modified = true;
                                }

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
                        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(textComp.gameObject.scene);
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

        private void ExportToCSVForWindow(Language[] languages)
        {
            RefreshSceneTexts();
            string path = EditorUtility.SaveFilePanel("Export Translations to CSV", "", "SceneTranslations.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // Build headers
            sb.Append("Hierarchy Path,Default Text (English)");
            for (int i = 1; i < languages.Length; i++)
            {
                sb.Append(",");
                sb.Append(EscapeCSV(languages[i].ToString()));
            }
            sb.AppendLine();

            foreach (var textComp in sceneTexts)
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
                Debug.Log($"[Localization Exporter] Successfully exported {sceneTexts.Count} components to CSV: {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Localization Exporter] Failed to export CSV: {ex.Message}");
            }
        }

        private void ImportFromCSVForWindow(Language[] languages)
        {
            RefreshSceneTexts();
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

                Dictionary<string, LocalizedText> componentMap = new Dictionary<string, LocalizedText>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var textComp in sceneTexts)
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
                        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(targetComponent.gameObject.scene);
                        updatedCount++;
                    }
                }

                foreach (var text in sceneTexts)
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

        private void TranslateDatabasesForWindow()
        {
            // Invoke the logic on LocalizationManagerEditor
            // We can just use standard Reflection to invoke LocalizationManagerEditor's private method if we want,
            // or instantiate an editor instance and call it, or run its menu command if there is one.
            // Wait, does LocalizationManagerEditor have a TranslateDatabases() method that is private? Yes, lines 735-902.
            // Let's copy it or call it. We can just create a temporary instance of LocalizationManagerEditor or make the helper static.
            // Better yet, since we have the TranslateDatabases code, we can just adapt/call it or delegate it.
            // Wait, we can construct LocalizationManagerEditor or call the method since we have the code.
            // Let's create an editor target or just implement it directly in a static method or helper class!
            // Let's check how TranslateDatabases is structured. It uses Resources.Load, JsonUtility, WebClient.
            // It is very self-contained. Let's write the translation database logic cleanly here:
            
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
                            string langCode = LocalizationManagerEditor.GetGoogleLanguageCode(targetLang);

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
                            string langCode = LocalizationManagerEditor.GetGoogleLanguageCode(targetLang);

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
                            string langCode = LocalizationManagerEditor.GetGoogleLanguageCode(targetLang);

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

        private string TranslateString(System.Net.WebClient client, string source, string langCode)
        {
            if (string.IsNullOrEmpty(source)) return "";
            try
            {
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={langCode}&dt=t&q={System.Uri.EscapeDataString(source)}";
                string jsonResponse = client.DownloadString(url);
                return LocalizationManagerEditor.ParseTranslationJson(jsonResponse);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Database Translation Warning] Failed to translate '{source}' to {langCode}: {ex.Message}");
                return source;
            }
        }
    }
}
#endif
