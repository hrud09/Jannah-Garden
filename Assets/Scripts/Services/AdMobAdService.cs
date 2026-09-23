using System;
using GoogleMobileAds.Api;
using UnityEngine;

/// <summary>
/// Rewarded ads via the Unity-side Google Mobile Ads SDK, using Google's public test ad unit.
///
/// Android only. Do not point this at iOS: <see cref="AdsManager"/> documents a prior crash caused by
/// running a Unity-side ad SDK alongside Flutter's own google_mobile_ads on iOS. iOS keeps going through
/// <see cref="FlutterAdService"/> instead.
/// </summary>
public class AdMobAdService : IAdService
{
    private static AdMobAdService _instance;

    /// <summary>Lazily created on first use so the SDK isn't initialised until a treasure box actually needs it.</summary>
    public static AdMobAdService Instance => _instance ??= new AdMobAdService();

    // Google's public test rewarded ad unit ID for Android — always fills, never real revenue.
    // https://developers.google.com/admob/unity/test-ads
    private const string TestRewardedAdUnitId = "ca-app-pub-3940256099942544/5224354917";

    private RewardedAd _rewardedAd;
    private bool _rewardEarned;
    private Action<bool> _pendingCallback;

    public bool IsAdReady => _rewardedAd != null && _rewardedAd.CanShowAd();

    private AdMobAdService()
    {
        MobileAds.Initialize(_ => LoadAd());
    }

    private void LoadAd()
    {
        if (_rewardedAd != null)
        {
            _rewardedAd.Destroy();
            _rewardedAd = null;
        }

        RewardedAd.Load(TestRewardedAdUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning($"[AdMobAdService] Rewarded ad failed to load: {error}");
                return;
            }

            _rewardedAd = ad;
            RegisterEventHandlers(ad);
        });
    }

    private void RegisterEventHandlers(RewardedAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            bool earned = _rewardEarned;
            _rewardEarned = false;

            Action<bool> callback = _pendingCallback;
            _pendingCallback = null;

            LoadAd(); // Pre-load the next one so the box after this one isn't left waiting.
            callback?.Invoke(earned);
        };

        ad.OnAdFullScreenContentFailed += error =>
        {
            Debug.LogWarning($"[AdMobAdService] Rewarded ad failed to show: {error}");

            Action<bool> callback = _pendingCallback;
            _pendingCallback = null;

            LoadAd();
            callback?.Invoke(false);
        };
    }

    public void ShowRewardedAd(Action<bool> onComplete)
    {
        if (!IsAdReady)
        {
            Debug.LogWarning("[AdMobAdService] ShowRewardedAd called with no ad loaded — no reward.");
            onComplete?.Invoke(false);
            return;
        }

        _pendingCallback = onComplete;
        _rewardEarned = false;

        _rewardedAd.Show(_ => { _rewardEarned = true; });
    }
}
