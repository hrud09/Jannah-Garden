using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LoadingScreenManager))]
public class LoadingScreenManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LoadingScreenManager manager = (LoadingScreenManager)target;

        GUILayout.Space(15);
        
        string buttonText = "Build Loading UI";
        if (manager.targetCanvas != null)
        {
            buttonText = "Build Loading UI Under Assigned Canvas";
            GUI.backgroundColor = new Color(0.18f, 0.49f, 0.72f); // Premium Slate Blue
        }
        else
        {
            GUI.backgroundColor = new Color(0.15f, 0.68f, 0.37f); // Premium Emerald Green
        }

        // Check if any UI references are already assigned
        bool hasReferences = manager.loadingPanel != null || manager.loadingCanvas != null ||
                             manager.backgroundImage != null || manager.progressBar != null ||
                             manager.percentText != null || manager.statusText != null;

        if (GUILayout.Button(buttonText, GUILayout.Height(35)))
        {
            if (hasReferences)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Overwrite UI References?",
                    "Some UI references are already assigned on this component. Overwriting will delete existing generated elements and recreate them. Are you sure you want to proceed?",
                    "Yes, Overwrite",
                    "Cancel"
                );
                if (!proceed)
                {
                    return;
                }
            }

            // Record Undo for building UI
            Undo.IncrementCurrentGroup();
            int groupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Loading UI");

            Undo.RecordObject(manager, "Build Loading Screen UI");
            
            // Build the UI elements
            manager.BuildUI();
            
            // Mark object and scene as dirty to ensure saving
            EditorUtility.SetDirty(manager);
            if (manager.targetCanvas != null)
            {
                EditorUtility.SetDirty(manager.targetCanvas.gameObject);
            }
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }

            Undo.CollapseUndoOperations(groupIndex);

            Debug.Log("[LoadingScreenManagerEditor] Successfully built Loading Screen UI.");
            EditorUtility.DisplayDialog(
                "Loading UI Built",
                manager.targetCanvas != null 
                    ? "Successfully generated loading screen UI hierarchy under target Canvas!\n\nAll references have been automatically assigned." 
                    : "Successfully generated loading screen canvas and UI hierarchy under LoadingScreenManager!\n\nAll references have been automatically assigned.",
                "OK"
            );
        }
        
        GUI.backgroundColor = Color.white;
    }
}
