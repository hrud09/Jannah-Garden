using System;
using System.Collections;
using UnityEngine;
#if LEVELPLAY_ENABLED
// Written against Ads Mediation 9.5.1, verified against the package source rather than the docs — the
// online 8.x reference is wrong for this version in two places: the namespace moved out of
// `com.unity3d.mediation` in 8.7, and OnAdDisplayFailed gained a LevelPlayAdInfo parameter.
using Unity.Services.LevelPlay;
#endif

/// <summary>
/// Rewarded ads through Unity LevelPlay mediation (Unity Ads, Meta Audience Network, InMobi, and later
/// Mintegral). This is the game's only ad SDK.
///
/// WHY THIS REPLACED THE UNITY-SIDE ADMOB PLUGIN
/// ---------------------------------------------
/// <see cref="AdsManager"/> documents a crash: shipping google_mobile_ads inside UnityFramework while the
/// Flutter host also shipped it left the final iOS binary referencing native-ads symbols it never linked,
/// and dyld killed the app before Flutter's main() ran. The rule that came out of it was "no ad SDK in
/// Unity" — but the real constraint is narrower: no *second copy of the same* native SDK in one process.
///
/// LevelPlay's mediation list here contains no AdMob, so no adapter pulls in google_mobile_ads and the
/// duplicate-symbol condition does not arise. That is what makes owning the SDK on the Unity side safe
/// again. It stays safe only while the AdMob adapter is left out — adding it would recreate the crash.
///
/// COMPILING WITHOUT THE SDK
/// -------------------------
/// The LevelPlay package is not in the project yet, so the real implementation sits behind the
/// LEVELPLAY_ENABLED scripting define. Until that define is set this class reports "no ad" and refuses to
/// show one, and <see cref="AdServiceBootstrap"/> does not register it — the game keeps the
/// <see cref="NullAdService"/> behaviour it has today. See the class comment on the bootstrap for the
/// steps that turn it on.
/// </summary>
public class LevelPlayAdService : IAdService
{
    // ─── Network IDs ──────────────────────────────────────────────────────────
    // LevelPlay app keys and rewarded ad unit IDs, per store. These identify the *mediation* account;
    // the individual networks (Unity Ads, Meta, InMobi) are configured in the LevelPlay dashboard and
    // reach the app through adapters, not through IDs compiled in here.

#if UNITY_IOS
    private const string AppKey = "285c64b55";
    private const string RewardedAdUnitId = "jyzi8bfs2bbt15co";
#else
    private const string AppKey = "285c611cd";
    private const string RewardedAdUnitId = "cdsr8xxvqptsq70h";
#endif

    private static LevelPlayAdService _instance;

    /// <summary>Created by <see cref="AdServiceBootstrap"/>, which also drives initialisation.</summary>
    public static LevelPlayAdService Instance => _instance ??= new LevelPlayAdService();

    private LevelPlayAdService() { }

    /// <summary>
    /// Grant the reward anyway when no ad can be shown at all — the SDK never initialised, no network
    /// filled the request, or the ad failed to display. Keeps a broken or unsold ad slot from locking the
    /// player out of treasure boxes.
    ///
    /// This covers "we could not show you an ad", NOT "you skipped the ad". A player who opens an ad and
    /// closes it early still earns nothing, because granting there would make every ad skippable for a
    /// free reward and the ad unit would never earn anything.
    ///
    /// Set from <see cref="AdServiceBootstrap"/>. Turn it off once ads reliably fill, or the game keeps
    /// paying out for impressions it never served.
    /// </summary>
    public bool GrantRewardWhenAdUnavailable { get; set; }

#if LEVELPLAY_ENABLED

    private LevelPlayRewardedAd _rewardedAd;
    private bool _initStarted;

    /// <summary>Backoff between load retries, multiplied by the consecutive-failure count.</summary>
    private const float LoadRetryDelaySeconds = 10f;

    /// <summary>Stop retrying after this many failures in a row so a dead ad unit doesn't retry forever.</summary>
    private const int MaxConsecutiveLoadFailures = 5;

    private int _consecutiveLoadFailures;
    private bool _retryScheduled;

    /// <summary>
    /// Something alive in the scene to run a one-frame wait on — this class is not a MonoBehaviour.
    /// Supplied by <see cref="AdServiceBootstrap"/>; see <see cref="DeferredSettleIfUnrewarded"/>.
    /// </summary>
    private MonoBehaviour _coroutineRunner;

    /// <summary>
    /// Set by OnAdRewarded. Only read by the close path, to tell "closed after earning" from "closed
    /// early", since the close event itself carries no outcome.
    /// </summary>
    private bool _rewardEarned;

    /// <summary>
    /// The in-flight caller's callback. Held rather than passed through because the result arrives on a
    /// LevelPlay event, not a return value.
    /// </summary>
    private Action<bool> _pendingCallback;

    public bool IsAdReady => _rewardedAd != null && _rewardedAd.IsAdReady();

    /// <summary>
    /// Starts the SDK and pre-loads the first rewarded ad. Safe to call more than once; only the first
    /// call does anything.
    /// </summary>
    /// <param name="consentGranted">
    /// The player's GDPR consent decision. Must be the answer from the host's consent flow, not a
    /// default — see <see cref="AdServiceBootstrap"/> for why this is still an open wire.
    /// </param>
    /// <param name="enableTestMode">
    /// Turns on adapter debug logging and runs LevelPlay's integration check. Both write their findings
    /// to logcat, which is the only way to see *why* a network returns no fill — a missing adapter, a
    /// bad app key, or a device the dashboard has not been told to treat as a test device. Leave off for
    /// release builds; the logging is noisy and reveals the mediation setup.
    /// </param>
    public void Initialize(bool consentGranted, MonoBehaviour coroutineRunner = null, bool enableTestMode = false)
    {
        _coroutineRunner = coroutineRunner;

        if (_initStarted) return;
        _initStarted = true;

        // Set before Init so the first ad request already carries the right consent state.
        LevelPlay.SetConsent(consentGranted);

        if (enableTestMode) LevelPlay.SetAdaptersDebug(true);

        LevelPlay.OnInitSuccess += OnInitSuccess;
        LevelPlay.OnInitFailed += error =>
            Debug.LogError($"[LevelPlayAdService] LevelPlay failed to initialise: {error}");

        if (enableTestMode)
        {
            // Only meaningful once the SDK is up, so hang it off the success event rather than calling
            // it straight after Init.
            LevelPlay.OnInitSuccess += _ => LevelPlay.ValidateIntegration();
        }

        LevelPlay.Init(AppKey);
    }

    private void OnInitSuccess(LevelPlayConfiguration configuration)
    {
        try
        {
            _rewardedAd = new LevelPlayRewardedAd(RewardedAdUnitId);
        }
        catch (Exception e)
        {
            // In the Editor, LevelPlay's mock ad resolves its prefab from Assets/LevelPlay/... whenever the
            // package is not embedded under Packages/. A registry (PackageCache) install leaves that folder
            // holding only the dependency XMLs, so the prefab is missing and constructing the ad throws.
            // Swallowing it here keeps the exception from unwinding into AdServiceBootstrap.Start().
            Debug.LogError($"[LevelPlayAdService] Could not create the rewarded ad: {e.Message}");
            return;
        }

        _rewardedAd.OnAdLoaded += adInfo =>
        {
            _consecutiveLoadFailures = 0;
            Debug.Log("[LevelPlayAdService] Rewarded ad loaded and ready to show.");
        };

        _rewardedAd.OnAdLoadFailed += error =>
        {
            Debug.LogWarning($"[LevelPlayAdService] Rewarded ad failed to load: {error}");
            ScheduleLoadRetry();
        };

        // The reward event is authoritative: seeing it at all means the player earned the reward, so
        // settle immediately rather than waiting for the close event.
        _rewardedAd.OnAdRewarded += (adInfo, reward) =>
        {
            _rewardEarned = true;
            Settle(true);
        };

        _rewardedAd.OnAdDisplayFailed += (adInfo, error) =>
        {
            // The player asked for an ad and the SDK could not put one on screen — that is our failure,
            // not a skip, so it follows the same bypass as an unfilled slot.
            Debug.LogWarning($"[LevelPlayAdService] Rewarded ad failed to display: {error}");
            Settle(GrantRewardWhenAdUnavailable);
        };

        // Closing settles as "not earned" only if the reward event never arrived — and only after a frame.
        // The two events are not ordered consistently: the real SDK sends rewarded-then-closed, but the
        // Editor mock's HideAd() sends closed-then-rewarded in the same call stack. Settling here
        // immediately would consume the callback with earned=false a line before the reward arrives, which
        // is exactly why watching an ad in the Editor left the treasure box shut.
        _rewardedAd.OnAdClosed += adInfo => DeferredSettleIfUnrewarded();

        _rewardedAd.LoadAd();
    }

    /// <summary>
    /// Re-requests an ad after a failed load.
    ///
    /// A load failure used to be terminal. The only <c>LoadAd</c> retry lived in
    /// <see cref="ShowRewardedAd"/>, but <see cref="TreasureBoxManager.TryOpenBox"/> tested
    /// <see cref="IsAdReady"/> first and returned early, so that retry was unreachable. A single failed
    /// load at startup therefore left <see cref="IsAdReady"/> false for the whole session and no treasure
    /// box could ever play an ad.
    /// </summary>
    private void ScheduleLoadRetry()
    {
        if (_rewardedAd == null || _coroutineRunner == null || _retryScheduled) return;

        if (_consecutiveLoadFailures >= MaxConsecutiveLoadFailures)
        {
            Debug.LogError($"[LevelPlayAdService] Rewarded ad failed to load {_consecutiveLoadFailures} "
                + "times in a row — giving up for this session. Check the ad unit ID and that the "
                + "LevelPlay dashboard has this device registered for test ads.");
            return;
        }

        _consecutiveLoadFailures++;
        _retryScheduled = true;
        _coroutineRunner.StartCoroutine(RetryLoadAfterDelay(LoadRetryDelaySeconds * _consecutiveLoadFailures));
    }

    private IEnumerator RetryLoadAfterDelay(float delay)
    {
        // Realtime: an ad that paused the game leaves Time.timeScale at 0, which would stall a scaled wait.
        yield return new WaitForSecondsRealtime(delay);

        _retryScheduled = false;
        if (_rewardedAd != null && !_rewardedAd.IsAdReady()) _rewardedAd.LoadAd();
    }

    /// <summary>
    /// Waits one frame, then settles as unrewarded — unless a reward event landed in the meantime and
    /// already settled, in which case <see cref="Settle"/> finds no pending callback and does nothing.
    /// </summary>
    private void DeferredSettleIfUnrewarded()
    {
        if (_pendingCallback == null) return;

        if (_coroutineRunner == null)
        {
            // No runner to wait a frame on. Settling on the flag as-is is still correct whenever the SDK
            // sends rewarded before closed, which is what it does on device.
            Settle(_rewardEarned);
            return;
        }

        _coroutineRunner.StartCoroutine(SettleNextFrame());
    }

    private IEnumerator SettleNextFrame()
    {
        yield return null;
        if (_pendingCallback != null) Settle(_rewardEarned);
    }

    public void ShowRewardedAd(Action<bool> onComplete)
    {
        if (_pendingCallback != null)
        {
            // A second show while one is on screen would overwrite the first caller's callback and strand
            // it — the treasure box that asked for the ad would never hear back.
            Debug.LogWarning("[LevelPlayAdService] A rewarded ad is already in flight — ignoring this request.");
            onComplete?.Invoke(false);
            return;
        }

        if (!IsAdReady)
        {
            // Request one for the next attempt, so a slot that is merely cold recovers by itself.
            if (_rewardedAd != null) _rewardedAd.LoadAd();
            else Debug.LogError("[LevelPlayAdService] No rewarded ad object — LevelPlay never initialised.");

            if (GrantRewardWhenAdUnavailable)
            {
                Debug.LogWarning("[LevelPlayAdService] No ad available to show — granting the reward anyway.");
                onComplete?.Invoke(true);
                return;
            }

            Debug.LogWarning("[LevelPlayAdService] ShowRewardedAd called with no ad loaded — no reward.");
            onComplete?.Invoke(false);
            return;
        }

        _rewardEarned = false;
        _pendingCallback = onComplete;
        _rewardedAd.ShowAd();
    }

    /// <summary>
    /// Every exit from an ad goes through here. Clearing <see cref="_pendingCallback"/> before invoking it
    /// means a callback that itself starts another ad is not treated as re-entrant, and pre-loading here
    /// keeps the next treasure box from waiting on a cold request.
    /// </summary>
    private void Settle(bool earned)
    {
        // Both the reward and the close event can reach here for one ad; whichever arrives first settles
        // it and the other finds nothing to do. Without this guard the second one would queue a duplicate
        // ad load.
        if (_pendingCallback == null) return;

        Action<bool> callback = _pendingCallback;
        _pendingCallback = null;
        _rewardEarned = false;

        _rewardedAd?.LoadAd();

        callback?.Invoke(earned);
    }

#else

    // ─── Stub used until the LevelPlay package is installed ───────────────────
    // Reports "not ready" rather than granting, so nothing silently hands out rewards through a service
    // that cannot show an ad. The bootstrap leaves NullAdService in place in this configuration, so this
    // path should not be reached in a normal build.

    public bool IsAdReady => false;

    public void Initialize(bool consentGranted, MonoBehaviour coroutineRunner = null, bool enableTestMode = false)
    {
        Debug.LogWarning("[LevelPlayAdService] LEVELPLAY_ENABLED is not set — the LevelPlay SDK is not in "
            + "the project. No real ads will be shown.");
    }

    public void ShowRewardedAd(Action<bool> onComplete)
    {
        Debug.LogWarning("[LevelPlayAdService] ShowRewardedAd called without the LevelPlay SDK — "
            + (GrantRewardWhenAdUnavailable ? "granting the reward anyway." : "no reward."));
        onComplete?.Invoke(GrantRewardWhenAdUnavailable);
    }

#endif
}
