using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using JannahGarden.Localization;

public class MCQEditorWindow : EditorWindow
{
    private TextAsset questionsAsset;
    private QuestionList questionDatabase;
    private Vector2 leftScrollPos;
    private Vector2 rightScrollPos;
    private int selectedQuestionIndex = -1;

    [MenuItem("Window/Jannah Garden/MCQ Editor")]
    public static void ShowWindow()
    {
        GetWindow<MCQEditorWindow>("MCQ Editor");
    }

    private void OnGUI()
    {
        GUILayout.Label("MCQ Database Manager", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        questionsAsset = (TextAsset)EditorGUILayout.ObjectField("Questions JSON File", questionsAsset, typeof(TextAsset), false);
        
        if (GUILayout.Button("Load", GUILayout.Width(80)))
        {
            LoadQuestions();
        }
        EditorGUILayout.EndHorizontal();

        if (questionDatabase == null || questionDatabase.questions == null)
        {
            EditorGUILayout.HelpBox("Please load a valid questions.txt JSON file.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();

        // Left Panel - List of Questions
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(250));
        leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos);
        
        for (int i = 0; i < questionDatabase.questions.Length; i++)
        {
            string label = $"Question {i + 1}";
            // Try to use english question text as label if available
            var engTrans = questionDatabase.questions[i].GetTranslation(Language.English);
            if (engTrans != null && !string.IsNullOrEmpty(engTrans.questionText))
            {
                label = engTrans.questionText;
                if (label.Length > 25) label = label.Substring(0, 25) + "...";
            }

            GUIStyle btnStyle = (selectedQuestionIndex == i) ? EditorStyles.selectionRect : GUI.skin.button;
            if (GUILayout.Button(label, btnStyle))
            {
                selectedQuestionIndex = i;
                GUI.FocusControl(null); // Clear focus when changing selection
            }
        }

        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Add New Question", GUILayout.Height(30)))
        {
            AddNewQuestion();
        }

        EditorGUILayout.EndVertical();

        // Right Panel - Edit Selected Question
        EditorGUILayout.BeginVertical(GUI.skin.box);
        rightScrollPos = EditorGUILayout.BeginScrollView(rightScrollPos);

        if (selectedQuestionIndex >= 0 && selectedQuestionIndex < questionDatabase.questions.Length)
        {
            DrawQuestionEditor(questionDatabase.questions[selectedQuestionIndex]);
        }
        else
        {
            GUILayout.Label("Select a question to edit.", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.EndScrollView();
        
        GUILayout.FlexibleSpace();
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save to File", GUILayout.Height(40)))
        {
            SaveQuestions();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawQuestionEditor(QuestionData data)
    {
        GUILayout.Label("Question Settings", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Correct Answer Index (0-3): ", GUILayout.Width(180));
        data.correctAnswerIndex = EditorGUILayout.IntSlider(data.correctAnswerIndex, 0, 3);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        GUILayout.Label("Translations", EditorStyles.boldLabel);

        if (data.translations == null)
        {
            data.translations = new LocalizedQuestionData[0];
        }

        for (int i = 0; i < data.translations.Length; i++)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Language: ", GUILayout.Width(70));
            data.translations[i].language = (Language)EditorGUILayout.EnumPopup(data.translations[i].language);
            
            if (data.translations[i].language != Language.English)
            {
                if (GUILayout.Button("Auto-Translate", GUILayout.Width(120)))
                {
                    AutoTranslateBlock(data, i);
                }
            }

            if (GUILayout.Button("Remove", GUILayout.Width(70)))
            {
                RemoveTranslation(data, i);
                return; // Break out of loop since array changed
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Question Text:");
            data.translations[i].questionText = EditorGUILayout.TextArea(data.translations[i].questionText, GUILayout.Height(50));

            EditorGUILayout.LabelField("Options:");
            if (data.translations[i].options == null || data.translations[i].options.Length != 4)
            {
                data.translations[i].options = new string[4];
            }

            EditorGUI.indentLevel++;
            for (int opt = 0; opt < 4; opt++)
            {
                data.translations[i].options[opt] = EditorGUILayout.TextField($"Option {opt}", data.translations[i].options[opt]);
            }
            EditorGUI.indentLevel--;
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        if (GUILayout.Button("Add Translation", GUILayout.Width(150)))
        {
            AddTranslation(data);
        }

        EditorGUILayout.Space(20);
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Delete This Question", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Confirm Delete", "Are you sure you want to delete this question?", "Yes", "Cancel"))
            {
                RemoveQuestion(selectedQuestionIndex);
                selectedQuestionIndex = -1;
            }
        }
        GUI.backgroundColor = Color.white;
    }

    private void LoadQuestions()
    {
        if (questionsAsset != null)
        {
            try
            {
                questionDatabase = JsonUtility.FromJson<QuestionList>(questionsAsset.text);
                selectedQuestionIndex = -1;
                Debug.Log($"Loaded {questionDatabase.questions.Length} questions successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error loading questions: {ex.Message}");
            }
        }
    }

    private void SaveQuestions()
    {
        if (questionsAsset == null || questionDatabase == null) return;

        string path = AssetDatabase.GetAssetPath(questionsAsset);
        string json = JsonUtility.ToJson(questionDatabase, true);
        
        System.IO.File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        EditorUtility.SetDirty(questionsAsset);
        AssetDatabase.Refresh();
        
        Debug.Log("Questions saved successfully.");
    }

    private void AddNewQuestion()
    {
        var list = new List<QuestionData>(questionDatabase.questions ?? new QuestionData[0]);
        var newQ = new QuestionData();
        newQ.translations = new LocalizedQuestionData[1] { 
            new LocalizedQuestionData { 
                language = Language.English, 
                options = new string[4] 
            } 
        };
        list.Add(newQ);
        questionDatabase.questions = list.ToArray();
        selectedQuestionIndex = list.Count - 1;
    }

    private void RemoveQuestion(int index)
    {
        var list = new List<QuestionData>(questionDatabase.questions);
        list.RemoveAt(index);
        questionDatabase.questions = list.ToArray();
    }

    private void AddTranslation(QuestionData data)
    {
        var list = new List<LocalizedQuestionData>(data.translations);
        list.Add(new LocalizedQuestionData { options = new string[4] });
        data.translations = list.ToArray();
    }

    private void RemoveTranslation(QuestionData data, int index)
    {
        var list = new List<LocalizedQuestionData>(data.translations);
        list.RemoveAt(index);
        data.translations = list.ToArray();
    }

    private void AutoTranslateBlock(QuestionData data, int index)
    {
        LocalizedQuestionData targetData = data.translations[index];
        LocalizedQuestionData engData = data.GetTranslation(Language.English);

        if (engData == null)
        {
            EditorUtility.DisplayDialog("Error", "No English translation exists for this question. Auto-translate relies on the English text as the source.", "OK");
            return;
        }

        string langCode = LocalizationManagerEditor.GetGoogleLanguageCode(targetData.language);
        if (string.IsNullOrEmpty(langCode))
        {
            EditorUtility.DisplayDialog("Error", "Unsupported language for auto-translation.", "OK");
            return;
        }

        try
        {
            using (var client = new System.Net.WebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;
                
                EditorUtility.DisplayProgressBar("Translating", $"Translating to {targetData.language}...", 0.3f);
                targetData.questionText = LocalizationManagerEditor.TranslateString(client, engData.questionText, langCode);
                
                if (targetData.options == null || targetData.options.Length != 4)
                {
                    targetData.options = new string[4];
                }

                for (int i = 0; i < 4; i++)
                {
                    EditorUtility.DisplayProgressBar("Translating", $"Translating options to {targetData.language}...", 0.3f + (0.6f * (i / 4f)));
                    if (engData.options != null && engData.options.Length > i)
                    {
                        targetData.options[i] = LocalizationManagerEditor.TranslateString(client, engData.options[i], langCode);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Translation failed: {ex.Message}");
            EditorUtility.DisplayDialog("Error", "Translation failed. Check the console for details.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            GUI.FocusControl(null); // Deselect text fields so updates are visible
        }
    }
}
