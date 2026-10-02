using System;
using System.Collections;
using UnityEngine;
using FlutterIntegration;
#if LEVELPLAY_ENABLED
// Written against Ads Mediation 9.5.1, verified against the package source rather than the docs — the
// online 8.x reference is wrong for this version in two places: the namespace moved out of
// `com.unity3d.mediation` in 8.7, and OnAdDisplayFailed gained a LevelPlayAdInfo parameter.
using Unity.Services.LevelPlay;
#endif

/// <summary>
/// Rewarded ads through Unity LevelPlay mediation (Unity Ads, InMobi, and later
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
/// again. It stays safe only while the AdMob adapter is left out.
///
/// This is not hypothetical: on 2026-10-01 the AdMob adapter was found installed, and it broke the
/// Flutter host's Android Release build at :app:checkReleaseDuplicateClasses — its ads-mobile-sdk:1.5.0
/// and the host's play-services-ads-lite:23.6.0 declare the same com.google.android.gms.ads.* classes.
/// It was removed, along with the AppLovin and Meta adapters. Do not try to exclude it in Gradle: the adapter
/// calls the excluded classes, so the build passes and the app crashes at runtime instead.
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
    // the individual networks (Unity Ads, InMobi) are configured in the LevelPlay dashboard and
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

    /// <summary>
    /// How many times a day the bypass above may actually fire. Set from <see cref="AdServiceBootstrap"/>.
    ///
    /// The bypass without a cap is an unbounded hole: a region or an account that never gets fill opens
    /// every treasure box for free, forever, and the game pays out for impressions it never served. The
    /// cap keeps the thing it was for — a player is never hard-blocked by a broken ad slot — while
    /// bounding what a permanent no-fill can cost.
    /// </summary>
    public int MaxBypassGrantsPerDay { get; set; } = 3;

    // Per-day bypass budget. PlayerPrefs rather than the treasure box save file because this is not
    // player progress — it is a throttle on our own failure path, and it is read from the shop as well.
    private const string BypassDateKey = "ads.bypass.date";
    private const string BypassCountKey = "ads.bypass.count";

    /// <summary>
    /// True if a "we could not show you an ad" reward may be granted right now, spending one of today's
    /// grants if so. Call this only on the failure paths — never where the player skipped an ad they
    /// were actually shown.
    /// </summary>
    private bool TryConsumeUnavailableBypass()
    {
        if (!GrantRewardWhenAdUnavailable) return false;

        if (MaxBypassGrantsPerDay <= 0)
        {
            Debug.LogWarning("[LevelPlayAdService] No ad to show and the daily bypass budget is zero — no reward.");
            return false;
        }

        // UTC, to match the treasure box save data. A player who moves the device clock can still earn
        // extra grants; that is worth far less than a treasure box and not worth a server round-trip.
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        int used = PlayerPrefs.GetString(BypassDateKey, string.Empty) == today
            ? PlayerPrefs.GetInt(BypassCountKey, 0)
            : 0;

        if (used >= MaxBypassGrantsPerDay)
        {
            Debug.LogWarning($"[LevelPlayAdService] No ad to show, and today's {MaxBypassGrantsPerDay} "
                + "no-ad rewards are already spent — no reward. If this keeps happening, the ad unit is "
                + "not filling; check the LevelPlay dashboard rather than raising the cap.");
            return false;
        }

        PlayerPrefs.SetString(BypassDateKey, today);
        PlayerPrefs.SetInt(BypassCountKey, used + 1);
        PlayerPrefs.Save();

        Debug.LogWarning($"[LevelPlayAdService] No ad available — granting the reward anyway "
            + $"({used + 1}/{MaxBypassGrantsPerDay} today).");
        return true;
    }

#if LEVELPLAY_ENABLED

    private LevelPlayRewardedAd _rewardedAd;
    private bool _initStarted;

    /// <summary>True once the rewarded ad has been built on the host's SDK — see <see cref="AttachToHostSdk"/>.</summary>
    private bool _attached;

    /// <summary>Remembered from Initialize, because the integration check can only run once the host is up.</summary>
    private bool _enableTestMode;

    /// <summary>
    /// How long to wait for the host's <c>UPDATE_AD_SDK_STATE</c> before building the rewarded ad anyway.
    ///
    /// The handshake is the correct mechanism, but it is not the only way the SDK can be up: the host
    /// initialises LevelPlay whether or not it has been taught to announce it. Waiting forever for an
    /// announcement that may never come means no ads at all, which is strictly worse than trying and
    /// failing — a failed attach logs and costs nothing, since the SDK is a process singleton and we
    /// never call Init ourselves either way.
    ///
    /// Realtime, because this runs while the loading screen may have the game at timeScale 0.
    /// </summary>
    private const float HostSdkAnnouncementTimeoutSeconds = 10f;

    /// <summary>How often to re-ask the host during that wait.</summary>
    private const float HostSdkAskIntervalSeconds = 2f;

    /// <summary>Backoff between load retries, multiplied by the consecutive-failure count.</summary>
    private const float LoadRetryDelaySeconds = 10f;

    /// <summary>Stop retrying after this many failures in a row so a dead ad unit doesn't retry forever.</summary>
    private const int MaxConsecutiveLoadFailures = 5;

    private int _consecutiveLoadFailures;
    private bool _retryScheduled;

    /// <summary>
    /// How long to wait for the SDK to report how a shown ad ended before settling it ourselves.
    ///
    /// Generous, because the wait covers the whole ad — playback plus however long the player leaves the
    /// close button alone. It only has to be shorter than "forever": every result path runs through a
    /// LevelPlay event, and if none of them arrives the pending callback is never cleared, so the caller
    /// that asked for the ad never hears back AND every later request is refused as already-in-flight.
    /// One lost callback would otherwise take out treasure boxes and the shop for the rest of the session.
    ///
    /// Realtime seconds, but Unity stops stepping coroutines while a full-screen native ad has the app
    /// backgrounded on Android — so the clock effectively only runs while the game is on screen, which is
    /// where a lost callback actually strands us.
    /// </summary>
    private const float ShowTimeoutSeconds = 180f;

    /// <summary>The in-flight watchdog from <see cref="ShowRewardedAd"/>, stopped by <see cref="Settle"/>.</summary>
    private Coroutine _showWatchdog;

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
    /// Waits for the Flutter host to finish initialising LevelPlay, then builds the game's rewarded ad
    /// and pre-loads it. Safe to call more than once; only the first call does anything.
    ///
    /// This does <b>not</b> initialise the SDK, and must not. The game ships inside the Amal Flutter app
    /// as one APK in one process, sharing one LevelPlay app key (<c>285c611cd</c> on Android). LevelPlay
    /// is a per-process singleton that may only be initialised once, and the host owns that call for two
    /// reasons: it runs first, and it owns the GDPR/UMP consent flow whose answer has to reach the SDK
    /// before init. Consent is therefore not a parameter here either — the host sets it, and the game
    /// setting it again could overwrite a "no" the player gave in Flutter's dialog.
    ///
    /// The game still gets its own revenue reporting: it shows ads on its own ad unit
    /// (<see cref="RewardedAdUnitId"/>) under the host's shared app key.
    /// </summary>
    /// <param name="enableTestMode">
    /// Turns on adapter debug logging and runs LevelPlay's integration check. Both write their findings
    /// to logcat, which is the only way to see *why* a network returns no fill — a missing adapter, a
    /// bad app key, or a device the dashboard has not been told to treat as a test device. Leave off for
    /// release builds; the logging is noisy and reveals the mediation setup.
    /// </param>
    public void Initialize(MonoBehaviour coroutineRunner = null, bool enableTestMode = false)
    {
        _coroutineRunner = coroutineRunner;

        if (_initStarted) return;
        _initStarted = true;
        _enableTestMode = enableTestMode;

        if (enableTestMode) LevelPlay.SetAdaptersDebug(true);

        // The host may well have finished init before this scene existed, in which case its unprompted
        // broadcast already came and went. Check the cached state first, then subscribe, then ask — in
        // that order, so neither an early nor a late host init is missed.
        if (FlutterBridge.AdSdkInitialized)
        {
            AttachToHostSdk();
            return;
        }

        FlutterBridge.OnAdSdkStateChanged += HandleHostSdkStateChanged;

        if (coroutineRunner != null)
        {
            coroutineRunner.StartCoroutine(AskHostUntilItAnswers());
        }
        else if (FlutterBridge.Instance != null)
        {
            // No runner to retry or time out with — one ask is all we get.
            FlutterBridge.Instance.RequestAdSdkState();
        }

        if (FlutterBridge.Instance == null)
        {
            // The bridge GameObject comes up with the scene and asks on our behalf during its handshake,
            // so this is recoverable — but it is worth saying out loud, because the symptom otherwise is
            // simply that ads never appear.
            Debug.LogWarning("[LevelPlayAdService] No FlutterBridge in the scene yet — waiting for the "
                + "host to announce LevelPlay. If no ad ever shows, check that the bridge object exists "
                + $"and that Flutter answers {FlutterCommands.RequestAdSdkState}.");
        }
    }

    /// <summary>
    /// Asks the host whether LevelPlay is up, repeatedly, and gives up waiting after
    /// <see cref="HostSdkAnnouncementTimeoutSeconds"/> — at which point it builds the ad anyway.
    ///
    /// The giving-up branch exists because the host answering this at all is newer than the host
    /// initialising LevelPlay. A build whose Flutter side predates the handshake would otherwise show no
    /// ads whatsoever, which is how the first merged QA build behaved: the game sat waiting for an
    /// announcement nothing was sending, and every treasure box fell through to the no-ad bypass.
    /// </summary>
    private IEnumerator AskHostUntilItAnswers()
    {
        float waited = 0f;

        while (waited < HostSdkAnnouncementTimeoutSeconds)
        {
            if (_attached) yield break;

            if (FlutterBridge.Instance != null) FlutterBridge.Instance.RequestAdSdkState();

            yield return new WaitForSecondsRealtime(HostSdkAskIntervalSeconds);
            waited += HostSdkAskIntervalSeconds;
        }

        if (_attached) yield break;

        Debug.LogWarning(
            $"[LevelPlayAdService] The host did not answer {FlutterCommands.RequestAdSdkState} within "
            + $"{HostSdkAnnouncementTimeoutSeconds:0}s. Building the rewarded ad anyway, on the assumption "
            + "that Flutter has LevelPlay up but has not been taught to announce it. If ads still do not "
            + $"show, the Flutter side needs to answer with {FlutterCommands.UpdateAdSdkState} — see "
            + "MONETIZATION.md.");

        AttachToHostSdk();
    }

    /// <summary>
    /// Flutter's answer to "is LevelPlay up?". Only the success edge matters: a false means the host's own
    /// init failed, and the game has no way to retry it — the host must.
    /// </summary>
    private void HandleHostSdkStateChanged(bool initialized)
    {
        if (!initialized) return;

        AttachToHostSdk();

        // Unsubscribe only once the ad actually exists. If the attach failed, stay subscribed so a later
        // announcement gets another go — the host may re-init after its own failure.
        if (_attached) FlutterBridge.OnAdSdkStateChanged -= HandleHostSdkStateChanged;
    }

    /// <summary>
    /// Builds the game's rewarded ad on top of the SDK instance the host already initialised.
    ///
    /// This is everything the old <c>OnInitSuccess</c> handler did, minus the init itself. The native
    /// LevelPlay SDK is a per-process singleton, so once the host has it up, creating an ad object here
    /// against the game's own ad unit is all that is left to do.
    /// </summary>
    private void AttachToHostSdk()
    {
        if (_attached) return;
        _attached = true;

        if (_enableTestMode)
        {
            // Only meaningful once the SDK is up, which — by the time we get here — it should be.
            LevelPlay.ValidateIntegration();
        }

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
            // Release the latch. This can be reached from the timeout path, where the guess that the host
            // had LevelPlay up was simply wrong; if the host announces itself later, that attempt must not
            // be swallowed as "already attached".
            _attached = false;

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
            Settle(TryConsumeUnavailableBypass());
        };

        // Closing settles as "not earned" only if the reward event never arrived — and only after a frame.
        // The two events are not ordered consistently: the real SDK sends rewarded-then-closed, but the
        // Editor mock's HideAd() sends closed-then-rewarded in the same call stack. Settling here
        // immediately would consume the callback with earned=false a line before the reward arrives, which
        // is exactly why watching an ad in the Editor left the treasure box shut.
        _rewardedAd.OnAdClosed += adInfo => DeferredSettleIfUnrewarded();

        SafeLoadAd();
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
        if (!IsAdReady) SafeLoadAd();
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
            if (_rewardedAd != null) SafeLoadAd();
            else Debug.LogError("[LevelPlayAdService] No rewarded ad object — LevelPlay never initialised.");

            onComplete?.Invoke(TryConsumeUnavailableBypass());
            return;
        }

        _rewardEarned = false;
        _pendingCallback = onComplete;

        try
        {
            _rewardedAd.ShowAd();
        }
        catch (Exception e)
        {
            // ShowAd goes straight through to the native adapter, which can throw — and the exception
            // would otherwise unwind into the caller with _pendingCallback already assigned, stranding it
            // exactly the way a lost SDK event does. Settle it here instead: the player asked for an ad
            // and did not get one, which is the same "we could not show you an ad" case as a display
            // failure, so it takes the same bypass rather than counting as a skip.
            Debug.LogError($"[LevelPlayAdService] ShowAd threw — treating it as a display failure: {e}");
            Settle(TryConsumeUnavailableBypass());
            return;
        }

        StartShowWatchdog();
    }

    /// <summary>
    /// Arms the fallback that settles an ad the SDK never reports on. Cancelled by <see cref="Settle"/>,
    /// so it only ever fires when every real result path has gone silent.
    /// </summary>
    private void StartShowWatchdog()
    {
        if (_coroutineRunner == null)
        {
            Debug.LogWarning("[LevelPlayAdService] No coroutine runner — a rewarded ad the SDK never "
                + "reports on cannot be timed out, and would block every later ad this session.");
            return;
        }

        _showWatchdog = _coroutineRunner.StartCoroutine(SettleIfSdkNeverAnswers());
    }

    private IEnumerator SettleIfSdkNeverAnswers()
    {
        // Realtime: a caller may have paused the game for the duration of the ad.
        yield return new WaitForSecondsRealtime(ShowTimeoutSeconds);

        _showWatchdog = null;

        if (_pendingCallback == null) yield break;

        Debug.LogError($"[LevelPlayAdService] No reward, close, or display-failure event within "
            + $"{ShowTimeoutSeconds}s of showing the ad — settling it so later ads still work.");

        // The ad may well have played fine and only the event was lost, so this is "we could not tell you
        // watched it", not a skip — same bypass as an ad we could never show.
        Settle(TryConsumeUnavailableBypass());
    }

    /// <summary>
    /// Requests an ad, absorbing anything the native adapter throws.
    ///
    /// Every <c>LoadAd</c> here is speculative — a pre-load, a retry, or a warm-up on the way out of an
    /// ad. None of them is worth propagating an exception for, and the one inside <see cref="Settle"/>
    /// runs a line before the caller's callback, so a throw there would swallow the reward the player
    /// just earned.
    /// </summary>
    private void SafeLoadAd()
    {
        try
        {
            _rewardedAd?.LoadAd();
        }
        catch (Exception e)
        {
            Debug.LogError($"[LevelPlayAdService] LoadAd threw: {e}");
        }
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

        // A settled ad no longer needs its timeout. Leaving it running would be harmless today — it
        // re-checks _pendingCallback before doing anything — but it would fire mid-way through the *next*
        // ad, whose callback is pending, and settle that one instead.
        if (_showWatchdog != null)
        {
            if (_coroutineRunner != null) _coroutineRunner.StopCoroutine(_showWatchdog);
            _showWatchdog = null;
        }

        SafeLoadAd();

        callback?.Invoke(earned);
    }

#else

    // ─── Stub used until the LevelPlay package is installed ───────────────────
    // Reports "not ready" rather than granting, so nothing silently hands out rewards through a service
    // that cannot show an ad. The bootstrap leaves NullAdService in place in this configuration, so this
    // path should not be reached in a normal build.

    public bool IsAdReady => false;

    public void Initialize(MonoBehaviour coroutineRunner = null, bool enableTestMode = false)
    {
        Debug.LogWarning("[LevelPlayAdService] LEVELPLAY_ENABLED is not set — the LevelPlay SDK is not in "
            + "the project. No real ads will be shown.");
    }

    public void ShowRewardedAd(Action<bool> onComplete)
    {
        Debug.LogWarning("[LevelPlayAdService] ShowRewardedAd called without the LevelPlay SDK — "
            + "checking the daily no-ad reward budget.");
        onComplete?.Invoke(TryConsumeUnavailableBypass());
    }

#endif
}
