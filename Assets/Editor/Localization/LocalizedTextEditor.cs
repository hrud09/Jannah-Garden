using UnityEngine;
using UnityEditor;

namespace JannahGarden.Localization
{
    [CustomEditor(typeof(LocalizedText))]
    public class LocalizedTextEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            LocalizedText script = (LocalizedText)target;

            // Check if standard Text or TextMeshPro components exist
            var textComp = script.GetComponent<UnityEngine.UI.Text>();
            var tmpComp = script.GetComponent<TMPro.TMP_Text>();

            if (textComp == null && tmpComp == null)
            {
                EditorGUILayout.HelpBox("Warning: No UnityEngine.UI.Text or TMPro.TMP_Text component found on this GameObject. LocalizedText requires one of these components to function.", MessageType.Error);
            }
            else
            {
                string componentType = textComp != null ? "UnityEngine.UI.Text" : "TMPro.TMP_Text";
                EditorGUILayout.HelpBox($"Linked to component: {componentType}", MessageType.None);
            }

            if (GUILayout.Button("Edit in Localization Window", GUILayout.Height(35)))
            {
                LocalizationEditorWindow.ShowWindowAndFocus(script);
            }
        }
    }
}
