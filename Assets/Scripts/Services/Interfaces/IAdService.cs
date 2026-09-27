using System;

/// <summary>
/// Abstraction over a rewarded ad network.
/// Implement this interface with your chosen ad SDK (Unity Ads, AdMob, etc.)
/// and register the implementation with <see cref="TreasureBoxManager.AdService"/>.
///
/// A no-op stub (<see cref="NullAdService"/>) is used by default so the
/// system compiles and runs without any ad SDK installed.
/// </summary>
public interface IAdService
{
    /// <summary>
    /// Returns true if a rewarded ad is currently loaded and ready to show.
    /// </summary>
    bool IsAdReady { get; }

    /// <summary>
    /// Displays a rewarded ad to the player.
    /// </summary>
    /// <param name="onComplete">
    /// Callback invoked when the ad finishes. The bool parameter is
    /// <c>true</c> if the player earned the reward, <c>false</c> if they
    /// skipped or the ad failed to show.
    /// </param>
    void ShowRewardedAd(Action<bool> onComplete);
}

// ─────────────────────────────────────────────────────────────────────────────
// Default no-op stub
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Null-object implementation of <see cref="IAdService"/>.
/// Always reports ad as ready and immediately grants the reward without
/// showing any real ad. Replace with a real implementation at integration time.
/// </summary>
public class NullAdService : IAdService
{
    public bool IsAdReady => true;

    public void ShowRewardedAd(Action<bool> onComplete)
    {
        UnityEngine.Debug.Log("[NullAdService] ShowRewardedAd called — no real ad SDK. Granting reward immediately.");
        onComplete?.Invoke(true);
    }
}

#if UNITY_EDITOR

/// <summary>
/// Editor stand-in that plays the visible countdown panel owned by <see cref="AdsManager"/>.
///
/// LevelPlay cannot show its own mock ad in this project. <c>EditorRewardedAd</c> picks its prefab path
/// with <c>Directory.Exists("Packages/com.unity.services.levelplay")</c>, which is false for a registry
/// install (the package sits in <c>Library/PackageCache</c>), so it falls back to
/// <c>Assets/LevelPlay/Runtime/.../MockRewardedEditorAd.prefab</c> — a path that does not exist here,
/// because <c>Assets/LevelPlay</c> holds only the adapter dependency XMLs. Loading returns null and
/// constructing the ad throws.
///
/// Playing the game's own fake ad instead means the Editor shows something for the tester to watch,
/// rather than <see cref="NullAdService"/> silently handing out the reward with no ad at all.
/// </summary>
public class EditorFakeAdService : IAdService
{
    public bool IsAdReady => true;

    public void ShowRewardedAd(Action<bool> onComplete)
    {
        if (AdsManager.Instance == null)
        {
            UnityEngine.Debug.LogWarning("[EditorFakeAdService] No AdsManager in the scene — granting the "
                + "reward without showing a panel.");
            onComplete?.Invoke(true);
            return;
        }

        UnityEngine.Debug.Log("[EditorFakeAdService] Playing the fake rewarded ad panel.");
        AdsManager.Instance.ShowFakeRewardedAd(() => onComplete?.Invoke(true));
    }
}

#endif
