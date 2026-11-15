using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the analytics lifecycle: starts GameAnalytics, measures time (session segments, lifetime
/// playtime milestones, time in the shop), and forwards the game's existing C# events to
/// <see cref="GameEvents"/>. Events that need context only the caller has (dhikr, quiz, ads,
/// placement, coins) are sent from the call site instead — see <see cref="GameEvents"/>.
///
/// Lives in the Jannah Garden scene and survives scene loads. Several managers it listens to are
/// per-scene, so instance subscriptions are re-bound on every scene load.
/// </summary>
public class GameAnalyticsManager : MonoBehaviour
{
    public static GameAnalyticsManager Instance { get; private set; }

    private const string FirstLaunchKey = "analytics.first_launch_done";
    private const string LifetimeSecondsKey = "analytics.lifetime_seconds";
    private const string MilestoneKeyPrefix = "analytics.milestone_";

    /// <summary>Lifetime playtime milestones, in minutes, each reported once per install.</summary>
    private static readonly int[] PlaytimeMilestones = { 5, 15, 30, 60, 120, 300, 600, 1200 };

    private float _sessionStart;
    private float _segmentStart;
    private float _pausedAt = -1f;
    private float _lifetimeSecondsAtSegmentStart;
    private float _shopOpenedAt = -1f;
    private int _lastXPLevel = -1;
    private bool _quitSent;

    private PlayerXPManager _boundXP;
    private GardenAreaManager _boundAreas;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialised in Awake so it is up before any OnApplicationPause or Start elsewhere.
        GameEvents.EnsureInitialized();

        _sessionStart = Time.realtimeSinceStartup;
        _segmentStart = _sessionStart;
        _lifetimeSecondsAtSegmentStart = PlayerPrefs.GetFloat(LifetimeSecondsKey, 0f);
    }

    private void Start()
    {
        bool firstLaunch = PlayerPrefs.GetInt(FirstLaunchKey, 0) == 0;
        if (firstLaunch)
        {
            PlayerPrefs.SetInt(FirstLaunchKey, 1);
            PlayerPrefs.Save();
        }
        GameEvents.GameStarted(firstLaunch, Time.realtimeSinceStartup);

        InGameShopManager.OnShopOpened += HandleShopOpened;
        InGameShopManager.OnShopClosed += HandleShopClosed;
        TreasureBoxManager.OnBoxOpened += HandleBoxOpened;
        TreasureBoxManager.OnSetCompleted += HandleSetCompleted;
        IAPManager.OnPurchaseSucceeded += HandleIapSucceeded;
        IAPManager.OnPurchaseFailed += HandleIapFailed;
        LocalizationManager.OnLocaleChanged += HandleLocaleChanged;
        QuickActionDrawerController.OnDrawerToggled += GameEvents.QuickActionDrawerToggled;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        BindSceneManagers();
        RefreshGlobalContext();
        StartCoroutine(SendGardenSnapshotWhenReady());
        StartCoroutine(PlaytimeTicker());
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;

        InGameShopManager.OnShopOpened -= HandleShopOpened;
        InGameShopManager.OnShopClosed -= HandleShopClosed;
        TreasureBoxManager.OnBoxOpened -= HandleBoxOpened;
        TreasureBoxManager.OnSetCompleted -= HandleSetCompleted;
        IAPManager.OnPurchaseSucceeded -= HandleIapSucceeded;
        IAPManager.OnPurchaseFailed -= HandleIapFailed;
        LocalizationManager.OnLocaleChanged -= HandleLocaleChanged;
        QuickActionDrawerController.OnDrawerToggled -= GameEvents.QuickActionDrawerToggled;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        UnbindSceneManagers();
    }

    // ─── App lifecycle & time ─────────────────────────────────────────────────

    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            if (_pausedAt >= 0f) return;
            _pausedAt = Time.realtimeSinceStartup;

            FlushSegment();
            SendGardenSnapshot();
            GameEvents.GamePaused();
        }
        else
        {
            if (_pausedAt < 0f) return;
            float away = Time.realtimeSinceStartup - _pausedAt;
            _pausedAt = -1f;

            _segmentStart = Time.realtimeSinceStartup;
            GameEvents.GameResumed(away);
        }
    }

    private void OnApplicationQuit()
    {
        if (_quitSent) return;
        _quitSent = true;

        if (_pausedAt < 0f) FlushSegment();
        GameEvents.GameQuit(Time.realtimeSinceStartup - _sessionStart);
    }

    /// <summary>Banks foreground time since the last segment into lifetime playtime and reports it.</summary>
    private void FlushSegment()
    {
        float seconds = Time.realtimeSinceStartup - _segmentStart;
        if (seconds <= 0.5f) return;

        float lifetime = _lifetimeSecondsAtSegmentStart + seconds;
        PlayerPrefs.SetFloat(LifetimeSecondsKey, lifetime);
        PlayerPrefs.Save();

        GameEvents.SessionSegment(seconds, lifetime / 60f);
        CheckMilestones(lifetime);

        _lifetimeSecondsAtSegmentStart = lifetime;
        _segmentStart = Time.realtimeSinceStartup;
    }

    private IEnumerator PlaytimeTicker()
    {
        var wait = new WaitForSecondsRealtime(30f);
        while (true)
        {
            yield return wait;
            if (_pausedAt >= 0f) continue;
            CheckMilestones(_lifetimeSecondsAtSegmentStart + (Time.realtimeSinceStartup - _segmentStart));
        }
    }

    private static void CheckMilestones(float lifetimeSeconds)
    {
        float minutes = lifetimeSeconds / 60f;
        foreach (int m in PlaytimeMilestones)
        {
            if (minutes < m) break;
            string key = MilestoneKeyPrefix + m;
            if (PlayerPrefs.GetInt(key, 0) == 1) continue;

            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
            GameEvents.PlaytimeMilestone(m);
        }
    }

    // ─── Scenes ───────────────────────────────────────────────────────────────

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameEvents.SceneLoaded(scene.name, Time.realtimeSinceStartup);
        BindSceneManagers();
        RefreshGlobalContext();
        StartCoroutine(SendGardenSnapshotWhenReady());
    }

    private void BindSceneManagers()
    {
        if (_boundXP != PlayerXPManager.Instance)
        {
            if (_boundXP != null) _boundXP.OnXPChanged -= HandleXPChanged;
            _boundXP = PlayerXPManager.Instance;
            if (_boundXP != null)
            {
                _lastXPLevel = _boundXP.xpLevel;
                _boundXP.OnXPChanged += HandleXPChanged;
            }
        }

        if (_boundAreas != GardenAreaManager.Instance)
        {
            if (_boundAreas != null) _boundAreas.AreaUnlocked -= HandleAreaUnlocked;
            _boundAreas = GardenAreaManager.Instance;
            if (_boundAreas != null) _boundAreas.AreaUnlocked += HandleAreaUnlocked;
        }
    }

    private void UnbindSceneManagers()
    {
        if (_boundXP != null) _boundXP.OnXPChanged -= HandleXPChanged;
        if (_boundAreas != null) _boundAreas.AreaUnlocked -= HandleAreaUnlocked;
        _boundXP = null;
        _boundAreas = null;
    }

    private IEnumerator SendGardenSnapshotWhenReady()
    {
        // The garden rebuilds from save over several frames (and possibly downloads); give it time to
        // settle so the count is the real garden rather than a half-loaded one.
        yield return new WaitForSecondsRealtime(15f);
        SendGardenSnapshot();
    }

    private static void SendGardenSnapshot()
    {
        if (ItemPlacementManager.Instance == null) return;
        GameEvents.GardenSnapshot(ItemPlacementManager.Instance.ActivePlacedItems.Count);
    }

    // ─── Context ──────────────────────────────────────────────────────────────

    private void RefreshGlobalContext()
    {
        int level = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpLevel : 0;
        string locale = LocalizationManager.Instance != null ? LocalizationManager.Instance.CurrentLocale.ToString() : "unknown";
        bool subscriber = TreasureBoxManager.Instance != null && TreasureBoxManager.Instance.isSubscriber;
        GameEvents.SetGlobalContext(level, locale, subscriber);
    }

    private void HandleLocaleChanged()
    {
        if (LocalizationManager.Instance != null)
            GameEvents.LanguageChanged(LocalizationManager.Instance.CurrentLocale.ToString());
        RefreshGlobalContext();
    }

    // ─── Forwarded game events ────────────────────────────────────────────────

    private void HandleXPChanged(int newLevel, float currentXP, float xpToNextLevel)
    {
        if (_lastXPLevel > 0 && newLevel > _lastXPLevel)
        {
            GameEvents.LevelReached(_lastXPLevel, newLevel);
            RefreshGlobalContext();
        }
        _lastXPLevel = newLevel;
    }

    private void HandleAreaUnlocked(GardenAreaDefinition area)
    {
        if (area == null) return;
        GameEvents.AreaUnlocked(area.number, area.id);
    }

    private void HandleShopOpened()
    {
        _shopOpenedAt = Time.realtimeSinceStartup;
        GameEvents.ShopOpened();
    }

    private void HandleShopClosed()
    {
        if (_shopOpenedAt < 0f) return;
        GameEvents.FeatureTime("Shop", Time.realtimeSinceStartup - _shopOpenedAt);
        _shopOpenedAt = -1f;
    }

    private static void HandleBoxOpened(TreasureBoxTier tier, int slot, TreasureBoxData data) =>
        GameEvents.TreasureBoxOpened(tier.ToString(), slot);

    private static void HandleSetCompleted(TreasureBoxTier tier, TreasureBoxData data) =>
        GameEvents.TreasureBoxSetCompleted(tier.ToString());

    private static void HandleIapSucceeded(ShopItemData data) =>
        GameEvents.IapSucceeded(data != null ? data.iapProductId : null);

    private static void HandleIapFailed(ShopItemData data, string message) =>
        GameEvents.IapFailed(data != null ? data.iapProductId : null, message);
}
