using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batchmode entry point for the one-time export-size fix: strip the scene-baked Shop Item dressing
/// into <see cref="PrePlacedLayoutData"/> (see PrePlacedLayoutBaker) and move the Outer Garden scene
/// to the remote Addressables group (see MoveOuterGardenToRemote). Both steps are
/// platform-independent, so run this once; then rebuild Addressables and re-export the player per
/// platform with the existing AddressablesBatchBuild / ProjectExporterBatchmode entry points:
///
/// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod JannahGardenSizeFixBatch.BakeAndMove -quit
/// </summary>
public static class JannahGardenSizeFixBatch
{
    private const string ScenePath = "Assets/Scenes/Jannah Garden.unity";

    public static void BakeAndMove()
    {
        bool ok = false;
        try
        {
            ok = PrePlacedLayoutBaker.BakeScenePath(ScenePath) && MoveOuterGardenToRemote.MoveInternal();
        }
        catch (Exception e)
        {
            Debug.LogError($"[JannahGardenSizeFixBatch] Threw: {e}");
        }

        Debug.Log(ok ? "[JannahGardenSizeFixBatch] ALL_OK" : "[JannahGardenSizeFixBatch] FAILED");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
