using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

/// <summary>
/// Manages mobile notifications for Jannah Garden: Treasure Box availability, and the Daily Noor Coin
/// Surprise becoming claimable again.
///
/// Scheduling is all-or-nothing on purpose. The platform APIs give no cheap way to cancel one pending
/// notification by meaning, so every reschedule cancels everything and rebuilds the full set. That is
/// why both features are scheduled from the single <see cref="ScheduleAllNotifications"/> pass — if the
/// daily reward scheduled itself from a separate manager, the next treasure-box reschedule would
/// silently wipe it.
/// </summary>
public class MobileNotificationManager : MonoBehaviour
{
    public static MobileNotificationManager Instance { get; private set; }

    private const string CHANNEL_ID = "treasure_box_channel";
    private const string DAILY_REWARD_CHANNEL_ID = "daily_reward_channel";

    [Header("Daily Noor Coin Surprise")]
    [Tooltip("Title of the notification fired when the daily rewarded-ad offer becomes claimable again.")]
    [SerializeField] private string dailyRewardTitle = "Daily Noor Coin Surprise!";

    [Tooltip("Body of the notification fired when the daily rewarded-ad offer becomes claimable again.")]
    [TextArea(2, 4)]
    [SerializeField] private string dailyRewardText =
        "Your Daily Noor Coin Surprise is now available. Go and claim your reward!";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeNotifications();
    }

    private void Start()
    {
        // Recalculate whenever either system's state moves: a box opened/reset, or a daily offer claimed.
        TreasureBoxManager.OnStateChanged += ScheduleAllNotifications;
        DailyOfferManager.OnOffersChanged += ScheduleAllNotifications;

        // Ask for permissions first (if needed), then do the initial schedule when game starts
        StartCoroutine(RequestNotificationPermissions());
    }

    private IEnumerator RequestNotificationPermissions()
    {
#if UNITY_ANDROID
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
        {
            UnityEngine.Android.Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS");
            // Wait until the permission dialog is resolved
            yield return new WaitWhile(() => !UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"));
        }
#elif UNITY_IOS
        using (var req = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, true))
        {
            while (!req.IsFinished)
            {
                yield return null;
            }
        }
#endif
        yield return null;
        ScheduleAllNotifications();
    }

    private void OnDestroy()
    {
        TreasureBoxManager.OnStateChanged -= ScheduleAllNotifications;
        DailyOfferManager.OnOffersChanged -= ScheduleAllNotifications;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            ScheduleAllNotifications();
        }
    }

    private void OnApplicationQuit()
    {
        ScheduleAllNotifications();
    }

    /// <summary>
    /// Initializes notification channels for Android.
    /// </summary>
    public void InitializeNotifications()
    {
#if UNITY_ANDROID
        var channel = new AndroidNotificationChannel()
        {
            Id = CHANNEL_ID,
            Name = "Treasure Box Notifications",
            Importance = Importance.Default,
            Description = "Notifications for when treasure boxes are ready to be opened"
        };
        AndroidNotificationCenter.RegisterNotificationChannel(channel);

        var dailyRewardChannel = new AndroidNotificationChannel()
        {
            Id = DAILY_REWARD_CHANNEL_ID,
            Name = "Daily Reward Notifications",
            Importance = Importance.Default,
            Description = "Notifications for when the Daily Noor Coin Surprise can be claimed again"
        };
        AndroidNotificationCenter.RegisterNotificationChannel(dailyRewardChannel);
#endif
    }

    /// <summary>
    /// Cancels every pending notification and rebuilds the whole schedule from current state.
    /// This is the only entry point that cancels — the per-feature schedulers below assume a clean slate.
    /// </summary>
    public void ScheduleAllNotifications()
    {
        CancelAllNotifications();

        ScheduleTreasureBoxNotifications();
        ScheduleDailyRewardNotification();
    }

    /// <summary>
    /// Schedules notifications for upcoming treasure boxes.
    /// Assumes pending notifications were already cleared by <see cref="ScheduleAllNotifications"/>.
    /// </summary>
    private void ScheduleTreasureBoxNotifications()
    {
        if (TreasureBoxManager.Instance == null) return;

        TreasureBoxTier upcomingTier = TreasureBoxManager.Instance.GetUpcomingTier();

        // Only schedule if the tier is unlocked and accessible
        if (!TreasureBoxManager.Instance.IsTierUnlocked(upcomingTier)) return;

        for (int i = 0; i < TreasureBoxManager.SLOTS_PER_TIER; i++)
        {
            // If the slot is not yet available, it means it will be available in the future
            if (!TreasureBoxManager.Instance.IsSlotAvailable(upcomingTier, i))
            {
                DateTime availableAt = TreasureBoxManager.Instance.GetSlotAvailableAt(upcomingTier, i);

                // If we got a valid future time, schedule the notification
                if (availableAt != DateTime.MinValue && availableAt > DateTime.Now)
                {
                    string title = "Treasure Box Ready!";
                    string text = $"A new {upcomingTier} Treasure Box is ready to be opened in Jannah Garden!";
                    ScheduleNotification(title, text, availableAt, CHANNEL_ID);
                }
            }
        }
    }

    /// <summary>
    /// Schedules the "your Daily Noor Coin Surprise is ready" notification for the moment the offer comes
    /// off cooldown. Nothing is scheduled when the reward is already claimable — the player can just open
    /// the shop and take it, so there is no future event to announce.
    /// Assumes pending notifications were already cleared by <see cref="ScheduleAllNotifications"/>.
    /// </summary>
    private void ScheduleDailyRewardNotification()
    {
        if (DailyOfferManager.Instance == null) return;

        if (!DailyOfferManager.Instance.TryGetNextRefreshTime(out DateTime nextRefreshUtc)) return;

        // The notification APIs work in device-local time; DailyOfferManager tracks cooldowns in UTC.
        DateTime fireTime = nextRefreshUtc.ToLocalTime();
        if (fireTime <= DateTime.Now) return;

        ScheduleNotification(dailyRewardTitle, dailyRewardText, fireTime, DAILY_REWARD_CHANNEL_ID);

        Debug.Log($"[MobileNotificationManager] Daily reward notification scheduled for {fireTime}.");
    }

    private void ScheduleNotification(string title, string text, DateTime fireTime, string channelId)
    {
#if UNITY_ANDROID
        var notification = new AndroidNotification
        {
            Title = title,
            Text = text,
            FireTime = fireTime,
            SmallIcon = "icon_small", // These should match icons configured in Mobile Notifications Settings
            LargeIcon = "icon_large"
        };
        AndroidNotificationCenter.SendNotification(notification, channelId);
#elif UNITY_IOS
        TimeSpan delay = fireTime - DateTime.Now;
        if (delay.TotalSeconds <= 0) return;

        var timeTrigger = new iOSNotificationTimeIntervalTrigger()
        {
            TimeInterval = delay,
            Repeats = false
        };

        var notification = new iOSNotification()
        {
            Identifier = Guid.NewGuid().ToString(),
            Title = title,
            Body = text,
            Subtitle = "Jannah Garden",
            ShowInForeground = true,
            ForegroundPresentationOption = (PresentationOption.Alert | PresentationOption.Sound),
            CategoryIdentifier = "category_a",
            ThreadIdentifier = "thread1",
            Trigger = timeTrigger,
        };

        iOSNotificationCenter.ScheduleNotification(notification);
#endif
    }

    public void CancelAllNotifications()
    {
#if UNITY_ANDROID
        AndroidNotificationCenter.CancelAllNotifications();
#elif UNITY_IOS
        iOSNotificationCenter.RemoveAllScheduledNotifications();
#endif
    }
}
