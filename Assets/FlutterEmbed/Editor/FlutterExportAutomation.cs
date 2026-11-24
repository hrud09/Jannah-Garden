using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dialog-free Android export to the Flutter <c>android/unityLibrary</c> layout, for driving the export
/// from a script or MCP. The stock menu item (<see cref="ProjectExporterMenuItem"/>) opens a folder picker
/// and confirm dialogs that nothing but a human can answer; this does the same checks and build as the
/// batchmode path (<c>ProjectExportChecker.PrepareExportDirectoryBatchmode</c>) inside the open editor.
///
/// The Addressables pre-build hook still runs first (it is an IPreprocessBuildWithReport), so changed
/// remote bundles are rebuilt and uploaded to R2 before the player build starts.
/// </summary>
public static class FlutterExportAutomation
{
    /// <summary>Where the last Android export went; reused by the menu item.</summary>
    public const string DefaultAndroidExportPath = "android/unityLibrary";

    [MenuItem("Flutter Embed/Export Android to <project>/android/unityLibrary (no dialogs)")]
    public static void ExportAndroidDefault() => ExportAndroid(Path.GetFullPath(DefaultAndroidExportPath));

    /// <summary>Exports to <paramref name="unityLibraryPath"/>, which must end in android/unityLibrary.
    /// Its existing contents are deleted first, exactly as the stock exporter does. Returns false (with
    /// an error logged) when a precondition fails; the build result itself is logged by the exporter.</summary>
    public static bool ExportAndroid(string unityLibraryPath)
    {
        var problems = new List<string>();
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) problems.Add("active build target is not Android");
        if (!EditorUserBuildSettings.exportAsGoogleAndroidProject) problems.Add("'Export project' is not ticked");
        var arch = PlayerSettings.Android.targetArchitectures;
        if (!arch.HasFlag(AndroidArchitecture.ARMv7) || !arch.HasFlag(AndroidArchitecture.ARM64)) problems.Add("target architectures must include ARMv7 and ARM64");
        if (PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP) problems.Add("scripting backend is not IL2CPP");

        var dir = new DirectoryInfo(unityLibraryPath);
        if (dir.Name != "unityLibrary" || dir.Parent == null || dir.Parent.Name != "android") problems.Add($"path must end in android/unityLibrary: {unityLibraryPath}");

        if (problems.Count > 0)
        {
            Debug.LogError("[FlutterExportAutomation] Export aborted: " + string.Join("; ", problems));
            return false;
        }

        BuildOptions options = BuildOptions.None;
        if (EditorUserBuildSettings.development) options |= BuildOptions.Development;
        if (EditorUserBuildSettings.connectProfiler) options |= BuildOptions.ConnectWithProfiler;
        if (EditorUserBuildSettings.allowDebugging) options |= BuildOptions.AllowDebugging;

        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            target = BuildTarget.Android,
            locationPathName = dir.FullName,
            options = options,
        };

        if (dir.Exists && Directory.GetFileSystemEntries(dir.FullName).Length != 0)
        {
            Debug.Log($"[FlutterExportAutomation] Deleting existing contents of {dir.FullName}");
            Directory.Delete(dir.FullName, true);
        }

        Debug.Log($"[FlutterExportAutomation] Exporting Android to {dir.FullName} ({buildPlayerOptions.scenes.Length} scenes, options {options})");
        new ProjectExporterAndroid().Export(buildPlayerOptions, new List<string>());
        return true;
    }
}
