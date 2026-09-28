using UnityEngine;
using System;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Singleton manager for the Jannah Garden Treasure Box daily-reward system.
///
/// Responsibilities:
///   • Tracks 4 tiers × 3 slots = 12 boxes per 24-hour cycle.
///   • Enforces strict tier progression (Silver → Gold → Platinum → Diamond).
///   • Manages 2-hour per-slot respawn cooldowns.
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
        if (treasureBoxStatusUi == null) treasureBoxStatusUi = replacement.treasureBoxStatusUi;

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

            return false;
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

    private void UpdateTierUiData()
    {
        if (treasureBoxStatusUi == null || _saveData == null) return;
        
        TreasureBoxTier upcomingTier = GetUpcomingTier();
        treasureBoxStatusUi.SetTier(upcomingTier);

        TreasureBoxTierState state = _saveData.GetTierState(upcomingTier);
        TreasureBoxData data = GetBoxData(upcomingTier);
        
        if (treasureBoxStatusUi.nameText != null && data != null)
        {
            LocalizedRendering.SetText(treasureBoxStatusUi.nameText, data.LocalizedTierDisplayName);
        }
            
        if (treasureBoxStatusUi.openedBoxCountText != null)
        {
            treasureBoxStatusUi.openedBoxCountText.text = $"{state.openedCount}/{SLOTS_PER_TIER}";
        }
            
        if (treasureBoxStatusUi.timerText != null)
        {
            if (state.pendingResetAvailableAtTicks > 0)
            {
                TimeSpan span = new DateTime(state.pendingResetAvailableAtTicks) - DateTime.Now;
                if (span.TotalSeconds > 0)
                {
                    treasureBoxStatusUi.timerText.text = FormatTimeSpan(span);
                }
                else
                {
                    LocalizedRendering.SetText(treasureBoxStatusUi.timerText, Loc("treasurebox.status.resetting"));
                }
            }
            else if (state.IsSetComplete)
            {
                LocalizedRendering.SetText(treasureBoxStatusUi.timerText, Loc("treasurebox.status.completed"));
            }
            else if (!IsTierUnlocked(upcomingTier))
            {
                DateTime readyAt = GetSlotAvailableAt(upcomingTier, 0);
                if (readyAt != DateTime.MinValue)
                {
                    TimeSpan span = readyAt - DateTime.Now;
                    if (span.TotalSeconds > 0)
                    {
                        treasureBoxStatusUi.timerText.text = FormatTimeSpan(span);
                    }
                    else
                    {
                        LocalizedRendering.SetText(treasureBoxStatusUi.timerText, Loc("treasurebox.status.finish_previous_tier"));
                    }
                }
                else
                {
                    LocalizedRendering.SetText(treasureBoxStatusUi.timerText, Loc("treasurebox.status.locked"));
                }
            }
            else
            {
                // Tier is unlocked but not complete. Find the NEXT slot to open.
                int nextSlot = 0;
                for (int i = 0; i < SLOTS_PER_TIER; i++)
                {
                    if (!state.slotOpened[i])
                    {
                        nextSlot = i;
                        break;
                    }
                }

                if (IsSlotAvailable(upcomingTier, nextSlot))
                {
                    LocalizedRendering.SetText(treasureBoxStatusUi.timerText, Loc("treasurebox.status.available"));
                }
                else
                {
                    DateTime readyAt = GetSlotAvailableAt(upcomingTier, nextSlot);
                    if (readyAt != DateTime.MinValue)
                    {
                        TimeSpan span = readyAt - DateTime.Now;
                        treasureBoxStatusUi.timerText.text = FormatTimeSpan(span);
                    }
                    else
                    {
                        LocalizedRendering.SetText(treasureBoxStatusUi.timerText, Loc("treasurebox.status.waiting"));
                    }
                }
            }
        }
    }

    public TreasureBoxTier GetUpcomingTier()
    {
        if (_saveData == null) return TreasureBoxTier.Silver;

        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            if (!state.IsSetComplete)
            {
                return tier;
            }
        }
        return TreasureBoxTier.Silver;
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
        if (state.selectedRewardIndex < 0 || state.selectedRewardIndex >= data.exclusiveRewardItemIDs.Count)
        {
            state.selectedRewardIndex = UnityEngine.Random.Range(0, data.exclusiveRewardItemIDs.Count);
            SaveState();
        }

        return rewardsDatabase != null
            ? rewardsDatabase.FindByID(data.exclusiveRewardItemIDs[state.selectedRewardIndex])
            : null;
    }

    /// <summary>
    /// Returns true if the given tier is unlocked for opening.
    ///
    /// Progression rule: a tier is unlocked only when all lower tiers have
    /// completed their set of 3 in the current cycle — OR when it is Silver
    /// (always accessible).
    ///
    /// IAP note: PurchaseTierUnlock calls bypass this check internally; do not
    /// call this before calling that flow.
    /// </summary>
    public bool IsTierUnlocked(TreasureBoxTier tier)
    {
        TreasureBoxRewardItemData currentReward = GetCurrentCycleReward(tier);
        if (currentReward != null)
        {
            if (PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < currentReward.unlockXPLevel)
            {
                return false;
            }
        }

        if (tier == TreasureBoxTier.Silver) return true;
        TreasureBoxTierState state = _saveData.GetTierState(tier);

        if (state.openedCount > 0 && state.openedCount < SLOTS_PER_TIER) return true;

        TreasureBoxTier prevTier = (TreasureBoxTier)((int)tier - 1);
        TreasureBoxTierState prevState = _saveData.GetTierState(prevTier);

        DateTime currentCycleStart = GetCurrentCycleStart(DateTime.Now);
        bool isPrevTierCurrentCycle = new DateTime(prevState.lastTierResetTicks) >= currentCycleStart;

        return prevState.IsSetComplete && isPrevTierCurrentCycle;
    }

    /// <summary>
    /// Returns true if a specific slot is ready to be opened right now:
    ///   • The slot has not already been opened this cycle.
    ///   • The current UTC time is at or past the slot's spawn timestamp
    ///     (or the slot has never been used, meaning it is immediately available).
    /// </summary>
    public bool IsSlotAvailable(TreasureBoxTier tier, int slotIndex)
    {
        TreasureBoxTierState state = _saveData.GetTierState(tier);

        if (slotIndex < 0 || slotIndex >= SLOTS_PER_TIER) return false;
        if (state.slotOpened[slotIndex]) return false;

        return DateTime.Now >= GetSlotAvailableAt(tier, slotIndex);
    }

    /// <summary>
    /// Returns the UTC DateTime at which the given slot will become available.
    /// Returns <c>DateTime.MinValue</c> if the slot is already available or opened.
    /// </summary>
    public DateTime GetSlotAvailableAt(TreasureBoxTier tier, int slotIndex)
    {
        TreasureBoxTierState state = _saveData.GetTierState(tier);
        if (slotIndex < 0 || slotIndex >= SLOTS_PER_TIER) return DateTime.MinValue;
        if (state.slotOpened[slotIndex]) return DateTime.MinValue;

        int overallIndex = (int)tier * SLOTS_PER_TIER + slotIndex;
        int hoursOffset = overallIndex * 2;
        
        long baseTicks = state.lastTierResetTicks > 0 ? state.lastTierResetTicks : GetCurrentCycleStart(DateTime.Now).Ticks;
        return new DateTime(baseTicks).AddHours(hoursOffset);
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
        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
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
            DateTime readyAt = GetSlotAvailableAt(tier, slotIndex);
            string msg = readyAt == DateTime.MinValue
                ? $"{tier} slot {slotIndex} is already opened."
                : $"Ready in {FormatTimeSpan(readyAt - DateTime.Now)}.";

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

        // No need to schedule next slot here since it's strictly chronological from midnight

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

            DateTime currentCycleStart = GetCurrentCycleStart(DateTime.Now);
            if (new DateTime(state.lastTierResetTicks) < currentCycleStart)
            {
                state.pendingResetAvailableAtTicks = DateTime.Now.AddMinutes(10).Ticks;
                Debug.Log($"[TreasureBoxManager] {tier} finished carried-forward boxes. Starting 10-minute cooldown.");
            }
        }

        OnStateChanged?.Invoke();
        onResult?.Invoke(true, null);
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

    private void TickCycleResets()
    {
        bool changed = false;
        DateTime currentCycleStart = GetCurrentCycleStart(DateTime.Now);

        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            if (state.lastTierResetTicks == 0)
            {
                state.ResetCycle();
                state.lastTierResetTicks = currentCycleStart.Ticks;
                changed = true;
                continue;
            }

            DateTime tierLastReset = new DateTime(state.lastTierResetTicks);

            if (tierLastReset < currentCycleStart)
            {
                if (state.openedCount == 0 || state.openedCount >= SLOTS_PER_TIER)
                {
                    state.ResetCycle();
                    state.lastTierResetTicks = currentCycleStart.Ticks;
                    state.pendingResetAvailableAtTicks = 0;
                    RemoveSpawnedBoxesForTier(tier);
                    changed = true;
                }
            }

            if (state.pendingResetAvailableAtTicks > 0 && DateTime.Now.Ticks >= state.pendingResetAvailableAtTicks)
            {
                state.ResetCycle();
                state.lastTierResetTicks = currentCycleStart.Ticks;
                state.pendingResetAvailableAtTicks = 0;
                RemoveSpawnedBoxesForTier(tier);
                changed = true;
            }
        }

        if (changed)
        {
            SaveState();
            OnStateChanged?.Invoke();
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
        if (_saveData == null || spawnPoints == null || spawnPoints.Count == 0) return;
        
        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
        {
            TreasureBoxData rd = GetBoxData(tier);
            if (rd == null || rd.boxPrefab == null) continue;
            
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (!state.slotOpened[i] && IsTierUnlocked(tier) && IsSlotAvailable(tier, i) && !_spawnedBoxes.Exists(b => b.tier == tier && b.slotIndex == i))
                {
                    SpawnBox(rd.boxPrefab, tier, i);
                }
            }
        }
    }

    private int GetAvailableSpawnPointIndex()
    {
        if (spawnPoints == null || spawnPoints.Count == 0) return -1;
        
        List<int> availableIndices = new List<int>();
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] != null) availableIndices.Add(i);
        }

        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            if (state.assignedSpawnPoints == null) continue;
            for (int slot = 0; slot < SLOTS_PER_TIER; slot++)
            {
                if (!state.slotOpened[slot] && state.assignedSpawnPoints[slot] != -1)
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
        TreasureBoxTierState state = _saveData.GetTierState(tier);
        
        // If not already assigned, get a new point
        if (state.assignedSpawnPoints[slotIndex] == -1)
        {
            int spIndex = GetAvailableSpawnPointIndex();
            if (spIndex == -1)
            {
                Debug.LogWarning($"[TreasureBoxManager] No available spawn points for {tier} slot {slotIndex}");
                return;
            }
            state.assignedSpawnPoints[slotIndex] = spIndex;
            SaveState();
        }

        int assignedIndex = state.assignedSpawnPoints[slotIndex];
        if (spawnPoints == null || assignedIndex < 0 || assignedIndex >= spawnPoints.Count || spawnPoints[assignedIndex] == null)
        {
            Debug.LogWarning("[TreasureBoxManager] Assigned spawn point is missing or invalid.");
            return;
        }

        Transform spawnPoint = spawnPoints[assignedIndex];
        
        Vector3 spawnPos = spawnPoint.position;
        if (terrainReference != null)
        {
            float terrainY = terrainReference.SampleHeight(spawnPos) + terrainReference.transform.position.y;
            spawnPos = new Vector3(spawnPos.x, terrainY + 2f, spawnPos.z);
        }
        
        // The pool lives in the garden scene, so it is absent for as long as the player is elsewhere -
        // and for the first frames of a load, before its own Awake has run. Neither is worth an
        // exception: the spawn check runs again a second later, by which time it is there.
        if (Objectpool.Instance == null)
        {
            Debug.LogWarning("[TreasureBoxManager] No Objectpool yet - deferring the " + tier
                             + " slot " + slotIndex + " spawn to the next check.");
            return;
        }

        GameObject go = Objectpool.Instance.Spawn(prefab, spawnPos, spawnPoint.rotation);

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
        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
        {
            TreasureBoxTierState s = _saveData.GetTierState(tier);
            if (s.slotOpened == null || s.slotOpened.Length != SLOTS_PER_TIER)
                s.slotOpened = new bool[SLOTS_PER_TIER];
            if (s.slotAvailableAtTicks == null || s.slotAvailableAtTicks.Length != SLOTS_PER_TIER)
                s.slotAvailableAtTicks = new long[SLOTS_PER_TIER];
            if (s.assignedSpawnPoints == null || s.assignedSpawnPoints.Length != SLOTS_PER_TIER)
                s.assignedSpawnPoints = new int[] { -1, -1, -1 };
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
            else
            {
                DateTime availableAt = GetSlotAvailableAt(tier, i);
                if (availableAt != DateTime.MinValue)
                    timerArray[i] = FormatTimeSpan(availableAt - DateTime.Now);
                else
                    timerArray[i] = "Waiting on previous...";
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

    [ContextMenu("DEBUG — Expire All Cooldowns (Make All Available)")]
    private void Debug_ExpireAllCooldowns()
    {
        foreach (TreasureBoxTier tier in Enum.GetValues(typeof(TreasureBoxTier)))
        {
            TreasureBoxTierState state = _saveData.GetTierState(tier);
            for (int i = 0; i < SLOTS_PER_TIER; i++)
            {
                if (!state.slotOpened[i])
                    state.slotAvailableAtTicks[i] = 0L; // 0 = immediately available
            }
        }
        SaveState();
        OnStateChanged?.Invoke();
        Debug.Log("[TreasureBoxManager] DEBUG: All cooldowns expired.");
    }

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
        SaveState();
        OnStateChanged?.Invoke();
        Debug.Log($"[TreasureBoxManager] DEBUG: {tier} tier force-completed.");
    }
}

