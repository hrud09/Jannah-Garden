using UnityEngine;
using System.Collections;
using System;
using TMPro;
using GoogleMobileAds.Api;

public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance;

    // Test Ad Unit IDs (Android). Replace with iOS IDs if needed for iOS builds.
#if UNITY_ANDROID
    private string bannerAdUnitId = "ca-app-pub-3940256099942544/6300978111";
    private string interstitialAdUnitId = "ca-app-pub-3940256099942544/1033173712";
    private string rewardedAdUnitId = "ca-app-pub-3940256099942544/5224354917";
#elif UNITY_IPHONE
    private string bannerAdUnitId = "ca-app-pub-3940256099942544/2934735716";
    private string interstitialAdUnitId = "ca-app-pub-3940256099942544/4411468910";
    private string rewardedAdUnitId = "ca-app-pub-3940256099942544/1712485313";
#else
    private string bannerAdUnitId = "unused";
    private string interstitialAdUnitId = "unused";
    private string rewardedAdUnitId = "unused";
#endif

    private BannerView bannerView;
    private InterstitialAd interstitialAd;
    private RewardedAd rewardedAd;

    // Dynamic callback for rewarded ad
    private Action onRewardedAdEarned;

    [Header("Fake Ad Setup (Fallback)")]
    public GameObject fakeRewardedAdPanel;
    public float fakeAdDuration = 15f;
    public TMP_Text fakeTimerCountDownText;

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
        }
    }

    void Start()
    {
        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(false);
        }

        // Initialize the Google Mobile Ads SDK.
        MobileAds.Initialize(initStatus => {
            Debug.Log("AdMob initialized.");
            LoadBannerAd();
            LoadInterstitialAd();
            LoadRewardedAd();
        });
    }

    #region Banner Ad

    public void LoadBannerAd()
    {
        if (bannerView != null)
        {
            bannerView.Destroy();
        }

        bannerView = new BannerView(bannerAdUnitId, AdSize.Banner, AdPosition.Bottom);
        AdRequest request = new AdRequest();
        bannerView.LoadAd(request);
    }

    public void ShowBanner()
    {
        if (bannerView != null)
        {
            bannerView.Show();
        }
        else
        {
            LoadBannerAd();
        }
    }

    public void HideBanner()
    {
        if (bannerView != null)
        {
            bannerView.Hide();
        }
    }

    #endregion

    #region Interstitial Ad

    public void LoadInterstitialAd()
    {
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }

        var adRequest = new AdRequest();

        InterstitialAd.Load(interstitialAdUnitId, adRequest,
            (InterstitialAd ad, LoadAdError error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogError("Interstitial ad failed to load an ad with error : " + error);
                    return;
                }
                
                interstitialAd = ad;
                RegisterEventHandlers(interstitialAd);
            });
    }

    public void ShowInterstitialAd()
    {
        if (interstitialAd != null && interstitialAd.CanShowAd())
        {
            interstitialAd.Show();
        }
        else
        {
            Debug.LogWarning("Interstitial ad is not ready yet.");
            LoadInterstitialAd(); // Try loading one for next time
        }
    }

    private void RegisterEventHandlers(InterstitialAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            Debug.Log("Interstitial Ad full screen content closed.");
            LoadInterstitialAd(); // Load next ad
        };
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogError("Interstitial ad failed to open full screen content with error : " + error);
            LoadInterstitialAd(); // Load next ad
        };
    }

    #endregion

    #region Rewarded Ad

    public void LoadRewardedAd()
    {
        if (rewardedAd != null)
        {
            rewardedAd.Destroy();
            rewardedAd = null;
        }

        var adRequest = new AdRequest();
        RewardedAd.Load(rewardedAdUnitId, adRequest,
            (RewardedAd ad, LoadAdError error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogError("Rewarded ad failed to load an ad with error : " + error);
                    return;
                }

                rewardedAd = ad;
                RegisterEventHandlers(rewardedAd);
            });
    }

    /// <summary>
    /// Shows the rewarded ad and passes the dynamic reward action.
    /// </summary>
    /// <param name="onReward">The action to execute when the user successfully earns the reward.</param>
    public void ShowRewardedAd(Action onReward)
    {
        if (rewardedAd != null && rewardedAd.CanShowAd())
        {
            onRewardedAdEarned = onReward;
            rewardedAd.Show((Reward reward) =>
            {
                // Reward the user!
                Debug.Log($"Rewarded ad granted a reward: {reward.Amount} {reward.Type}");
                if (onRewardedAdEarned != null)
                {
                    onRewardedAdEarned.Invoke();
                    onRewardedAdEarned = null; // Clear the callback after executing
                }
            });
        }
        else
        {
            Debug.LogWarning("Rewarded ad is not ready yet. Showing fake rewarded ad as fallback.");
            // Fallback to fake ad if the real one isn't ready
            ShowFakeRewardedAd(onReward);
            LoadRewardedAd(); // Attempt to load a real one for next time
        }
    }

    private void RegisterEventHandlers(RewardedAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            Debug.Log("Rewarded Ad full screen content closed.");
            LoadRewardedAd(); // Load next ad
        };
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogError("Rewarded ad failed to open full screen content with error : " + error);
            LoadRewardedAd(); // Load next ad
        };
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
            yield return new WaitForSeconds(1f);
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
