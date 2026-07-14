using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// A single claimed offer. The cooldown is baked into the entry (rather than read back off the
/// ScriptableObject) so an offer already on cooldown keeps the window it was claimed under, even if
/// the designer later retunes <see cref="ShopItemData.offerCooldownHours"/>.
/// </summary>
[Serializable]
public class DailyOfferEntry
{
    public string itemID;
    public long claimedAtTicks;   // DateTime.UtcNow.Ticks at claim time
    public long availableAtTicks; // claimedAtTicks + cooldown
}

[Serializable]
public class DailyOfferSaveData
{
    public List<DailyOfferEntry> entries = new List<DailyOfferEntry>();
}

/// <summary>
/// Tracks the cooldown on daily-refreshing shop offers (e.g. "watch an ad, get 100 Noor Coins",
/// claimable once every 24 hours).
///
/// Claim times are stored as UTC ticks via <see cref="SaveSystem"/>, so a cooldown keeps running while
/// the game is closed and cannot be skipped by relaunching. It can still be skipped by moving the
/// device clock backwards — closing that hole needs a server timestamp, which is a backend concern.
/// </summary>
public class DailyOfferManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    private static DailyOfferManager _instance;
    private static bool _isQuitting;

    /// <summary>
    /// The live manager. It has nothing to configure in the Inspector, so rather than require a
    /// GameObject in every scene that shows the shop, it creates itself on first use. A scene may still
    /// contain one (it wins, and the spare destroys itself in <see cref="Awake"/>).
    /// Null only while the app is shutting down.
    /// </summary>
    public static DailyOfferManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            if (_isQuitting) return null; // Don't resurrect a manager during teardown

            _instance = FindFirstObjectByType<DailyOfferManager>();
            if (_instance != null) return _instance;

            // AddComponent runs Awake synchronously, which is what assigns _instance and marks the
            // object DontDestroyOnLoad — the assignment here is just belt and braces.
            var go = new GameObject(nameof(DailyOfferManager));
            _instance = go.AddComponent<DailyOfferManager>();
            return _instance;
        }
    }

    /// <summary>
    /// Spin the manager up before the first scene loads, so cooldowns are already loaded by the time a
    /// shop card asks whether its offer is claimable — otherwise the first card to render would be the
    /// one paying to construct it.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        _isQuitting = false; // Statics survive play sessions when domain reload is disabled
        _ = Instance;
    }

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired when an offer is claimed, and again when its cooldown expires and it refreshes.</summary>
    public static event Action OnOffersChanged;

    // ─── Private State ────────────────────────────────────────────────────────

    private const string SAVE_KEY = "DailyOffersState";
    private const float TICK_INTERVAL = 1f; // Seconds between cooldown-expiry checks

    private DailyOfferSaveData _state = new DailyOfferSaveData();
    private float _tickTimer;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        LoadState();
    }

    private void Update()
    {
        // Sweep expired cooldowns once per second so item cards flip back to "Watch Ad" on their own.
        _tickTimer += Time.unscaledDeltaTime;
        if (_tickTimer < TICK_INTERVAL) return;
        _tickTimer = 0f;

        if (PruneExpiredOffers())
        {
            SaveState();
            OnOffersChanged?.Invoke();
        }
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
        SaveState();
    }

    private void OnApplicationPause(bool paused) { if (paused) SaveState(); }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the offer can be claimed right now. Items that are not daily offers are
    /// always available.
    /// </summary>
    public bool IsOfferAvailable(ShopItemData data)
    {
        if (data == null || !data.isDailyOffer) return true;

        DailyOfferEntry entry = FindEntry(data.itemID);
        if (entry == null) return true;

        return DateTime.UtcNow.Ticks >= entry.availableAtTicks;
    }

    /// <summary>
    /// Time remaining until the offer refreshes, or <see cref="TimeSpan.Zero"/> if it is claimable now.
    /// </summary>
    public TimeSpan GetTimeUntilAvailable(ShopItemData data)
    {
        if (data == null || !data.isDailyOffer) return TimeSpan.Zero;

        DailyOfferEntry entry = FindEntry(data.itemID);
        if (entry == null) return TimeSpan.Zero;

        TimeSpan remaining = new DateTime(entry.availableAtTicks, DateTimeKind.Utc) - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Starts the cooldown on an offer the player just claimed. Call this only after the reward has
    /// actually been granted.
    /// </summary>
    public void MarkOfferClaimed(ShopItemData data)
    {
        if (data == null || !data.isDailyOffer) return;

        if (string.IsNullOrEmpty(data.itemID))
        {
            Debug.LogError($"[DailyOfferManager] Daily offer '{data.itemName}' has no itemID — its cooldown "
                + "cannot be tracked. Run 'Tools/Assign Shop Item IDs'.");
            return;
        }

        DateTime now = DateTime.UtcNow;
        double cooldownHours = data.offerCooldownHours;

        // Unity gives a newly-added serialized field its C# default on assets that were saved before the
        // field existed — so an older ShopItemData retyped into a daily offer arrives here with 0 hours,
        // which would let the player reclaim it immediately. Fall back to a day rather than give it away.
        if (cooldownHours <= 0.0)
        {
            Debug.LogWarning($"[DailyOfferManager] '{data.itemName}' has offerCooldownHours = "
                + $"{data.offerCooldownHours}, which would refresh instantly. Falling back to 24h. "
                + "Set a real cooldown on the asset.");
            cooldownHours = 24.0;
        }

        DailyOfferEntry entry = FindEntry(data.itemID);
        if (entry == null)
        {
            entry = new DailyOfferEntry { itemID = data.itemID };
            _state.entries.Add(entry);
        }

        entry.claimedAtTicks = now.Ticks;
        entry.availableAtTicks = now.AddHours(cooldownHours).Ticks;

        SaveState();
        OnOffersChanged?.Invoke();

        Debug.Log($"[DailyOfferManager] Claimed '{data.itemName}'. Refreshes in {cooldownHours}h "
            + $"(at {new DateTime(entry.availableAtTicks, DateTimeKind.Utc).ToLocalTime()}).");
    }

    /// <summary>
    /// The soonest moment at which some claimed offer becomes available again, in UTC.
    /// Returns <c>false</c> when nothing is on cooldown — there is then no future moment to announce,
    /// because everything is already claimable.
    /// Used by <see cref="MobileNotificationManager"/> to schedule the "your reward is ready" push.
    /// </summary>
    public bool TryGetNextRefreshTime(out DateTime nextRefreshUtc)
    {
        nextRefreshUtc = DateTime.MaxValue;
        bool found = false;

        foreach (var entry in _state.entries)
        {
            if (entry == null) continue;

            DateTime availableAt = new DateTime(entry.availableAtTicks, DateTimeKind.Utc);
            if (availableAt <= DateTime.UtcNow) continue; // Already claimable — the sweep will drop it

            if (!found || availableAt < nextRefreshUtc)
            {
                nextRefreshUtc = availableAt;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Clears every cooldown. Debug / testing helper.</summary>
    [ContextMenu("Reset All Offers")]
    public void ResetAllOffers()
    {
        _state.entries.Clear();
        SaveState();
        OnOffersChanged?.Invoke();
        Debug.Log("[DailyOfferManager] All daily offer cooldowns reset.");
    }

    /// <summary>Formats a cooldown for display on an item card: "23h 59m", "05m 12s", "12s".</summary>
    public static string FormatCooldown(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "Ready";

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours:00}h {span.Minutes:00}m";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes:00}m {span.Seconds:00}s";
        }

        return $"{span.Seconds}s";
    }

    // ─── Internal Helpers ─────────────────────────────────────────────────────

    private DailyOfferEntry FindEntry(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return null;

        foreach (var entry in _state.entries)
        {
            if (entry != null && entry.itemID == itemID) return entry;
        }

        return null;
    }

    /// <summary>Drops entries whose cooldown has elapsed. Returns true if anything was removed.</summary>
    private bool PruneExpiredOffers()
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        int removed = _state.entries.RemoveAll(e => e == null || nowTicks >= e.availableAtTicks);

        if (removed > 0)
        {
            Debug.Log($"[DailyOfferManager] {removed} daily offer(s) refreshed and are claimable again.");
        }

        return removed > 0;
    }

    private void SaveState()
    {
        SaveSystem.Save(SAVE_KEY, _state);
    }

    private void LoadState()
    {
        if (SaveSystem.Exists(SAVE_KEY))
        {
            _state = SaveSystem.Load<DailyOfferSaveData>(SAVE_KEY) ?? new DailyOfferSaveData();

            // Guard against a save written before this field existed.
            if (_state.entries == null) _state.entries = new List<DailyOfferEntry>();

            PruneExpiredOffers();
            Debug.Log($"[DailyOfferManager] Loaded {_state.entries.Count} offer(s) still on cooldown.");
        }
        else
        {
            _state = new DailyOfferSaveData();
            Debug.Log("[DailyOfferManager] No save found. All daily offers are claimable.");
        }
    }
}
