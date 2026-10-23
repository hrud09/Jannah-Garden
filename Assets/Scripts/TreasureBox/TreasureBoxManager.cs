using UnityEngine;
using System;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Singleton manager for the Jannah Garden Treasure Box daily-reward system.
///
/// Responsibilities:
///   • Runs one sequence of 4 tiers × 3 slots = 12 boxes per daily cycle.
///   • Enforces strict tier progression (Silver → Gold → Platinum → Diamond): all
///     3 boxes of a tier appear together the moment the previous tier is finished.
///   • Carries a partially opened tier over the daily reset; the new day's
///     sequence starts from Silver as soon as that tier is finished.
///   • Gates free-user opens behind a rewarded ad.
///   • Fires events for the UI layer to react to.
///   • Persists all state via <see cref="SaveSystem"/>.
/// </summary>
public class TreasureBoxManager : MonoBehaviour
{
    // ─── Constants ────────────────────────────────────────────────────────────

    public const int  SLOTS_PER_TIER      = 3;
    public const string SAVE_KEY          = "TreasureBoxState";

    /// <summary>
    /// Fallback spawn cooldown in hours when no reward data is available.
    /// The per-tier value from <see cref="TreasureBoxData.spawnCooldownHours"/> is used preferentially.
    /// </summary>
    public const double DEFAULT_SPAWN_COOLDOWN_HOURS = 2.0;

    /// <summary>
    /// Fallback cycle duration in hours when no reward data is available.
    /// The per-tier value from <see cref="TreasureBoxData.cycleDurationHours"/> is used preferentially.
    /// </summary>
    public const double DEFAULT_CYCLE_DURATION_HOURS = 24.0;

    // ─── Singleton ────────────────────────────────────────────────────────────

    public static TreasureBoxManager Instance { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired after a single box is successfully opened.
    /// Parameters: (tier, slotIndex, rewardData).
    /// </summary>
    public static event Action<TreasureBoxTier, int, TreasureBoxData> OnBoxOpened;

    /// <summary>
    /// Fired when all 3 boxes of a tier have been opened (set complete).
    /// Parameters: (tier, rewardData).
    /// </summary>
    public static event Action<TreasureBoxTier, TreasureBoxData> OnSetCompleted;

    /// <summary>
    /// Fired whenever the overall state changes so the UI can refresh timers,
    /// button states, lock overlays, etc.
    /// </summary>
    public static event Action OnStateChanged;

    // ─── Inspector Fields ─────────────────────────────────────────────────────

    [Header("Reward Data Assets")]
    [Tooltip("Drag the TreasureBoxRewardData SO for each tier here, in order.")]
    public TreasureBoxData silverBoxData;
    public TreasureBoxData goldBoxData;
    public TreasureBoxData platinumBoxData;
    public TreasureBoxData diamondBoxData;

    [Tooltip("The single database asset holding every treasure box reward item, used to resolve each " +
             "tier's TreasureBoxData.exclusiveRewardItemIDs back into TreasureBoxRewardItemData.")]
    public TreasureBoxRewardItemsData rewardsDatabase;

    [Header("Subscriber Setting")]
    [Tooltip("Subscribers bypass rewarded ads and open all available boxes instantly.")]
    public bool isSubscriber = false;

    [Header("Reset Configuration")]
    [Tooltip("x = hour (0-23), y = minute (0-59) for daily reset.")]
    public Vector2 dailyResetTime = new Vector2(0, 0);

    [Header("UI Data")]
    public TreasureBoxStatusUI treasureBoxStatusUi;

    [Header("Tier Colors")]
    public Color silverColor = new Color(0.9f, 0.9f, 0.9f); // Soft white
    public Color goldColor = new Color(1f, 0.75f, 0f);      // Warm amber
    public Color platinumColor = new Color(0.85f, 0.9f, 1f);// Platinum-blue
    public Color diamondColor = new Color(0.8f, 1f, 1f);    // Cyan-white

    public Color GetTierColor(TreasureBoxTier tier)
    {
        switch (tier)
        {
            case TreasureBoxTier.Silver: return silverColor;
            case TreasureBoxTier.Gold: return goldColor;
            case TreasureBoxTier.Platinum: return platinumColor;
            case TreasureBoxTier.Diamond: return diamondColor;
            default: return Color.white;
        }
    }

    [Header("Debug Timers (Live Updates)")]
    [SerializeField] private string[] silverTimers = new string[3];
    [SerializeField] private string[] goldTimers = new string[3];
    [SerializeField] private string[] platinumTimers = new string[3];
    [SerializeField] private string[] diamondTimers = new string[3];

    // ─── Service Injection ────────────────────────────────────────────────────

    /// <summary>
    /// Rewarded-ad service. Defaults to <see cref="NullAdService"/> so the
    /// system works without an ad SDK. Assign a real implementation at runtime
    /// (e.g., from your ad initialisation flow) before any box is opened.
    /// </summary>
    public IAdService AdService { get; set; } = new NullAdService();

    /// <summary>
    /// IAP service for the "unlock all 3 at once" purchase flow.
    /// Defaults to <see cref="NullIAPService"/>.
    /// </summary>
    public IIAPService IAPService { get; set; } = new NullIAPService();

    // ─── Private State ────────────────────────────────────────────────────────

    private TreasureBoxSaveData _saveData;

    [Header("Spawning")]
    [Tooltip("Parent transform for spawn points. Children will be used as spawn points.")]
    public Transform spawnPointsParent;
    public Terrain terrainReference;

    /// <summary>
    /// Marks a slot whose box stands on a spot picked inside an unlocked garden area rather than on one
    /// of the hand-placed spawn points. The position itself is in
    /// <see cref="TreasureBoxTierState.assignedSpawnPositions"/>.
    /// </summary>
    public const int GENERATED_SPAWN_POINT = -2;

    [Header("Spawn Placement (Unlocked Areas)")]
    [Tooltip("ON: boxes only ever appear inside garden areas the player has unlocked, spread across " +
             "them. OFF: the old behaviour - any hand-placed spawn point, anywhere in the garden. " +
             "Also falls back to the old behaviour in a scene with no GardenAreaManager.")]
    public bool restrictToUnlockedAreas = true;

    [Tooltip("Use the hand-placed spawn points (children of 'Spawn Points Parent') when one stands " +
             "inside the chosen unlocked area. Areas with no hand-placed point get a spot picked " +
             "automatically. OFF: every spot is picked automatically.")]
    public bool useHandPlacedPoints = true;

    [Tooltip("How far, in metres, an automatically picked spot must stay inside its area's edge - " +
             "keeps boxes off the roads and paths that separate the areas.")]
    [Range(0f, 10f)]
    public float areaEdgeMargin = 2f;

    [Tooltip("Steepest ground, in degrees, an automatically picked spot may be on.")]
    [Range(0f, 45f)]
    public float maxSpawnSlope = 20f;

    [Tooltip("Boxes waiting to be opened are kept at least this many metres apart. Relaxed " +
             "automatically when an area is too small to honour it.")]
    [Range(0f, 30f)]
    public float minDistanceBetweenBoxes = 6f;

    [Tooltip("Radius, in metres, around an automatically picked spot that must be free of placed " +
             "garden items and of colliders on the layers below.")]
    [Range(0.25f, 4f)]
    public float spawnClearRadius = 1f;

    [Tooltip("Colliders on these layers block an automatically picked spot (scenery, buildings, " +
             "placed items). Trigger colliders are always ignored.")]
    public LayerMask spawnBlockingLayers = ~0;

    /// <summary>Random samples tried per area, per relaxation pass, before giving up on that area.</summary>
    private const int GeneratedSpawnAttempts = 60;

    private List<Transform> spawnPoints = new List<Transform>();

    private class SpawnedBoxInfo
    {
        public TreasureBox boxScript;
        public TreasureBoxTier tier;
        public int slotIndex;
        public GameObject gameObject;
    }
    private List<SpawnedBoxInfo> _spawnedBoxes = new List<SpawnedBoxInfo>();

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // The survivor's own spawn points, terrain and HUD were destroyed with the scene it was
            // created in; this copy carries the live ones. Hand them over before going away, or the
            // treasure box system stays dead for the rest of the session once the player has been
            // to the Outer Garden and back.
            Instance.AdoptSceneReferences(this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CollectSpawnPoints();

        LoadState();
        TickCycleResets();
        OnStateChanged += CheckAndSpawnNewBoxes;
    }

    /// <summary>
    /// Takes over the scene-bound references of a newly loaded copy of this manager.
    ///
    /// <para>The manager itself is <c>DontDestroyOnLoad</c> because the box timers are global and must
    /// keep running across a scene change. Most of what it <em>points at</em> is not: the status HUD and
    /// the terrain belong to roots the load throws away. Without this the survivor spends the rest of
    /// the session holding destroyed references, which is what produced a null reference every frame
    /// from <see cref="UpdateTierUiData"/> and on every spawn check.</para>
    /// </summary>
    private void AdoptSceneReferences(TreasureBoxManager replacement)
    {
        if (replacement == null) return;

        // Only what has actually died is replaced. Some of this manager's references outlive a scene
        // load and some do not, depending on which root object they hang from: the spawn points are
        // its own children and travel with it, while the status panel (a HUD canvas) and the terrain
        // belong to roots that are not preserved. Taking the replacement's copy of something still
        // alive would be worse than useless — the replacement is about to be destroyed, so it would
        // trade a live reference for one that dies a frame later.
        if (spawnPointsParent == null && replacement.spawnPointsParent != null)
        {
            // Re-parented rather than merely referenced, for that same reason: left where it is, it
            // goes away with the copy that carried it.
            replacement.spawnPointsParent.SetParent(transform, worldPositionStays: true);
            spawnPointsParent = replacement.spawnPointsParent;
            CollectSpawnPoints();
        }

        if (terrainReference == null) terrainReference = replacement.terrainReference;
        if (treasureBoxStatusUi == null)
        {
            // Adopting another instance's HUD: nothing this object cached describes those labels.
            treasureBoxStatusUi = replacement.treasureBoxStatusUi;
            InvalidateHudCache();
        }

        // Every box that was standing is gone with the old scene. Forgetting them is what lets the
        // next spawn check put them back rather than believing they are still out there.
        _spawnedBoxes.Clear();

        // Left to the one-second tick rather than respawned here: this runs from the new copy's Awake,
        // where the object pool the boxes come from may not have woken yet.
    }

    private void CollectSpawnPoints()
    {
        spawnPoints.Clear();

        if (spawnPointsParent == null) return;

        foreach (Transform child in spawnPointsParent)
        {
            spawnPoints.Add(child);
        }
    }

    /// <summary>
    /// True while the garden this manager spawns into is actually loaded around it.
    ///
    /// <para>It outlives its scene by design, and in the Outer Garden there is no ground to put a box
    /// on and no HUD to write a timer into. Rather than letting every call guard itself, the whole
    /// per-frame job stops until the garden comes back.</para>
    /// </summary>
    private bool IsBoundToScene
    {
        get
        {
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null) return true;
            }

            // Spots inside unlocked areas need no hand-placed points at all; the area manager only
            // exists while the garden scene is loaded, so it is just as good a sign of being in it.
            return PlacementAreas != null;
        }
    }

    /// <summary>
    /// A localized string, or an empty one when the localization manager is not up.
    ///
    /// <para>Every lookup here goes through this rather than <c>LocalizationManager.Instance.Get</c>
    /// directly: this manager keeps running through scene loads, and a status line is not worth a
    /// null reference exception per frame.</para>
    /// </summary>
    private static string Loc(string key)
    {
        return LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : string.Empty;
    }

    private static string Loc(string key, params object[] args)
    {
        return LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key, args) : string.Empty;
    }

    private void Start()
    {
        CheckAndSpawnNewBoxes();
    }

    private float _spawnCheckTimer = 0f;

    private void Update()
    {
#if UNITY_EDITOR
        if (_saveData != null)
        {
            UpdateDebugTimers(TreasureBoxTier.Silver, silverTimers);
            UpdateDebugTimers(TreasureBoxTier.Gold, goldTimers);
            UpdateDebugTimers(TreasureBoxTier.Platinum, platinumTimers);
            UpdateDebugTimers(TreasureBoxTier.Diamond, diamondTimers);
        }
#endif
        _spawnCheckTimer += Time.deltaTime;
        if (_spawnCheckTimer >= 1f)
        {
            _spawnCheckTimer = 0f;

            // Timers keep running wherever the player is - they are real time, and a box that became
            // available while they were in the Outer Garden should be waiting when they come back.
            TickCycleResets();

            // Spawning and the HUD are not: both need the garden scene around them.
            if (IsBoundToScene) CheckAndSpawnNewBoxes();
        }

        UpdateTierUiData();
    }

    /// <summary>
    /// Every tier in progression order — the same order and contents
    /// <c>Enum.GetValues(typeof(TreasureBoxTier))</c> returns, held once instead of rebuilt per call.
    /// That call allocates a fresh array every time and, iterated with <c>foreach</c>, boxes each
    /// value; the sweeps below run often enough (one of them on every frame, behind the status HUD)
    /// for that to be steady garbage for no gain.
    /// </summary>
    private static readonly TreasureBoxTier[] AllTiers =
    {
        TreasureBoxTier.Silver,
        TreasureBoxTier.Gold,
        TreasureBoxTier.Platinum,
        TreasureBoxTier.Diamond
    };

    // ─── Status HUD write cache ───────────────────────────────────────────────
    //
    // UpdateTierUiData runs every frame, but everything it shows changes at most once a second.
    // Re-writing a label is not free: LocalizedRendering.SetText searches the transform for a shaped
    // child, re-reads layout components, re-runs the Arabic shaper and rebuilds the text mesh, and
    // even a plain TMP_Text.text assignment forces a mesh regeneration. These hold what was last
    // pushed to each label, so an unchanged value costs a string comparison instead.
    //
    // The locale is part of the key: a language switch changes every rendered string without changing
    // any of the values they were derived from.

    private AppLocale _hudLocale = (AppLocale)(-1);
    private string _hudName;
    private int _hudOpenedCount = int.MinValue;
    private string _hudTimer;
    private bool _hudTimerLocalized;

    /// <summary>Drops the cache so the next tick rewrites every label from scratch.</summary>
    private void InvalidateHudCache()
    {
        _hudName = null;
        _hudOpenedCount = int.MinValue;
        _hudTimer = null;
    }

    /// <summary>Writes the countdown label, skipping the rebuild when it already reads that way.</summary>
    private void SetHudTimer(TMP_Text label, string value, bool localized)
    {
        if (_hudTimerLocalized == localized && string.Equals(_hudTimer, value)) return;

        _hudTimer = value;
        _hudTimerLocalized = localized;

        if (localized) LocalizedRendering.SetText(label, value);
        else label.text = value;
    }

    private void UpdateTierUiData()
    {
        if (treasureBoxStatusUi == null || _saveData == null) return;

        AppLocale locale = LocalizationManager.Instance != null
            ? LocalizationManager.Instance.CurrentLocale
            : AppLocale.en;

        if (locale != _hudLocale)
        {
            _hudLocale = locale;
            InvalidateHudCache();
        }
        
        TreasureBoxTier upcomingTier = GetUpcomingTier();
        treasureBoxStatusUi.SetTier(upcomingTier);

        TreasureBoxTierState state = _saveData.GetTierState(upcomingTier);
        TreasureBoxData data = GetBoxData(upcomingTier);
        
        if (treasureBoxStatusUi.nameText != null && data != null)
        {
            string tierName = data.LocalizedTierDisplayName;
            if (!string.Equals(tierName, _hudName))
            {
                _hudName = tierName;
                LocalizedRendering.SetText(treasureBoxStatusUi.nameText, tierName);
            }
        }
            
        if (treasureBoxStatusUi.openedBoxCountText != null && state.openedCount != _hudOpenedCount)
        {
            _hudOpenedCount = state.openedCount;
            treasureBoxStatusUi.openedBoxCountText.text = $"{state.openedCount}/{SLOTS_PER_TIER}";
        }
            
        if (treasureBoxStatusUi.timerText != null)
        {
            if (!TryGetActiveTier(out _))
            {
                // Today's twelve are all opened; the next sequence starts at the daily reset.
                TimeSpan span = GetNextSequenceStartTime() - DateTime.Now;
                if (span.TotalSeconds > 0)
                {
                    SetHudTimer(treasureBoxStatusUi.timerText, FormatTimeSpan(span), false);
                }
                else
                {
                    SetHudTimer(treasureBoxStatusUi.timerText, Loc("treasurebox.status.resetting"), true);
                }
            }
            else if (!IsTierUnlocked(upcomingTier))
            {
                SetHudTimer(treasureBoxStatusUi.timerText, Loc("treasurebox.status.locked"), true);
            }
            else
            {
                SetHudTimer(treasureBoxStatusUi.timerText, Loc("treasurebox.status.available"), true);
            }
        }
    }

    /// <summary>
    /// The tier the HUD and minimap should point at: the active tier, or Diamond once the whole
    /// sequence has been opened and the player is waiting on the next daily reset.
    /// </summary>
    public TreasureBoxTier GetUpcomingTier()
    {
        if (_saveData == null) return TreasureBoxTier.Silver;

        return TryGetActiveTier(out TreasureBoxTier active) ? active : TreasureBoxTier.Diamond;
    }

    /// <summary>
    /// The tier whose boxes are out in the garden right now — the lowest tier of the current sequence
    /// that is not finished. False once all four are finished, until the next daily reset.
    /// </summary>
    public bool TryGetActiveTier(out TreasureBoxTier activeTier)
    {
        activeTier = TreasureBoxTier.Silver;
        if (_saveData == null) return false;

        foreach (TreasureBoxTier tier in AllTiers)
        {
            if (!_saveData.GetTierState(tier).IsSetComplete)
            {
                activeTier = tier;
                return true;
            }
        }

        activeTier = TreasureBoxTier.Diamond;
        return false;
    }

    private void OnApplicationQuit()  => SaveState();
    private void OnApplicationPause(bool paused) { if (paused) SaveState(); }

    // ─── Public Query API ─────────────────────────────────────────────────────

    public bool HasShownChestOnce => _saveData != null && _saveData.hasShownChestOnce;

    public void MarkChestShown()
    {
        if (_saveData != null && !_saveData.hasShownChestOnce)
        {
            _saveData.hasShownChestOnce = true;
            SaveState();
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Returns the <see cref="TreasureBoxData"/> associated with a tier.
    /// </summary>
    public TreasureBoxData GetBoxData(TreasureBoxTier tier)
    {
        switch (tier)
        {
            case TreasureBoxTier.Silver:   return silverBoxData;
            case TreasureBoxTier.Gold:     return goldBoxData;
            case TreasureBoxTier.Platinum: return platinumBoxData;
            case TreasureBoxTier.Diamond:  return diamondBoxData;
            default: return null;
        }
    }

    /// <summary>
    /// Returns the saved state struct for a given tier.
    /// </summary>
    public TreasureBoxTierState GetTierState(TreasureBoxTier tier)
        => _saveData.GetTierState(tier);

    public TreasureBoxRewardItemData GetCurrentCycleReward(TreasureBoxTier tier)
    {
        TreasureBoxData data = GetBoxData(tier);
        if (data == null || data.exclusiveRewardItemIDs == null || data.exclusiveRewardItemIDs.Count == 0) return null;

        TreasureBoxTierState state = _saveData.GetTierState(tier);
        bool hasValidPick = state.selectedRewardIndex >= 0 && state.selectedRewardIndex < data.exclusiveRewardItemIDs.Count;

        // A pick above the player's level is re-rolled, but only while none of the tier's boxes have
        // been opened — after that the pieces already handed out belong to it.
        bool rerollOverLevel = hasValidPick && state.openedCount == 0
                               && !IsRewardWithinPlayerLevel(data.exclusiveRewardItemIDs[state.selectedRewardIndex]);

        if (!hasValidPick || rerollOverLevel)
        {
            state.selectedRewardIndex = PickRewardIndexForPlayerLevel(data);
            SaveState();
        }

        return rewardsDatabase != null
            ? rewardsDatabase.FindByID(data.exclusiveRewardItemIDs[state.selectedRewardIndex])
            : null;
    }

    /// <summary>
    /// A random reward the player's level already allows, or any reward when none of them is.
    /// </summary>
    private int PickRewardIndexForPlayerLevel(TreasureBoxData data)
    {
        List<int> eligible = new List<int>();
        for (int i = 0; i < data.exclusiveRewardItemIDs.Count; i++)
        {
            if (IsRewardWithinPlayerLevel(data.exclusiveRewardItemIDs[i])) eligible.Add(i);
        }

        return eligible.Count > 0
            ? eligible[UnityEngine.Random.Range(0, eligible.Count)]
            : UnityEngine.Random.Range(0, data.exclusiveRewardItemIDs.Count);
    }

    private bool IsRewardWithinPlayerLevel(string itemID)
    {
        if (rewardsDatabase == null || PlayerXPManager.Instance == null) return true;

        TreasureBoxRewardItemData item = rewardsDatabase.FindByID(itemID);
        return item == null || PlayerXPManager.Instance.xpLevel >= item.unlockXPLevel;
    }

    /// <summary>
    /// Returns true if the given tier is unlocked for opening.
    ///
    /// Progression rule: only the active tier (see <see cref="TryGetActiveTier"/>)
    /// is open; tiers already finished in this sequence also count as unlocked.
    /// Higher tiers wait until every tier below them is finished.
    ///
    /// The player's XP level never blocks a tier: it is applied when the tier's reward is picked
    /// (see <see cref="GetCurrentCycleReward"/>). Gating here left the whole sequence stuck — boxes
    /// never spawned — whenever the random pick was above the player's level.
    ///
    /// IAP note: PurchaseTierUnlock calls bypass this check internally; do not
    /// call this before calling that flow.
    /// </summary>
    public bool IsTierUnlocked(TreasureBoxTier tier)
    {
        if (_saveData == null) return false;
        if (_saveData.GetTierState(tier).IsSetComplete) return true;

        return TryGetActiveTier(out TreasureBoxTier active) && active == tier;
    }

    /// <summary>
    /// Returns true if a specific slot is ready to be opened right now: it belongs to the active
    /// tier and has not been opened yet. There are no per-slot timers — all three boxes of a tier
    /// appear together.
    /// </summary>
    public bool IsSlotAvailable(TreasureBoxTier tier, int slotIndex)
    {
        if (_saveData == null) return false;
        if (slotIndex < 0 || slotIndex >= SLOTS_PER_TIER) return false;
        if (!TryGetActiveTier(out TreasureBoxTier active) || active != tier) return false;

        return !_saveData.GetTierState(tier).slotOpened[slotIndex];
    }

    /// <summary>
    /// When the next daily sequence (3 Silver boxes) becomes due: the next daily reset.
    /// </summary>
    public DateTime GetNextSequenceStartTime()
    {
        return GetCurrentCycleStart(DateTime.Now).AddDays(1);
    }

    public DateTime GetCurrentCycleStart(DateTime now)
    {
        DateTime resetTimeToday = now.Date.AddHours(dailyResetTime.x).AddMinutes(dailyResetTime.y);
        if (now < resetTimeToday)
        {
            return resetTimeToday.AddDays(-1);
        }
        return resetTimeToday;
    }

    /// <summary>
    /// Returns how many boxes across all tiers are currently available to open
    /// (i.e., unlocked, slot spawned, not yet opened).
    /// </summary>
    public int GetTotalAvailableBoxCount()
    {
        int count = 0;
        foreach (TreasureBoxTier tier in AllTiers)
        {
            if (!IsTierUnlocked(tier)) continue;
            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (IsSlotAvailable(tier, i)) count++;
            }
        }
        return count;
    }

    // ─── Open Flow ────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to open the specified box slot.
    ///
    /// For free users this will trigger a rewarded ad before completing the
    /// open. For subscribers the open happens immediately.
    ///
    /// <param name="onResult">
    ///   Callback when the attempt resolves.
    ///   bool = success; string = human-readable failure reason (or null on success).
    /// </param>
    /// </summary>
    public void TryOpenBox(TreasureBoxTier tier, int slotIndex, Action<bool, string> onResult = null)
    {
        // ── Progression gate ──────────────────────────────────────────────────
        if (!IsTierUnlocked(tier))
        {
            string msg = Loc("treasurebox.complete_previous_tier", GetPreviousTierName(tier));
            Debug.Log($"[TreasureBoxManager] Cannot open {tier} slot {slotIndex}: {msg}");
            
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(msg);
            }

            onResult?.Invoke(false, msg);
            return;
        }

        // ── Slot availability gate ────────────────────────────────────────────
        if (!IsSlotAvailable(tier, slotIndex))
        {
            // Past the progression gate, the only way a slot is unavailable is that it is already opened.
            string msg = $"{tier} slot {slotIndex} is already opened.";

            Debug.Log($"[TreasureBoxManager] Cannot open {tier} slot {slotIndex}: {msg}");
            onResult?.Invoke(false, msg);
            return;
        }

        // ── Subscriber fast-path ──────────────────────────────────────────────
        if (isSubscriber)
        {
            CompleteBoxOpen(tier, slotIndex, onResult);
            return;
        }

        // ── Free user: show rewarded ad ───────────────────────────────────────
        // Deliberately no IsAdReady gate before the call. Returning early here made the "request another
        // ad" path inside ShowRewardedAd unreachable, so a single failed load at startup left every later
        // box permanently unopenable. ShowRewardedAd reports false when it has nothing to show and asks
        // for a fresh ad on the way out, which lets the next attempt succeed.
        bool adWasReady = AdService.IsAdReady;

        AdService.ShowRewardedAd(earned =>
        {
            if (earned)
            {
                CompleteBoxOpen(tier, slotIndex, onResult);
                return;
            }

            string msg = adWasReady
                ? "Ad skipped — box not opened."
                : "Ad not ready. Please try again shortly.";
            Debug.Log($"[TreasureBoxManager] {msg}");
            onResult?.Invoke(false, msg);
        });
    }

    /// <summary>
    /// Initiates the real-money IAP flow to immediately unlock all 3 boxes
    /// of a specific tier, bypassing the standard progression order.
    /// On success, marks all 3 slots as opened and awards the set-completion reward.
    /// </summary>
    public void PurchaseTierUnlock(TreasureBoxTier tier, Action<bool, string> onResult = null)
    {
        IAPService.PurchaseTierUnlock(tier, success =>
        {
            if (!success)
            {
                onResult?.Invoke(false, "Purchase cancelled or failed.");
                return;
            }

            // Mark all 3 slots as immediately opened (no progression gate applied)
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            DateTime now = DateTime.UtcNow;

            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (!state.slotOpened[i])
                {
                    state.slotOpened[i] = true;
                    state.openedCount++;
                    
                    TreasureBoxData rd = GetBoxData(tier);
                    TreasureBoxRewardItemData rewardItem = GetCurrentCycleReward(tier);
                    GameObject puzzlePiecePrefab = null;
                    if (rd != null && rewardItem != null && rewardItem.puzzlePieces != null && i < rewardItem.puzzlePieces.Length)
                    {
                        puzzlePiecePrefab = rewardItem.puzzlePieces[i];
                    }
                    
                    // Destroy the spawned box
                    for (int j = _spawnedBoxes.Count - 1; j >= 0; j--)
                    {
                        if (_spawnedBoxes[j].tier == tier && _spawnedBoxes[j].slotIndex == i)
                        {
                            if (_spawnedBoxes[j].gameObject != null)
                            {
                                TreasureBox tb = _spawnedBoxes[j].boxScript;
                                if (tb != null) tb.StartCoroutine(tb.PlayOpenAnimation(puzzlePiecePrefab));
                                Objectpool.Instance.Despawn(_spawnedBoxes[j].gameObject, 2f);
                            }
                            _spawnedBoxes.RemoveAt(j);
                            break;
                        }
                    }
                    
                    OnBoxOpened?.Invoke(tier, i, GetBoxData(tier));
                }
            }

            if (state.cycleStartedAtTicks == 0L)
                state.cycleStartedAtTicks = now.Ticks;

            if (_saveData.globalCycleStartedAtTicks == 0L)
                _saveData.globalCycleStartedAtTicks = now.Ticks;

            SaveState();
            OnSetCompleted?.Invoke(tier, GetBoxData(tier));
            GrantSetCompletionReward(tier);

            // The purchase opens all 3 at once, so the per-box progress toasts would be three messages
            // fighting over one slot; report the finished set instead.
            ShowSetProgressToast(SLOTS_PER_TIER);

            // Finishing a carried-over tier is what lets the new day's sequence start.
            TickCycleResets();
            OnStateChanged?.Invoke();

            onResult?.Invoke(true, null);
        });
    }

    // ─── Internal Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Core logic that runs after the ad/subscription gate has been cleared.
    /// </summary>
    private void CompleteBoxOpen(TreasureBoxTier tier, int slotIndex, Action<bool, string> onResult)
    {
        TreasureBoxTierState state = _saveData.GetTierState(tier);
        DateTime now = DateTime.UtcNow;

        // Fetch reward data early — needed for cooldown calculation below.
        TreasureBoxData rewardData = GetBoxData(tier);

        // Record cycle start on the very first open for this tier
        if (state.cycleStartedAtTicks == 0L)
            state.cycleStartedAtTicks = now.Ticks;

        // Record global cycle start
        if (_saveData.globalCycleStartedAtTicks == 0L)
            _saveData.globalCycleStartedAtTicks = now.Ticks;

        // Mark this slot opened
        state.slotOpened[slotIndex] = true;
        state.openedCount++;

        // Read before TickCycleResets below can clear the tier: the toast reports the progress this
        // open just made, not whatever the state has rolled over to by the end of the method.
        int openedCountForToast = state.openedCount;

        // Grant per-box NC reward
        if (rewardData != null && rewardData.noorCoinPerBox > 0 && NoorCoinManager.Instance != null)
        {
            NoorCoinManager.Instance.Earn(rewardData.noorCoinPerBox);
            Debug.Log($"[TreasureBoxManager] Earned {rewardData.noorCoinPerBox} NC from {tier} box {slotIndex}.");
        }

        SaveState();

        Debug.Log($"[TreasureBoxManager] Opened {tier} slot {slotIndex}. " +
                  $"({state.openedCount}/{SLOTS_PER_TIER} this cycle)");

        GameObject puzzlePiecePrefab = null;
        TreasureBoxRewardItemData rewardItem = GetCurrentCycleReward(tier);
        if (rewardData != null && rewardItem != null && rewardItem.puzzlePieces != null && slotIndex < rewardItem.puzzlePieces.Length)
        {
            puzzlePiecePrefab = rewardItem.puzzlePieces[slotIndex];
        }

        // Destroy the spawned box
        for (int i = _spawnedBoxes.Count - 1; i >= 0; i--)
        {
            if (_spawnedBoxes[i].tier == tier && _spawnedBoxes[i].slotIndex == slotIndex)
            {
                if (_spawnedBoxes[i].gameObject != null)
                {
                    TreasureBox tb = _spawnedBoxes[i].boxScript;
                    if (tb != null) tb.StartCoroutine(tb.PlayOpenAnimation(puzzlePiecePrefab));
                    Objectpool.Instance.Despawn(_spawnedBoxes[i].gameObject, 2f);
                }
                _spawnedBoxes.RemoveAt(i);
                break;
            }
        }

        OnBoxOpened?.Invoke(tier, slotIndex, rewardData);

        // Check set completion
        if (state.IsSetComplete)
        {
            Debug.Log($"[TreasureBoxManager] {tier} set complete!");
            GrantSetCompletionReward(tier);
            OnSetCompleted?.Invoke(tier, rewardData);

            // If this was a tier carried over from an earlier day, the current day's sequence starts
            // right away, from Silver.
            TickCycleResets();
        }

        ShowSetProgressToast(openedCountForToast);

        OnStateChanged?.Invoke();
        onResult?.Invoke(true, null);
    }

    /// <summary>
    /// Toasts how far the player is through the current tier's 3-box set, so the reason to come back
    /// for the remaining boxes is stated at the moment a box is opened rather than only implied by the
    /// status HUD.
    /// </summary>
    private void ShowSetProgressToast(int openedCount)
    {
        if (ToastMessageManager.Instance == null) return;

        openedCount = Mathf.Clamp(openedCount, 1, SLOTS_PER_TIER);

        string key = openedCount >= SLOTS_PER_TIER
            ? "treasurebox.progress_all_complete"
            : "treasurebox.progress";

        ToastMessageManager.Instance.ShowToast(Loc(key, openedCount, SLOTS_PER_TIER));
    }

    /// <summary>
    /// Awards the set-completion reward: either unlocks the exclusive item
    /// (by setting its ShopItemState to Unlocked) or grants NC if already owned.
    /// </summary>
    private void GrantSetCompletionReward(TreasureBoxTier tier)
    {
        TreasureBoxData rewardData = GetBoxData(tier);
        if (rewardData == null) return;

        TreasureBoxRewardItemData reward = GetCurrentCycleReward(tier);
        if (reward == null)
        {
            Debug.LogWarning($"[TreasureBoxManager] No exclusive reward assigned to {tier} reward data.");
            return;
        }

        Debug.Log($"[TreasureBoxManager] Unlocked {tier} set reward: {reward.itemName}!");
        
        if (InventoryManager.Instance != null && !string.IsNullOrEmpty(reward.itemID))
        {
            int quantityToAdd = 1;
            InventoryManager.Instance.AddInventoryItem(reward.itemID, quantityToAdd);
            reward.quantity = InventoryManager.Instance.GetItemQuantity(reward.itemID);
        }
        else if (InventoryManager.Instance == null)
        {
            Debug.LogWarning("[TreasureBoxManager] InventoryManager instance not found. Cannot add item to inventory.");
        }
        else if (string.IsNullOrEmpty(reward.itemID))
        {
            Debug.LogWarning("[TreasureBoxManager] Reward item is missing an itemID! Cannot save to inventory.");
        }

        if (ItemPlacementManager.Instance != null)
        {
            ItemPlacementManager.Instance.PreparePlacement(reward);
        }
        else
        {
            Debug.LogWarning("[TreasureBoxManager] ItemPlacementManager instance not found. Cannot automatically place item.");
        }
    }

    /// <summary>
    /// Starts a new daily sequence once the daily reset has passed — unless the active tier has been
    /// partially opened. That tier is carried over, however many resets go by, and the sequence
    /// restarts from Silver the moment it is finished. Any untouched tiers left over from the old
    /// sequence are dropped.
    /// </summary>
    private void TickCycleResets()
    {
        if (_saveData == null) return;

        bool changed = false;
        DateTime currentCycleStart = GetCurrentCycleStart(DateTime.Now);

        if (_saveData.sequenceCycleStartTicks == 0L)
        {
            MigrateLegacySave(currentCycleStart);
            changed = true;
        }

        if (new DateTime(_saveData.sequenceCycleStartTicks) < currentCycleStart && !HasCarriedOverTier())
        {
            StartNewSequence(currentCycleStart);
            changed = true;
        }

        if (changed)
        {
            SaveState();
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>True while the active tier has some, but not all, of its boxes opened.</summary>
    private bool HasCarriedOverTier()
    {
        return TryGetActiveTier(out TreasureBoxTier active) && _saveData.GetTierState(active).openedCount > 0;
    }

    private void StartNewSequence(DateTime cycleStart)
    {
        foreach (TreasureBoxTier tier in AllTiers)
        {
            _saveData.GetTierState(tier).ResetCycle();
            RemoveSpawnedBoxesForTier(tier);
        }

        _saveData.sequenceCycleStartTicks = cycleStart.Ticks;
        Debug.Log($"[TreasureBoxManager] New daily sequence started for {cycleStart}.");
    }

    /// <summary>
    /// Brings a save from before the single-sequence model up to date. Those saves stamped and carried
    /// each tier on its own, so a higher tier could hold progress while a lower one had been reset.
    /// </summary>
    private void MigrateLegacySave(DateTime currentCycleStart)
    {
        long oldestReset = 0L;
        foreach (TreasureBoxTier tier in AllTiers)
        {
            long ticks = _saveData.GetTierState(tier).lastTierResetTicks;
            if (ticks > 0 && (oldestReset == 0L || ticks < oldestReset)) oldestReset = ticks;
        }
        _saveData.sequenceCycleStartTicks = oldestReset > 0 ? oldestReset : currentCycleStart.Ticks;

        // Progress on a tier means every tier below it was finished in that sequence. Marking them so
        // keeps the partially opened tier the active one, which is what carries it over.
        for (int t = AllTiers.Length - 1; t > 0; t--)
        {
            TreasureBoxTierState state = _saveData.GetTierState(AllTiers[t]);
            if (state.openedCount == 0 || state.IsSetComplete) continue;

            for (int lower = 0; lower < t; lower++)
            {
                TreasureBoxTierState lowerState = _saveData.GetTierState(AllTiers[lower]);
                for (int i = 0; i < SLOTS_PER_TIER; i++) lowerState.slotOpened[i] = true;
                lowerState.openedCount = SLOTS_PER_TIER;
            }
            break;
        }
    }

    private void RemoveSpawnedBoxesForTier(TreasureBoxTier tier)
    {
        for (int i = _spawnedBoxes.Count - 1; i >= 0; i--)
        {
            if (_spawnedBoxes[i].tier == tier)
            {
                if (_spawnedBoxes[i].gameObject != null)
                {
                    Objectpool.Instance.Despawn(_spawnedBoxes[i].gameObject);
                }
                _spawnedBoxes.RemoveAt(i);
            }
        }
    }

    public void PlayShowAnimationForTier(TreasureBoxTier tier)
    {
        MarkChestShown();

        TreasureBoxTierState state = _saveData.GetTierState(tier);
        if (state != null && state.IsSetComplete)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(Loc("treasurebox.all_opened"));
            }
            return;
        }

        Vector3? firstBoxPos = null;

        foreach (var boxInfo in _spawnedBoxes)
        {
            if (boxInfo.tier == tier && boxInfo.boxScript != null)
            {
                boxInfo.boxScript.StartCoroutine(boxInfo.boxScript.PlayShowAnimation());
                if (firstBoxPos == null && boxInfo.gameObject != null)
                {
                    firstBoxPos = boxInfo.gameObject.transform.position;
                }
            }
        }

        if (firstBoxPos.HasValue)
        {
            if (TargetDirectionController.Instance != null)
            {
                TargetDirectionController.Instance.PointTo(firstBoxPos.Value, 10f);
            }
        }
        else
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(Loc("treasurebox.not_appeared_yet"));
            }
        }
    }
    
    private void CheckAndSpawnNewBoxes()
    {
        // _saveData is null until LoadState has run, and this is also reached from the OnStateChanged
        // event, which anything may raise at any point in a load.
        if (_saveData == null || spawnPoints == null) return;
        if (spawnPoints.Count == 0 && PlacementAreas == null) return;
        
        foreach (TreasureBoxTier tier in AllTiers)
        {
            TreasureBoxData rd = GetBoxData(tier);
            if (rd == null || rd.boxPrefab == null) continue;
            
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (IsTierUnlocked(tier) && IsSlotAvailable(tier, i) && !_spawnedBoxes.Exists(b => b.tier == tier && b.slotIndex == i))
                {
                    SpawnBox(rd.boxPrefab, tier, i);
                }
            }
        }
    }

    // ─── Spawn Placement ──────────────────────────────────────────────────────
    //
    // Where each box goes is decided once, when its slot is first spawned, and saved with the slot so
    // the box is standing in the same place every time the garden loads until it is opened.
    //
    // With a GardenAreaManager in the scene, a slot is only ever given ground the player has unlocked:
    // an unlocked area is picked first (weighted by size, and away from areas already holding a waiting
    // box so the boxes spread out), then a spot inside it - one of the hand-placed spawn points when one
    // stands there, otherwise a spot picked from the baked area map. As more areas unlock, later boxes
    // start appearing in them on their own.
    //
    // Without one (a test scene, or the feature switched off) it is the original behaviour: any free
    // hand-placed spawn point.

    /// <summary>The area manager to place by, or null when boxes are not restricted to unlocked areas.</summary>
    private GardenAreaManager PlacementAreas
    {
        get
        {
            if (!restrictToUnlockedAreas) return null;
            GardenAreaManager areas = GardenAreaManager.Instance;
            return areas != null && areas.IsReady ? areas : null;
        }
    }

    /// <summary>The terrain heights and slopes are read from.</summary>
    private Terrain SpawnTerrain
    {
        get
        {
            if (terrainReference != null) return terrainReference;
            if (GardenGrid.Instance != null && GardenGrid.Instance.terrain != null) return GardenGrid.Instance.terrain;
            return Terrain.activeTerrain;
        }
    }

    /// <summary>True when <paramref name="world"/> lies in an area the player has unlocked (always, without area rules).</summary>
    private bool IsInUnlockedArea(Vector3 world)
    {
        GardenAreaManager areas = PlacementAreas;
        if (areas == null) return true;

        GardenAreaDefinition area = areas.AreaAt(world);
        return area != null && areas.IsUnlocked(area);
    }

    /// <summary>
    /// True when the slot already has a spot that may still be used. A spot stops being usable when its
    /// hand-placed point has gone, or when it is not on unlocked ground - which is how a save made
    /// before this rule existed (boxes sitting in locked areas) gets moved onto the player's own ground.
    /// </summary>
    private bool IsAssignedSpawnUsable(TreasureBoxTierState state, int slotIndex)
    {
        int assigned = state.assignedSpawnPoints[slotIndex];

        if (assigned == GENERATED_SPAWN_POINT)
        {
            return PlacementAreas != null && IsInUnlockedArea(state.assignedSpawnPositions[slotIndex]);
        }

        if (assigned < 0 || assigned >= spawnPoints.Count || spawnPoints[assigned] == null) return false;

        // With area rules active, a hand-placed point is only kept while hand-placed points are in use
        // and it stands on unlocked ground.
        if (PlacementAreas != null && (!useHandPlacedPoints || !IsInUnlockedArea(spawnPoints[assigned].position)))
            return false;

        return true;
    }

    /// <summary>World position and rotation of the slot's assigned spot, before it is dropped onto the ground.</summary>
    private bool TryResolveAssignedSpawn(TreasureBoxTierState state, int slotIndex, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        int assigned = state.assignedSpawnPoints[slotIndex];

        if (assigned == GENERATED_SPAWN_POINT)
        {
            position = state.assignedSpawnPositions[slotIndex];
            rotation = GeneratedSpawnRotation(position);
            return true;
        }

        if (assigned < 0 || assigned >= spawnPoints.Count || spawnPoints[assigned] == null) return false;

        position = spawnPoints[assigned].position;
        rotation = spawnPoints[assigned].rotation;
        return true;
    }

    /// <summary>
    /// A facing for a generated spot. Derived from the position rather than random so the box faces the
    /// same way on every load without the rotation needing a save field of its own.
    /// </summary>
    private static Quaternion GeneratedSpawnRotation(Vector3 position)
    {
        float yaw = Mathf.Repeat(position.x * 37.13f + position.z * 91.71f, 360f);
        return Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>
    /// Where every other box still waiting to be opened stands, and which hand-placed points they hold,
    /// so a new spot keeps its distance and never doubles up on a point.
    /// </summary>
    private void CollectReservedSpawns(TreasureBoxTier exceptTier, int exceptSlot,
        List<Vector3> reservedPositions, HashSet<int> reservedPoints)
    {
        foreach (TreasureBoxTier tier in AllTiers)
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            if (state.assignedSpawnPoints == null) continue;

            for (int slot = 0; slot < SLOTS_PER_TIER; slot++)
            {
                if (tier == exceptTier && slot == exceptSlot) continue;
                if (state.slotOpened[slot]) continue;

                // A stale spot (say, a box from an old save sitting in a locked area) is about to be
                // replaced, so it must not push the new spots away from where it stood.
                if (!IsAssignedSpawnUsable(state, slot)) continue;

                int assigned = state.assignedSpawnPoints[slot];
                if (assigned >= 0) reservedPoints.Add(assigned);

                if (TryResolveAssignedSpawn(state, slot, out Vector3 pos, out _)) reservedPositions.Add(pos);
            }
        }
    }

    private static bool IsFarFromAll(Vector3 candidate, List<Vector3> others, float minDistance)
    {
        if (minDistance <= 0f) return true;

        float minSqr = minDistance * minDistance;
        for (int i = 0; i < others.Count; i++)
        {
            float dx = candidate.x - others[i].x;
            float dz = candidate.z - others[i].z;
            if (dx * dx + dz * dz < minSqr) return false;
        }

        return true;
    }

    /// <summary>
    /// Gives the slot a new spot and records it in the save. False when there is nowhere to put it.
    /// The caller saves.
    /// </summary>
    private bool TryAssignSpawn(TreasureBoxTier tier, int slotIndex)
    {
        TreasureBoxTierState state = _saveData.GetTierState(tier);
        state.assignedSpawnPoints[slotIndex] = -1;

        GardenAreaManager areas = PlacementAreas;

        if (areas == null)
        {
            // No area rules: the original behaviour, any free hand-placed point.
            int legacyIndex = GetAvailableSpawnPointIndex();
            if (legacyIndex == -1) return false;

            state.assignedSpawnPoints[slotIndex] = legacyIndex;
            return true;
        }

        var reservedPositions = new List<Vector3>();
        var reservedPoints = new HashSet<int>();
        CollectReservedSpawns(tier, slotIndex, reservedPositions, reservedPoints);

        List<GardenAreaDefinition> ordered = OrderUnlockedAreasForSpawn(areas, reservedPositions);
        if (ordered.Count == 0)
        {
            Debug.LogWarning("[TreasureBoxManager] No unlocked garden area to put a treasure box in.");
            return false;
        }

        // Each pass is a little less fussy than the last, so a cramped garden still gets its box rather
        // than none: first the full spacing, then half of it, then none at all.
        float[] spacingPasses = { minDistanceBetweenBoxes, minDistanceBetweenBoxes * 0.5f, 0f };

        foreach (float spacing in spacingPasses)
        {
            foreach (GardenAreaDefinition area in ordered)
            {
                if (useHandPlacedPoints &&
                    TryPickHandPlacedPointIn(areas, area, reservedPoints, reservedPositions, spacing, out int pointIndex))
                {
                    state.assignedSpawnPoints[slotIndex] = pointIndex;
                    return true;
                }

                if (TryGenerateSpotIn(areas, area, reservedPositions, spacing, out Vector3 spot))
                {
                    state.assignedSpawnPoints[slotIndex] = GENERATED_SPAWN_POINT;
                    state.assignedSpawnPositions[slotIndex] = spot;
                    return true;
                }
            }
        }

        // Everything was refused (every spot blocked by scenery, say). The deepest point of the first
        // choice of area is still on unlocked ground and well clear of its roads.
        GardenAreaDefinition fallbackArea = ordered[0];
        Vector3 anchor = new Vector3(fallbackArea.labelAnchor.x, 0f, fallbackArea.labelAnchor.y);
        anchor.y = SampleGroundHeight(anchor);

        Debug.LogWarning($"[TreasureBoxManager] Found no clear spot for {tier} slot {slotIndex} - using the " +
                         $"centre of area {fallbackArea.number}. Check 'Spawn Blocking Layers' if this keeps happening.");

        state.assignedSpawnPoints[slotIndex] = GENERATED_SPAWN_POINT;
        state.assignedSpawnPositions[slotIndex] = anchor;
        return true;
    }

    /// <summary>
    /// The unlocked areas in the order to try them: a random draw weighted by ground area, with areas
    /// already holding a waiting box pushed down so the three boxes of a tier spread across the garden.
    /// </summary>
    private List<GardenAreaDefinition> OrderUnlockedAreasForSpawn(GardenAreaManager areas, List<Vector3> reservedPositions)
    {
        var pool = new List<GardenAreaDefinition>();
        var weights = new List<float>();

        foreach (GardenAreaDefinition area in areas.map.areas)
        {
            if (area == null || !areas.IsUnlocked(area)) continue;

            int boxesAlreadyThere = 0;
            for (int i = 0; i < reservedPositions.Count; i++)
            {
                if (areas.AreaAt(reservedPositions[i]) == area) boxesAlreadyThere++;
            }

            float weight = Mathf.Max(1f, area.squareMetres) * Mathf.Pow(0.2f, boxesAlreadyThere);
            pool.Add(area);
            weights.Add(weight);
        }

        var ordered = new List<GardenAreaDefinition>(pool.Count);
        while (pool.Count > 0)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i];

            float roll = UnityEngine.Random.value * total;
            int pick = pool.Count - 1;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f) { pick = i; break; }
            }

            ordered.Add(pool[pick]);
            pool.RemoveAt(pick);
            weights.RemoveAt(pick);
        }

        return ordered;
    }

    /// <summary>A free hand-placed spawn point standing inside <paramref name="area"/>, if there is one.</summary>
    private bool TryPickHandPlacedPointIn(GardenAreaManager areas, GardenAreaDefinition area,
        HashSet<int> reservedPoints, List<Vector3> reservedPositions, float spacing, out int pointIndex)
    {
        pointIndex = -1;
        var candidates = new List<int>();

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] == null || reservedPoints.Contains(i)) continue;

            Vector3 pos = spawnPoints[i].position;
            if (areas.AreaAt(pos) != area) continue;
            if (!IsFarFromAll(pos, reservedPositions, spacing)) continue;

            candidates.Add(i);
        }

        if (candidates.Count == 0) return false;

        pointIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        return true;
    }

    /// <summary>
    /// A random spot inside <paramref name="area"/>, read off the baked area map: clear of the area's
    /// edges (so off the roads), on gentle ground, free of placed items and scenery, and at least
    /// <paramref name="spacing"/> from every other waiting box.
    /// </summary>
    private bool TryGenerateSpotIn(GardenAreaManager areas, GardenAreaDefinition area,
        List<Vector3> reservedPositions, float spacing, out Vector3 spot)
    {
        spot = Vector3.zero;

        GardenAreaMap map = areas.map;
        int slot = map.areas.IndexOf(area) + 1;
        if (slot <= 0 || slot > byte.MaxValue) return false;

        // Every cell of this area. One pass over the grid; this only runs when a box is first given its
        // spot, a handful of times a day.
        var cells = new List<int>();
        byte slotByte = (byte)slot;
        for (int i = 0; i < map.cells.Length; i++)
        {
            if (map.cells[i] == slotByte) cells.Add(i);
        }

        if (cells.Count == 0) return false;

        float cellSize = map.CellSize;
        int attempts = Mathf.Min(GeneratedSpawnAttempts, cells.Count);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int cellIndex = cells[UnityEngine.Random.Range(0, cells.Count)];
            int cx = cellIndex % map.resolution;
            int cy = cellIndex / map.resolution;

            Vector3 candidate = map.CellCentre(cx, cy);
            candidate.x += UnityEngine.Random.Range(-0.5f, 0.5f) * cellSize;
            candidate.z += UnityEngine.Random.Range(-0.5f, 0.5f) * cellSize;

            if (!IsFarFromAll(candidate, reservedPositions, spacing)) continue;
            if (!IsWellInsideArea(map, area, candidate)) continue;

            candidate.y = SampleGroundHeight(candidate);

            if (!IsGentleGround(candidate)) continue;
            if (!IsClearOfPlacedItems(candidate)) continue;
            if (!IsClearOfScenery(candidate)) continue;

            spot = candidate;
            return true;
        }

        return false;
    }

    /// <summary>True when the ground within <see cref="areaEdgeMargin"/> all round still belongs to <paramref name="area"/>.</summary>
    private bool IsWellInsideArea(GardenAreaMap map, GardenAreaDefinition area, Vector3 point)
    {
        if (map.AreaAt(point) != area) return false;
        if (areaEdgeMargin <= 0f) return true;

        const int Samples = 8;
        for (int i = 0; i < Samples; i++)
        {
            float angle = i * Mathf.PI * 2f / Samples;
            Vector3 probe = new Vector3(
                point.x + Mathf.Cos(angle) * areaEdgeMargin,
                point.y,
                point.z + Mathf.Sin(angle) * areaEdgeMargin);

            if (map.AreaAt(probe) != area) return false;
        }

        return true;
    }

    private float SampleGroundHeight(Vector3 world)
    {
        Terrain terrain = SpawnTerrain;
        return terrain != null ? terrain.SampleHeight(world) + terrain.transform.position.y : world.y;
    }

    private bool IsGentleGround(Vector3 world)
    {
        Terrain terrain = SpawnTerrain;
        TerrainData data = terrain != null ? terrain.terrainData : null;
        if (data == null) return true;

        Vector3 local = world - terrain.transform.position;
        float u = Mathf.Clamp01(local.x / data.size.x);
        float v = Mathf.Clamp01(local.z / data.size.z);

        return Vector3.Angle(data.GetInterpolatedNormal(u, v), Vector3.up) <= maxSpawnSlope;
    }

    /// <summary>True when no item the player has placed covers the ground around <paramref name="world"/>.</summary>
    private bool IsClearOfPlacedItems(Vector3 world)
    {
        GardenGrid grid = GardenGrid.Instance;
        if (grid == null || !grid.IsReady) return true;

        int span = Mathf.Max(1, Mathf.CeilToInt(spawnClearRadius * 2f / grid.CellSize));
        return grid.IsAreaFree(grid.AreaCovering(world, new Vector2Int(span, span)));
    }

    /// <summary>
    /// True when nothing solid stands where the box would. The test sphere sits just above the ground so
    /// the terrain under it is never what blocks it.
    /// </summary>
    private bool IsClearOfScenery(Vector3 groundPoint)
    {
        if (spawnBlockingLayers.value == 0) return true;

        Vector3 centre = groundPoint + Vector3.up * (spawnClearRadius + 0.5f);
        return !Physics.CheckSphere(centre, spawnClearRadius, spawnBlockingLayers, QueryTriggerInteraction.Ignore);
    }

    private int GetAvailableSpawnPointIndex()
    {
        if (spawnPoints == null || spawnPoints.Count == 0) return -1;
        
        List<int> availableIndices = new List<int>();
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] != null) availableIndices.Add(i);
        }

        foreach (TreasureBoxTier tier in AllTiers)
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            if (state.assignedSpawnPoints == null) continue;
            for (int slot = 0; slot < SLOTS_PER_TIER; slot++)
            {
                if (!state.slotOpened[slot] && state.assignedSpawnPoints[slot] >= 0)
                {
                    availableIndices.Remove(state.assignedSpawnPoints[slot]);
                }
            }
        }

        if (availableIndices.Count == 0) return -1;
        return availableIndices[UnityEngine.Random.Range(0, availableIndices.Count)];
    }

    private void SpawnBox(GameObject prefab, TreasureBoxTier tier, int slotIndex)
    {
        // The pool lives in the garden scene, so it is absent for as long as the player is elsewhere -
        // and for the first frames of a load, before its own Awake has run. Neither is worth an
        // exception: the spawn check runs again a second later, by which time it is there. Checked
        // before a spot is picked, so nothing is decided (and saved) while the garden and its area map
        // are not actually loaded.
        if (Objectpool.Instance == null)
        {
            Debug.LogWarning("[TreasureBoxManager] No Objectpool yet - deferring the " + tier
                             + " slot " + slotIndex + " spawn to the next check.");
            return;
        }

        TreasureBoxTierState state = _saveData.GetTierState(tier);
        
        // Pick a spot when the slot has none yet, or when the one it has is no longer on unlocked ground.
        if (!IsAssignedSpawnUsable(state, slotIndex))
        {
            if (!TryAssignSpawn(tier, slotIndex))
            {
                Debug.LogWarning($"[TreasureBoxManager] No available spawn spot for {tier} slot {slotIndex}");
                SaveState();
                return;
            }
            SaveState();
        }

        if (!TryResolveAssignedSpawn(state, slotIndex, out Vector3 spawnPos, out Quaternion spawnRot))
        {
            Debug.LogWarning("[TreasureBoxManager] Assigned spawn point is missing or invalid.");
            return;
        }
        
        if (SpawnTerrain != null)
        {
            spawnPos = new Vector3(spawnPos.x, SampleGroundHeight(spawnPos) + 2f, spawnPos.z);
        }

        GameObject go = Objectpool.Instance.Spawn(prefab, spawnPos, spawnRot);

        if (go == null)
        {
            Debug.LogWarning("[TreasureBoxManager] Pool returned nothing for " + tier + " slot " + slotIndex + ".");
            return;
        }

        go.name = $"TreasureBox_{tier}_Slot{slotIndex}";
        
        TreasureBox boxScript = go.GetComponent<TreasureBox>();
        if (boxScript != null)
        {
            boxScript.tier = tier;
            boxScript.slotIndex = slotIndex;
            
            if (boxScript.nameText != null)
            {
                TreasureBoxData boxData = GetBoxData(tier);
                if (boxData != null)
                {
                    boxScript.boxData = boxData;
                    LocalizedRendering.SetText(boxScript.nameText, boxData.LocalizedTierDisplayName);
                }
            }
            _spawnedBoxes.Add(new SpawnedBoxInfo { boxScript = boxScript, tier = tier, slotIndex = slotIndex, gameObject = go });
        }
        else
        {
            _spawnedBoxes.Add(new SpawnedBoxInfo { boxScript = null, tier = tier, slotIndex = slotIndex, gameObject = go });
        }
    }

    private void SaveState()
    {
        SaveSystem.Save(SAVE_KEY, _saveData);
        Debug.Log("[TreasureBoxManager] State saved.");
    }

    private void LoadState()
    {
        if (SaveSystem.Exists(SAVE_KEY))
        {
            _saveData = SaveSystem.Load<TreasureBoxSaveData>(SAVE_KEY);
            Debug.Log("[TreasureBoxManager] Save data loaded.");
        }
        else
        {
            _saveData = new TreasureBoxSaveData();
            Debug.Log("[TreasureBoxManager] No save found. Starting fresh.");
        }

        // Guard: ensure per-slot arrays are initialized (handles saves from older versions)
        foreach (TreasureBoxTier tier in AllTiers)
        {
            TreasureBoxTierState s = _saveData.GetTierState(tier);
            if (s.slotOpened == null || s.slotOpened.Length != SLOTS_PER_TIER)
                s.slotOpened = new bool[SLOTS_PER_TIER];
            if (s.slotAvailableAtTicks == null || s.slotAvailableAtTicks.Length != SLOTS_PER_TIER)
                s.slotAvailableAtTicks = new long[SLOTS_PER_TIER];
            if (s.assignedSpawnPoints == null || s.assignedSpawnPoints.Length != SLOTS_PER_TIER)
                s.assignedSpawnPoints = new int[] { -1, -1, -1 };
            if (s.assignedSpawnPositions == null || s.assignedSpawnPositions.Length != SLOTS_PER_TIER)
            {
                // Saves from before generated spots existed: none of their slots can be pointing at one.
                s.assignedSpawnPositions = new SerializableVector3[SLOTS_PER_TIER];
                for (int i = 0; i < SLOTS_PER_TIER; i++)
                {
                    if (s.assignedSpawnPoints[i] == GENERATED_SPAWN_POINT) s.assignedSpawnPoints[i] = -1;
                }
            }
        }
    }

    private static string GetPreviousTierName(TreasureBoxTier tier)
    {
        int prev = (int)tier - 1;
        return prev >= 0 ? TreasureBoxData.GetLocalizedShortTierName((TreasureBoxTier)prev) : "N/A";
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return Loc("common.now");
        return span.Hours > 0
            ? $"{span.Hours:D2}h {span.Minutes:D2}m {span.Seconds:D2}s"
            : $"{span.Minutes:D2}m {span.Seconds:D2}s";
    }

    // ─── Debug / Editor Helpers ───────────────────────────────────────────────

#if UNITY_EDITOR
    private void UpdateDebugTimers(TreasureBoxTier tier, string[] timerArray)
    {
        for (int i = 0; i < SLOTS_PER_TIER; i++)
        {
            if (_saveData.GetTierState(tier).slotOpened[i])
            {
                timerArray[i] = "Opened";
            }
            else if (IsSlotAvailable(tier, i))
            {
                timerArray[i] = "Available";
            }
            else if (!TryGetActiveTier(out _))
            {
                timerArray[i] = FormatTimeSpan(GetNextSequenceStartTime() - DateTime.Now);
            }
            else
            {
                timerArray[i] = "Waiting on previous tier...";
            }
        }
    }
#endif

    [ContextMenu("DEBUG — Reset All Boxes")]
    private void Debug_ResetAllBoxes()
    {
        _saveData = new TreasureBoxSaveData();
        SaveState();
        OnStateChanged?.Invoke();
        Debug.Log("[TreasureBoxManager] DEBUG: All boxes reset.");
    }

    [ContextMenu("DEBUG — Complete Silver Tier")]
    private void Debug_CompleteSilver()
    {
        Debug_CompleteTier(TreasureBoxTier.Silver);
    }

    [ContextMenu("DEBUG — Complete Gold Tier")]
    private void Debug_CompleteGold()
    {
        Debug_CompleteTier(TreasureBoxTier.Gold);
    }

    [ContextMenu("DEBUG — Complete Platinum Tier")]
    private void Debug_CompletePlatinum()
    {
        Debug_CompleteTier(TreasureBoxTier.Platinum);
    }

    [ContextMenu("DEBUG — Simulate Daily Reset")]
    private void Debug_SimulateDailyReset()
    {
        // Backdates the current sequence by a day, so the next tick treats the reset as having passed.
        _saveData.sequenceCycleStartTicks = new DateTime(_saveData.sequenceCycleStartTicks).AddDays(-1).Ticks;
        TickCycleResets();
        SaveState();
        OnStateChanged?.Invoke();
        Debug.Log("[TreasureBoxManager] DEBUG: Daily reset simulated.");
    }

    [ContextMenu("DEBUG — Re-pick Box Spawn Spots")]
    private void Debug_RepickSpawnSpots()
    {
        if (_saveData == null) return;

        foreach (TreasureBoxTier tier in AllTiers)
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (!state.slotOpened[i]) state.assignedSpawnPoints[i] = -1;
            }
            RemoveSpawnedBoxesForTier(tier);
        }

        SaveState();
        CheckAndSpawnNewBoxes();
        Debug.Log("[TreasureBoxManager] DEBUG: Unopened boxes given new spawn spots.");
    }

#if UNITY_EDITOR
    /// <summary>Shows where every waiting box has been placed, with the clear radius it was checked against.</summary>
    private void OnDrawGizmosSelected()
    {
        if (_saveData == null) return;

        foreach (TreasureBoxTier tier in AllTiers)
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            if (state.assignedSpawnPoints == null || state.assignedSpawnPositions == null) continue;

            Gizmos.color = GetTierColor(tier);
            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (state.slotOpened[i]) continue;
                if (!TryResolveAssignedSpawn(state, i, out Vector3 pos, out _)) continue;

                pos.y = SampleGroundHeight(pos);
                Gizmos.DrawWireSphere(pos + Vector3.up * (spawnClearRadius + 0.5f), spawnClearRadius);
                Gizmos.DrawLine(pos, pos + Vector3.up * 6f);
            }
        }
    }
#endif

    private void Debug_CompleteTier(TreasureBoxTier tier)
    {
        TreasureBoxTierState state = _saveData.GetTierState(tier);
        DateTime now = DateTime.UtcNow;
        if (state.cycleStartedAtTicks == 0L) state.cycleStartedAtTicks = now.Ticks;

        for (int i = 0; i < SLOTS_PER_TIER; i++)
        {
            state.slotOpened[i] = true;
        }
        state.openedCount = SLOTS_PER_TIER;
        RemoveSpawnedBoxesForTier(tier);
        TickCycleResets();
        SaveState();
        OnStateChanged?.Invoke();
        Debug.Log($"[TreasureBoxManager] DEBUG: {tier} tier force-completed.");
    }
}

