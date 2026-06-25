using System;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

/// <summary>
/// Manages mobile notifications for Jannah Garden, particularly for Treasure Box availability.
/// </summary>
public class MobileNotificationManager : MonoBehaviour
{
    public static MobileNotificationManager Instance { get; private set; }

    private const string CHANNEL_ID = "treasure_box_channel";

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
        // Subscribe to state changes so we recalculate notifications when a box is opened or state resets
        TreasureBoxManager.OnStateChanged += ScheduleTreasureBoxNotifications;
        
        // Initial schedule when game starts
        ScheduleTreasureBoxNotifications();
    }

    private void OnDestroy()
    {
        TreasureBoxManager.OnStateChanged -= ScheduleTreasureBoxNotifications;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            ScheduleTreasureBoxNotifications();
        }
    }

    private void OnApplicationQuit()
    {
        ScheduleTreasureBoxNotifications();
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
#endif
    }

    /// <summary>
    /// Cancels existing notifications and schedules new ones for upcoming treasure boxes.
    /// </summary>
    public void ScheduleTreasureBoxNotifications()
    {
        CancelAllNotifications();

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
                    ScheduleNotification(title, text, availableAt);
                }
            }
        }
    }

    private void ScheduleNotification(string title, string text, DateTime fireTime)
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
        AndroidNotificationCenter.SendNotification(notification, CHANNEL_ID);
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
