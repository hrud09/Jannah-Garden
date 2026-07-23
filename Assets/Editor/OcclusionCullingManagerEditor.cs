using UnityEngine;
using UnityEditor;

/// <summary>
/// Inspector for <see cref="OcclusionCullingManager"/>. Adds the one-click button
/// that collects every mesh in the scene and deactivates it, so the world can be
/// revealed progressively behind the loading screen at runtime.
/// </summary>
[CustomEditor(typeof(OcclusionCullingManager))]
public class OcclusionCullingManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var manager = (OcclusionCullingManager)target;

        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Scene Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Press the button below to gather every mesh in the scene and set it inactive. " +
            "At runtime the meshes are re-activated in batches behind the loading screen, " +
            "then the loading panel hides once they are all visible.",
            MessageType.Info);

        GUI.backgroundColor = new Color(0.35f, 0.75f, 0.5f);
        if (GUILayout.Button("Collect Scene Meshes & Deactivate", GUILayout.Height(34)))
        {
            manager.CollectAndDeactivateSceneMeshes();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Re-activate All Managed Meshes", GUILayout.Height(24)))
        {
            manager.ActivateAllInEditor();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Managed meshes: {manager.TotalCount}", EditorStyles.miniBoldLabel);
    }
}
