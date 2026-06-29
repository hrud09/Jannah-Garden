using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RuntimeEnvironmentGenerator))]
public class RuntimeEnvironmentGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default fields (including our new serialized sceneAssets list)
        DrawDefaultInspector();

        RuntimeEnvironmentGenerator generator = (RuntimeEnvironmentGenerator)target;

        GUILayout.Space(15);
        GUI.backgroundColor = new Color(0.15f, 0.68f, 0.37f); // Premium Emerald Green matching project style
        if (GUILayout.Button("Assign All Assets From Scene", GUILayout.Height(35)))
        {
            generator.EditorAssignAllAssetsFromScene();
        }
        GUI.backgroundColor = Color.white;
    }
}
