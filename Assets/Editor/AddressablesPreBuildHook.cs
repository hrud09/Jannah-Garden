using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Runs AddressablesBatchBuild.BuildAndDeployIfChanged automatically before every Android/iOS player
/// build, so the manual "Build And Deploy For Active Platform" menu step is no longer needed — an
/// APK/IPA can no longer ship a catalog pointing at remote bundles that were never uploaded.
///
/// Skips the rebuild (and the upload) when Addressables reports no content changes since the last build
/// and the last upload completed. Aborts the whole player build if the Addressables build or the R2
/// upload fails, since shipping anyway would mean a build referencing bundles that don't exist, or
/// are stale, on the server.
/// </summary>
public class AddressablesPreBuildHook : IPreprocessBuildWithReport
{
    public int callbackOrder => -10000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android && report.summary.platform != BuildTarget.iOS)
            return;

        Debug.Log($"[AddressablesPreBuildHook] Checking Addressable content before building for {report.summary.platform}...");

        try
        {
            AddressablesBatchBuild.BuildAndDeployIfChanged();
        }
        catch (Exception e)
        {
            throw new BuildFailedException($"[AddressablesPreBuildHook] {e.Message}");
        }
    }
}
