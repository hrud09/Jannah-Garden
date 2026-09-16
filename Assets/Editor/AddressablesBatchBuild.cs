using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using Debug = UnityEngine.Debug;

/// <summary>
/// Batchmode entry point for rebuilding Addressables content, so the remote bundles in ServerData can be
/// regenerated from a script instead of by hand through the Addressables Groups window.
///
/// Builds for whichever platform is already active rather than switching targets itself — pass the
/// platform on Unity's own command line (-buildTarget iOS) and invoke this once per platform. Switching
/// the active target from inside an -executeMethod call kicks off an asset reimport and a domain reload
/// mid-method, which is a good way to end up with a half-written catalog.
///
/// Both entry points skip the rebuild entirely when Addressables reports no modified entries against the
/// last recorded addressables_content_state.bin for the active platform — that file is otherwise written
/// on every build and never read. "Build And Deploy" additionally skips `firebase deploy` when the build
/// was skipped. AddressablesPreBuildHook calls BuildAndDeployIfChanged automatically before every
/// Android/iOS player build, so this no longer needs to be run by hand at all.
/// </summary>
public static class AddressablesBatchBuild
{
    [MenuItem("Tools/Addressables/Build Content For Active Platform")]
    public static void BuildForActiveTarget()
    {
        try
        {
            BuildIfChanged();
        }
        catch (Exception e)
        {
            Fail(e.Message);
            return;
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    [MenuItem("Tools/Addressables/Build And Deploy For Active Platform")]
    public static void BuildAndDeployForActiveTarget()
    {
        try
        {
            BuildAndDeployIfChanged();
        }
        catch (Exception e)
        {
            Fail(e.Message);
            return;
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// <summary>
    /// Builds Addressables content for the active target if anything changed since the last build, then
    /// deploys to Firebase Hosting if it did. Throws on failure instead of handling it — callers decide
    /// how to surface that (log+exit here, abort the whole player build in AddressablesPreBuildHook).
    /// </summary>
    internal static void BuildAndDeployIfChanged()
    {
        bool changed = BuildIfChanged();
        if (changed)
            Deploy();
        else
            Debug.Log("[AddressablesBatchBuild] Skipping deploy — no content changes since last build.");
    }

    /// <returns>True if content was rebuilt, false if the build was skipped because nothing changed.</returns>
    internal static bool BuildIfChanged()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

        // Logged on its own line so a scripted run can read back which platform it actually built — the
        // caller needs this to know what the active target was before it started switching.
        Debug.Log($"[AddressablesBatchBuild] ACTIVE_TARGET={target}");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new Exception("No AddressableAssetSettings found — is Addressables initialised in this project?");

        // addressables_content_state.bin records the asset/dependency hashes from the last full build.
        // GatherModifiedEntries diffs the current asset state against it — the same check Addressables
        // itself uses to decide whether a content update build is needed — so a no-op re-run costs one
        // hash comparison instead of a full repack.
        string contentStatePath = ContentUpdateScript.GetContentStateDataPath(false, settings);
        if (File.Exists(contentStatePath))
        {
            var modified = ContentUpdateScript.GatherModifiedEntries(settings, contentStatePath);
            if (modified == null || modified.Count == 0)
            {
                Debug.Log($"[AddressablesBatchBuild] NO_CHANGES target={target}");
                return false;
            }
        }

        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

        if (!string.IsNullOrEmpty(result.Error))
            throw new Exception($"Build failed for {target}: {result.Error}");

        Debug.Log($"[AddressablesBatchBuild] BUILD_OK target={target} duration={result.Duration:F1}s");
        return true;
    }

    internal static void Deploy()
    {
        Debug.Log("[AddressablesBatchBuild] Content changed — deploying ServerData to Firebase Hosting...");

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // firebase on Windows resolves to a .cmd shim, which only runs through a shell.
            Arguments = "/c firebase deploy --only hosting",
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // Default concurrency (200 parallel upload streams) times out on this project's bundle count.
        psi.EnvironmentVariables["FIREBASE_HOSTING_UPLOAD_CONCURRENCY"] = "4";

        using (var process = new Process { StartInfo = psi })
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[firebase] {e.Data}"); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.LogWarning($"[firebase] {e.Data}"); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"firebase deploy exited with code {process.ExitCode}");

            Debug.Log("[AddressablesBatchBuild] DEPLOY_OK");
        }
    }

    private static void Fail(string message)
    {
        Debug.LogError($"[AddressablesBatchBuild] {message}");
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
