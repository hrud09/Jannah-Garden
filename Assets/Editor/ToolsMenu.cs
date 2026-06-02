using UnityEngine;
using UnityEditor;

public static class ToolsMenu
{
    [MenuItem("Tools/Clear All Saved Data")]
    public static void ClearAllSavedData()
    {
        // Delete all PlayerPrefs keys
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        // Show editor confirmation popup
        EditorUtility.DisplayDialog(
            "Clear Saved Data",
            "All PlayerPrefs and saved item placement data have been cleared successfully.",
            "OK"
        );

        Debug.Log("All saved placement data has been cleared.");
    }
}
