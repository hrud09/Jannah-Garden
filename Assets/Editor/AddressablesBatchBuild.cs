using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// Batchmode entry point for rebuilding Addressables content, so the remote bundles in ServerData can be
/// regenerated from a script instead of by hand through the Addressables Groups window.
///
/// Builds for whichever platform is already active rather than switching targets itself — pass the
/// platform on Unity's own command line (-buildTarget iOS) and invoke this once per platform. Switching
/// the active target from inside an -executeMethod call kicks off an asset reimport and a domain reload
/// mid-method, which is a good way to end up with a half-written catalog.
/// </summary>
public static class AddressablesBatchBuild
{
    [MenuItem("Tools/Addressables/Build Content For Active Platform")]
    public static void BuildForActiveTarget()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

        // Logged on its own line so a scripted run can read back which platform it actually built — the
        // caller needs this to know what the active target was before it started switching.
        Debug.Log($"[AddressablesBatchBuild] ACTIVE_TARGET={target}");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Fail("No AddressableAssetSettings found — is Addressables initialised in this project?");
            return;
        }

        try
        {
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Fail($"Build failed for {target}: {result.Error}");
                return;
            }

            Debug.Log($"[AddressablesBatchBuild] BUILD_OK target={target} duration={result.Duration:F1}s");
        }
        catch (Exception e)
        {
            Fail($"Build threw for {target}: {e}");
            return;
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void Fail(string message)
    {
        Debug.LogError($"[AddressablesBatchBuild] {message}");
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
