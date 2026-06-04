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
