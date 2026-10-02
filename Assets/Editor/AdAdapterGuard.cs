using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Fails the build if a forbidden LevelPlay ad adapter is installed.
///
/// Jannah Garden ships inside the Amal Flutter app (<c>com.ibadah.amal</c>) as one APK. The host already
/// ships Google's ad SDK through its own <c>google_mobile_ads</c> plugin, so installing LevelPlay's AdMob
/// adapter here puts two copies of <c>com.google.android.gms.ads.*</c> into one APK and Gradle refuses to
/// build it:
///
///   Execution failed for task ':app:checkReleaseDuplicateClasses'
///   Duplicate class com.google.android.gms.ads.AdError found in modules
///     ads-mobile-sdk-1.5.0.aar and play-services-ads-lite-23.6.0.aar
///
/// That is not a theoretical risk: it happened on 2026-10-01 and blocked the Flutter team for a day. The
/// failure surfaces in *their* build, not ours, which is exactly why it needs catching on this side.
///
/// AppLovin and Meta are refused for a different reason: the host app's privacy policy, app-ads.txt and
/// Play Store Data safety declaration list only Unity Ads, InMobi and ironSource. Shipping an undeclared
/// network SDK is a policy violation and an account risk, even though it builds cleanly.
///
/// The adapters are installed and removed through LevelPlay's Network Manager, which is a few clicks and
/// carries no warning about any of this — hence a build-time guard rather than a comment.
/// </summary>
public class AdAdapterGuard : IPreprocessBuildWithReport
{
    // Ahead of the Addressables hook: there is no point building and uploading content for a player
    // build that is going to be rejected anyway.
    public int callbackOrder => -20000;

    private const string LevelPlayEditorDir = "Assets/LevelPlay/Editor";

    /// <summary>
    /// Dependency-XML file name → why that adapter is refused. The file name is what LevelPlay's Network
    /// Manager writes when an adapter is installed, and deleting it is what uninstalls the adapter.
    /// </summary>
    private static readonly Dictionary<string, string> ForbiddenAdapters = new()
    {
        ["ISAdMobAdapterDependencies.xml"] =
            "AdMob — pulls in ads-mobile-sdk, whose com.google.android.gms.ads.* classes collide with the "
            + "play-services-ads-lite that the Flutter host's google_mobile_ads already ships. Breaks the "
            + "host's Release build at :app:checkReleaseDuplicateClasses.",

        ["ISAppLovinAdapterDependencies.xml"] =
            "AppLovin — not declared in the host app's privacy policy, app-ads.txt or Play Data safety "
            + "declaration.",

        ["ISFacebookAdapterDependencies.xml"] =
            "Meta / Audience Network — not declared in the host app's privacy policy, app-ads.txt or Play "
            + "Data safety declaration.",
    };

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android && report.summary.platform != BuildTarget.iOS)
            return;

        List<string> offenders = ForbiddenAdapters
            .Where(entry => File.Exists(Path.Combine(LevelPlayEditorDir, entry.Key)))
            .Select(entry => $"  • {entry.Key}\n      {entry.Value}")
            .ToList();

        if (offenders.Count == 0) return;

        throw new BuildFailedException(
            $"[AdAdapterGuard] {offenders.Count} forbidden LevelPlay ad adapter(s) are installed. This "
            + "build would break or endanger the Amal Flutter app it ships inside.\n\n"
            + string.Join("\n", offenders)
            + "\n\nTo fix: LevelPlay → Network Manager → uninstall those adapters (or delete the files "
            + $"above from {LevelPlayEditorDir}), then run\n"
            + "  Assets → External Dependency Manager → Android Resolver → Force Resolve\n"
            + "and confirm Assets/Plugins/Android/mainTemplate.gradle no longer mentions them.\n\n"
            + "Do NOT work around this with a Gradle 'exclude' — the adapter calls into the classes you "
            + "would be excluding, so the build passes and the app crashes at runtime instead.\n"
            + "Keep only: Unity Ads, InMobi, ironSource. See Assets/Scripts/Services/MONETIZATION.md.");
    }

    /// <summary>
    /// Same check, on demand, for when you want to verify before kicking off a long IL2CPP export rather
    /// than finding out at the end of it.
    /// </summary>
    [MenuItem("Jannah/Ads/Check Ad Adapters")]
    private static void CheckFromMenu()
    {
        List<string> offenders = ForbiddenAdapters
            .Where(entry => File.Exists(Path.Combine(LevelPlayEditorDir, entry.Key)))
            .Select(entry => $"{entry.Key}\n{entry.Value}")
            .ToList();

        if (offenders.Count == 0)
        {
            string installed = Directory.Exists(LevelPlayEditorDir)
                ? string.Join("\n", Directory.GetFiles(LevelPlayEditorDir, "*Dependencies.xml")
                    .Select(Path.GetFileName))
                : "(no LevelPlay Editor folder)";

            EditorUtility.DisplayDialog(
                "Ad adapters OK",
                "No forbidden ad adapter is installed.\n\nCurrently installed:\n" + installed,
                "OK");
            return;
        }

        Debug.LogError("[AdAdapterGuard] Forbidden ad adapters installed:\n" + string.Join("\n\n", offenders));
        EditorUtility.DisplayDialog(
            "Forbidden ad adapters installed",
            string.Join("\n\n", offenders) + "\n\nSee the Console for the full fix instructions.",
            "OK");
    }
}
