using UnityEngine;
using System.Collections;
using System;
using TMPro;
using FlutterIntegration;

/// <summary>
/// The game's single entry point for rewarded ads.
///
/// The game does NOT own an ad SDK. Amal embeds this Unity build inside a Flutter host that already
/// ships google_mobile_ads, and shipping a second copy inside UnityFramework broke the iOS app: the
/// Unity plugin referenced native-ads symbols that the final binary never linked, so dyld killed the
/// app before Flutter's main() even ran. Ads now live on the Flutter side only, and this class just
/// asks for them over <see cref="FlutterBridge"/>.
///
/// Do not reintroduce a Unity-side ad package. Two Google Mobile Ads instances in one process means two
/// initialisations and two consent (UMP) flows, on top of the linking problem that caused the crash.
///
/// The fake ad panel below is the fallback when there is no Flutter host at all (Editor play mode) or
/// when Flutter has no ad loaded — the player waits out a timer and is granted the reward, which is the
/// behaviour the game shipped with.
/// </summary>
public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance;

    /// <summary>
    /// How long to wait for Flutter's REWARDED_AD_RESULT before giving up. Generous, because the wait
    /// covers the whole ad: request, load, playback, and the close button. Without it a dropped message
    /// would strand the game at timeScale 0 with no way out.
    /// </summary>
    private const float ResultTimeoutSeconds = 120f;

    // Dynamic callback for the in-flight rewarded ad.
    private Action onRewardedAdEarned;

    /// <summary>Set while a request is out to Flutter, so a second tap cannot start a second ad.</summary>
    private bool _adInFlight;

    private Coroutine _timeout;
    private float _timeScaleBeforeAd = 1f;

    [Header("Fake Ad Setup (Fallback)")]
    public GameObject fakeRewardedAdPanel;
    public float fakeAdDuration = 15f;
    public TMP_Text fakeTimerCountDownText;

    /// <summary>
    /// True when Flutter has told us it has a rewarded ad loaded. False does not block
    /// <see cref="ShowRewardedAd(Action)"/> — it falls back to the fake ad — but a "Watch Ad" button can
    /// read this to show the player what to expect.
    /// </summary>
    public bool IsAdReady => FlutterBridge.RewardedAdReady;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Optionally: DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        FlutterBridge.OnRewardedAdResult += HandleRewardedAdResult;
    }

    void OnDestroy()
    {
        FlutterBridge.OnRewardedAdResult -= HandleRewardedAdResult;

        // A scene change mid-ad must not leave the next scene frozen.
        if (_adInFlight) Time.timeScale = _timeScaleBeforeAd;

        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(false);
        }
    }

    #region Rewarded Ad

    /// <summary>
    /// Shows a rewarded ad and runs <paramref name="onReward"/> only if the player earns the reward.
    /// </summary>
    /// <param name="onReward">Runs once the ad is watched through. Never runs if the player skips.</param>
    /// <param name="source">
    /// What the reward is for, forwarded to Flutter for attribution — e.g. "treasure_box", "shop_item".
    /// </param>
    public void ShowRewardedAd(Action onReward, string source = "game")
    {
        if (_adInFlight)
        {
            Debug.LogWarning("[AdsManager] A rewarded ad is already in flight — ignoring this request.");
            return;
        }

        // No host (Editor play mode) or nothing loaded: the player waits out the timer instead. Flutter
        // pre-loads ads and pushes UPDATE_AD_AVAILABILITY, so this is the rare path on device.
        if (FlutterBridge.Instance == null || !FlutterBridge.RewardedAdReady)
        {
            Debug.LogWarning("[AdsManager] No rewarded ad available from Flutter — showing the fake ad instead.");
            ShowFakeRewardedAd(onReward);
            return;
        }

        _adInFlight = true;
        onRewardedAdEarned = onReward;

        // Flutter draws the ad over the Unity view, so the game must stop rather than run underneath it.
        _timeScaleBeforeAd = Time.timeScale;
        Time.timeScale = 0f;

        Debug.Log($"[AdsManager] Requesting a rewarded ad from Flutter (source: {source}).");
        FlutterBridge.Instance.RequestRewardedAd(source);

        _timeout = StartCoroutine(FailIfFlutterNeverAnswers());
    }

    private IEnumerator FailIfFlutterNeverAnswers()
    {
        // Realtime: the game is paused at timeScale 0 while the ad plays.
        yield return new WaitForSecondsRealtime(ResultTimeoutSeconds);

        Debug.LogError($"[AdsManager] Flutter sent no {FlutterCommands.RewardedAdResult} within "
            + $"{ResultTimeoutSeconds}s — resuming the game without a reward.");

        _timeout = null;
        FinishRewardedAd(false);
    }

    private void HandleRewardedAdResult(RewardedAdResultPayload result)
    {
        if (!_adInFlight)
        {
            // Late or duplicate answer to an ad that already ended. Resuming here would clobber a
            // timeScale the game has since set for its own reasons, so drop it.
            Debug.LogWarning($"[AdsManager] Rewarded ad result '{result.status}' arrived with no ad in flight — ignoring.");
            return;
        }

        if (_timeout != null)
        {
            StopCoroutine(_timeout);
            _timeout = null;
        }

        switch (result.status)
        {
            case RewardedAdStatus.Rewarded:
                Debug.Log($"[AdsManager] Rewarded ad watched (source: {result.source}) — granting the reward.");
                FinishRewardedAd(true);
                break;

            case RewardedAdStatus.Dismissed:
                // Not an error. The player chose to stop watching; they simply do not get the reward.
                Debug.Log("[AdsManager] Rewarded ad dismissed — no reward.");
                FinishRewardedAd(false);
                break;

            default:
                Debug.LogWarning($"[AdsManager] Rewarded ad did not play (status: {result.status}) — no reward.");
                FinishRewardedAd(false);
                break;
        }
    }

    /// <summary>
    /// Restores the game and settles the pending callback. Every exit from an ad goes through here —
    /// missing one would leave the game frozen at timeScale 0.
    /// </summary>
    private void FinishRewardedAd(bool earned)
    {
        _adInFlight = false;
        Time.timeScale = _timeScaleBeforeAd;

        Action callback = onRewardedAdEarned;
        onRewardedAdEarned = null;

        if (earned) callback?.Invoke();
    }

    #endregion

    #region Fake Ad (Fallback/Testing)

    public void ShowFakeRewardedAd(Action onComplete)
    {
        StartCoroutine(AdRoutine(onComplete));
    }

    private IEnumerator AdRoutine(Action onComplete)
    {
        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(true);
        }

        float remainingTime = fakeAdDuration;
        while (remainingTime > 0)
        {
            if (fakeTimerCountDownText != null)
            {
                fakeTimerCountDownText.text = Mathf.CeilToInt(remainingTime).ToString();
            }
            // Realtime, so the countdown still runs when a panel has paused the game.
            yield return new WaitForSecondsRealtime(1f);
            remainingTime -= 1f;
        }

        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(false);
        }

        onComplete?.Invoke();
    }

    #endregion
}
