using UnityEngine;

public static class ScreenOrientationInitializer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeScreenOrientation()
    {
        // Force the orientation to landscape immediately on startup
        Screen.orientation = ScreenOrientation.LandscapeLeft;

        // Configure auto-rotation permissions (only landscape orientations)
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        // Apply auto-rotation rules restricted to the allowed orientations above
        Screen.orientation = ScreenOrientation.AutoRotation;

        Debug.Log("[ScreenOrientationInitializer] Automatically rotated and locked screen to Landscape orientations.");
    }
}
