using UnityEngine;
using UnityEditor;

public class TestJsonParsing
{
    [MenuItem("Tools/Misc/Test JSON")]
    public static void Test()
    {
        TextAsset ta = Resources.Load<TextAsset>("questions");
        if (ta == null) {
            Debug.LogError("Could not load questions.txt");
            return;
        }
        try {
            var allQuestions = JsonUtility.FromJson<QuestionList>(ta.text);
            if (allQuestions != null && allQuestions.questions != null) {
                Debug.Log("Successfully parsed " + allQuestions.questions.Length + " questions.");
                var q0 = allQuestions.questions[0];
                Debug.Log("Q0 text: " + q0.questionText);
                Debug.Log("Q0 options count: " + q0.options.Length);
            } else {
                Debug.LogError("Parsed object or questions array is null!");
            }
        } catch (System.Exception e) {
            Debug.LogError("Exception parsing JSON: " + e.Message);
        }
    }
}
