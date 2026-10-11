using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-call Android APK builds, so producing a sideloadable test build doesn't mean clicking through
/// the Build Settings dialog and remembering which toggles to put back afterwards.
///
/// Builds go to Builds/Android/ (gitignored) and always use the project's shipping configuration —
/// IL2CPP, Managed Stripping Level "Medium", release (non-development). That matters: the
/// Bengali-locale shop failure this project hit only existed in a stripped release player and was
/// invisible in the Editor, so a "quick" development build is not a valid test of it.
///
/// <see cref="AddressablesPreBuildHook"/> still runs ahead of every build here, so the APK can't ship a
/// catalog pointing at bundles that were never uploaded.
/// </summary>
public static class AndroidApkBuilder
{
    private const string OutputDirectory = "Builds/Android";

    [MenuItem("Tools/Build/Android APK (all architectures)")]
    public static void BuildAllArchitectures() => Build(AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64, "all");

    /// <summary>
    /// ARM64 only — roughly halves IL2CPP time versus a universal build, and every 64-bit-only device
    /// (Pixel 6 and anything else from the last several years) runs it. Not for store uploads: Play
    /// still expects the 32-bit slice for older hardware. Only the native-code slice differs; managed
    /// stripping, which is what most device-only bugs turn on, is identical to a universal build.
    /// </summary>
    [MenuItem("Tools/Build/Android APK (ARM64 only, device test)")]
    public static void BuildArm64() => Build(AndroidArchitecture.ARM64, "arm64");

    private static void Build(AndroidArchitecture architectures, string tag)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.LogError("[AndroidApkBuilder] Active build target is "
                + $"{EditorUserBuildSettings.activeBuildTarget}; switch to Android first.");
            return;
        }

        Directory.CreateDirectory(OutputDirectory);
        string outputPath = Path.Combine(OutputDirectory, $"JannahGarden-{PlayerSettings.bundleVersion}-{tag}.apk");

        // Restored in the finally below so a one-off test build never silently redefines what a later
        // store build produces.
        AndroidArchitecture previousArchitectures = PlayerSettings.Android.targetArchitectures;
        bool previousAppBundle = EditorUserBuildSettings.buildAppBundle;
        bool previousExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;

        try
        {
            PlayerSettings.Android.targetArchitectures = architectures;
            EditorUserBuildSettings.buildAppBundle = false;

            // This project normally EXPORTS a Gradle project rather than building an APK, because the
            // shipping artifact is a Unity library embedded in the Flutter host app. That export is a
            // directory, not something adb can install, so force a real APK here and put the setting
            // back afterwards. The resulting APK runs the game standalone — FlutterBridge simply never
            // receives a host message, which is exactly what you want when testing the in-game
            // language selector without the host overriding the locale.
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenePaths(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            Debug.Log($"[AndroidApkBuilder] Building {architectures} APK -> {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[AndroidApkBuilder] BUILD_SUCCEEDED path={outputPath} "
                    + $"size={summary.totalSize / (1024 * 1024)}MB time={summary.totalTime}");
            }
            else
            {
                Debug.LogError($"[AndroidApkBuilder] BUILD_FAILED result={summary.result} "
                    + $"errors={summary.totalErrors}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AndroidApkBuilder] BUILD_FAILED {e}");
        }
        finally
        {
            PlayerSettings.Android.targetArchitectures = previousArchitectures;
            EditorUserBuildSettings.buildAppBundle = previousAppBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExportProject;
        }
    }

    private static string[] EnabledScenePaths()
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled) scenes.Add(scene.path);
        return scenes.ToArray();
    }
}
