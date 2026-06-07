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

    [Header("Subscriber Setting")]
    [Tooltip("Subscribers bypass rewarded ads and open all available boxes instantly.")]
    public bool isSubscriber = false;

    [Header("Reset Configuration")]
    [Tooltip("x = hour (0-23), y = minute (0-59) for daily reset.")]
    public Vector2 dailyResetTime = new Vector2(0, 0);

    [Header("UI Data")]
    public TreasureBoxStatusUI[] treasureBoxStatusUis;

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
    [Tooltip("Pre-placed spawn points for treasure boxes. A box will randomly pick one that is unoccupied.")]
    public Transform[] spawnPoints;

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
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadState();
        TickCycleResets();
        OnStateChanged += CheckAndSpawnNewBoxes;
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
            TickCycleResets();
            CheckAndSpawnNewBoxes();
        }

        UpdateTierUiData();
    }

    private void UpdateTierUiData()
    {
        if (treasureBoxStatusUis == null || _saveData == null) return;
        
        foreach (var ui in treasureBoxStatusUis)
        {
            if (ui == null) continue;
            
            TreasureBoxTierState state = _saveData.GetTierState(ui.tier);
            TreasureBoxData data = GetBoxData(ui.tier);
            
            if (ui.nameText != null && data != null)
            {
                ui.nameText.text = data.tierDisplayName;
            }
                
            if (ui.openedBoxCountText != null)
            {
                ui.openedBoxCountText.text = $"{state.openedCount}/{SLOTS_PER_TIER}";
            }
                
            if (ui.timerText != null)
            {
                if (state.pendingResetAvailableAtTicks > 0)
                {
                    TimeSpan span = new DateTime(state.pendingResetAvailableAtTicks) - DateTime.Now;
                    if (span.TotalSeconds > 0)
                    {
                        ui.timerText.text = FormatTimeSpan(span);
                    }
                    else
                    {
                        ui.timerText.text = "Resetting...";
                    }
                }
                else if (state.IsSetComplete)
                {
                    ui.timerText.text = "Completed";
                }
                else if (!IsTierUnlocked(ui.tier))
                {
                    DateTime readyAt = GetSlotAvailableAt(ui.tier, 0);
                    if (readyAt != DateTime.MinValue)
                    {
                        TimeSpan span = readyAt - DateTime.Now;
                        if (span.TotalSeconds > 0)
                        {
                            ui.timerText.text = FormatTimeSpan(span);
                        }
                        else
                        {
                            ui.timerText.text = "Finish previous tier";
                        }
                    }
                    else
                    {
                        ui.timerText.text = "Locked";
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

                    if (IsSlotAvailable(ui.tier, nextSlot))
                    {
                        ui.timerText.text = "Available";
                    }
                    else
                    {
                        DateTime readyAt = GetSlotAvailableAt(ui.tier, nextSlot);
                        if (readyAt != DateTime.MinValue)
                        {
                            TimeSpan span = readyAt - DateTime.Now;
                            ui.timerText.text = FormatTimeSpan(span);
                        }
                        else
                        {
                            ui.timerText.text = "Waiting...";
                        }
                    }
                }
            }
        }
    }

    private void OnApplicationQuit()  => SaveState();
    private void OnApplicationPause(bool paused) { if (paused) SaveState(); }

    // ─── Public Query API ─────────────────────────────────────────────────────

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
            string msg = $"Complete all {GetPreviousTierName(tier)} boxes first.";
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
        if (!AdService.IsAdReady)
        {
            string msg = "Ad not ready. Please try again shortly.";
            Debug.Log($"[TreasureBoxManager] {msg}");
            onResult?.Invoke(false, msg);
            return;
        }

        AdService.ShowRewardedAd(earned =>
        {
            if (earned)
            {
                CompleteBoxOpen(tier, slotIndex, onResult);
            }
            else
            {
                string msg = "Ad skipped — box not opened.";
                Debug.Log($"[TreasureBoxManager] {msg}");
                onResult?.Invoke(false, msg);
            }
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
                    GameObject puzzlePiecePrefab = null;
                    if (rd != null && rd.exclusiveRewardItem != null && rd.exclusiveRewardItem.puzzlePieces != null && i < rd.exclusiveRewardItem.puzzlePieces.Length)
                    {
                        puzzlePiecePrefab = rd.exclusiveRewardItem.puzzlePieces[i];
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
        if (rewardData != null && rewardData.exclusiveRewardItem != null && rewardData.exclusiveRewardItem.puzzlePieces != null && slotIndex < rewardData.exclusiveRewardItem.puzzlePieces.Length)
        {
            puzzlePiecePrefab = rewardData.exclusiveRewardItem.puzzlePieces[slotIndex];
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

        TreasureBoxRewardItemData reward = rewardData.exclusiveRewardItem;
        if (reward == null)
        {
            Debug.LogWarning($"[TreasureBoxManager] No exclusive reward assigned to {tier} reward data.");
            return;
        }

        reward.isUnlocked = true;
        Debug.Log($"[TreasureBoxManager] Unlocked {tier} set reward: {reward.itemName}!");
        
        if (ItemPlacementManager.Instance != null)
        {
            ItemPlacementManager.Instance.PreparePlacement(reward);
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
        TreasureBoxTierState state = _saveData.GetTierState(tier);
        if (state != null && state.IsSetComplete)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("All boxes for this tier are already opened!");
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
                ToastMessageManager.Instance.ShowToast("Boxes for this tier haven't appeared yet!");
            }
        }
    }
    
    private void CheckAndSpawnNewBoxes()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return;
        
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
        if (spawnPoints == null || spawnPoints.Length == 0) return -1;
        
        List<int> availableIndices = new List<int>();
        for (int i = 0; i < spawnPoints.Length; i++)
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
        if (spawnPoints == null || assignedIndex < 0 || assignedIndex >= spawnPoints.Length || spawnPoints[assignedIndex] == null)
        {
            Debug.LogWarning("[TreasureBoxManager] Assigned spawn point is missing or invalid.");
            return;
        }

        Transform spawnPoint = spawnPoints[assignedIndex];
        
        GameObject go = Objectpool.Instance.Spawn(prefab, spawnPoint.position, spawnPoint.rotation);
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
                    boxScript.nameText.text = boxData.tierDisplayName;
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
        return prev >= 0 ? ((TreasureBoxTier)prev).ToString() : "N/A";
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "now";
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

