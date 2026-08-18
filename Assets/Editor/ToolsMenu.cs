using UnityEngine;
using UnityEditor;

public static class ToolsMenu
{
    [MenuItem("Tools/Data/Clear All Saved Data")]
    public static void ClearAllSavedData()
    {
        // Clear all binary save files and PlayerPrefs
        SaveSystem.ClearAll();

        // Show editor confirmation popup
        EditorUtility.DisplayDialog(
            "Clear Saved Data",
            "All PlayerPrefs and saved item placement data have been cleared successfully.",
            "OK"
        );

        Debug.Log("All saved placement data has been cleared.");
    }

    [MenuItem("Tools/Data/Reset Onboarding Tutorial")]
    public static void ResetOnboardingTutorial()
    {
        GameOnboardingManager.ResetForTesting();

        EditorUtility.DisplayDialog(
            "Reset Onboarding Tutorial",
            "The onboarding tutorial will play again from the start next time you press Play. Your placed garden and other saved data were left untouched.",
            "OK"
        );

        Debug.Log("Onboarding tutorial stage reset to NotStarted.");
    }
}
