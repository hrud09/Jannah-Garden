using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
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
/// on every build and never read. "Build And Deploy" additionally skips the R2 upload when the build was
/// skipped *and* the last upload is known to have completed. AddressablesPreBuildHook calls
/// BuildAndDeployIfChanged automatically before every Android/iOS player build, so this no longer needs
/// to be run by hand at all.
/// </summary>
public static class AddressablesBatchBuild
{
    /// <summary>Upload script at the project root — see its header for the one-time R2/rclone setup.</summary>
    private const string DeployScriptName = "deploy-r2.ps1";

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
    /// uploads it to Cloudflare R2 if it did — or if the last upload never completed. Throws on failure
    /// instead of handling it — callers decide how to surface that (log+exit here, abort the whole player
    /// build in AddressablesPreBuildHook).
    /// </summary>
    internal static void BuildAndDeployIfChanged()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        bool changed = BuildIfChanged();
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

        // "Nothing changed" is not on its own a reason to skip the upload. A build whose deploy failed
        // still left addressables_content_state.bin updated by the build that preceded it, so every later
        // run reported NO_CHANGES and skipped the deploy as well — which is how bundles that were never
        // uploaded ended up shipping. The marker records what actually reached the bucket, so an upload
        // that failed gets retried rather than forgotten.
        if (!changed && DeployIsUpToDate(target, settings))
        {
            Debug.Log("[AddressablesBatchBuild] Skipping deploy — no content changes since the last successful upload.");
            return;
        }

        if (!changed)
            Debug.Log("[AddressablesBatchBuild] No content changes, but the last upload did not complete — re-deploying.");

        Deploy(target);
        RecordSuccessfulDeploy(target, settings);
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

    /// <summary>
    /// Uploads ServerData/&lt;target&gt; to the Cloudflare R2 bucket by running deploy-r2.ps1 from the project
    /// root. The script, not this method, is the implementation — so an upload that fails mid-build can be
    /// retried from a terminal without reopening Unity.
    /// </summary>
    internal static void Deploy(BuildTarget target)
    {
        if (target != BuildTarget.Android && target != BuildTarget.iOS)
            throw new Exception($"No remote content is hosted for {target} — only Android and iOS have bundles in ServerData.");

        Debug.Log($"[AddressablesBatchBuild] Uploading ServerData/{target} to Cloudflare R2...");

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string scriptPath = Path.Combine(projectRoot, DeployScriptName);
        if (!File.Exists(scriptPath))
            throw new Exception($"{DeployScriptName} is missing from the project root ({projectRoot}) — that script is what performs the upload.");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            // Bypass because the script is local and unsigned; NoProfile so a user profile can't write
            // anything into the stream being read back as deploy output.
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -BuildTarget {target}",
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using (var process = new Process { StartInfo = psi })
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[r2] {e.Data}"); };
            // rclone writes its per-file log and transfer stats to stderr as a matter of course, so stderr
            // is not an error channel here — the exit code is. Warning on it would flag every normal upload.
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[r2] {e.Data}"); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"R2 deploy exited with code {process.ExitCode} — see the [r2] lines above.");

            Debug.Log("[AddressablesBatchBuild] DEPLOY_OK");
        }
    }

    /// <summary>
    /// Where the fingerprint of the last successfully uploaded content state is kept, per platform. It
    /// lives under Library/ because it describes this machine's ServerData rather than the project: if
    /// Library is wiped the marker goes with it and the next build re-uploads, which is the harmless
    /// direction to fail in since rclone skips objects that are already in the bucket.
    /// </summary>
    private static string DeployMarkerPath(BuildTarget target)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, "Library", "com.unity.addressables", $"r2-deploy-{target}.txt");
    }

    private static string ContentStateFingerprint(AddressableAssetSettings settings)
    {
        string contentStatePath = ContentUpdateScript.GetContentStateDataPath(false, settings);
        if (!File.Exists(contentStatePath))
            return null;

        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(contentStatePath))
            return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static bool DeployIsUpToDate(BuildTarget target, AddressableAssetSettings settings)
    {
        string fingerprint = ContentStateFingerprint(settings);
        if (fingerprint == null)
            return false;

        string markerPath = DeployMarkerPath(target);
        if (!File.Exists(markerPath))
            return false;

        return string.Equals(File.ReadAllText(markerPath).Trim(), fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private static void RecordSuccessfulDeploy(BuildTarget target, AddressableAssetSettings settings)
    {
        string fingerprint = ContentStateFingerprint(settings);
        if (fingerprint == null)
            return;

        string markerPath = DeployMarkerPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
        File.WriteAllText(markerPath, fingerprint);
    }

    private static void Fail(string message)
    {
        Debug.LogError($"[AddressablesBatchBuild] {message}");
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
