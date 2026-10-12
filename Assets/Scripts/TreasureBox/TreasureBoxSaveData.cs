using System;
using System.Runtime.Serialization;

/// <summary>
/// Persistent state for a single tier's three treasure box slots.
/// Stored as part of <see cref="TreasureBoxSaveData"/>.
/// </summary>
[Serializable]
public class TreasureBoxTierState
{
    // ── Slots (always exactly 3 per tier) ────────────────────────────────────

    /// <summary>
    /// Whether each of the 3 slots has been opened in the current 24-hour cycle.
    /// </summary>
    public bool[] slotOpened = new bool[TreasureBoxManager.SLOTS_PER_TIER];

    /// <summary>
    /// Legacy: per-slot spawn times from before all 3 boxes of a tier appeared together.
    /// No longer read; kept so older binary saves still deserialize.
    /// </summary>
    public long[] slotAvailableAtTicks = new long[TreasureBoxManager.SLOTS_PER_TIER];

    /// <summary>
    /// Index of the spawn point assigned to this slot from the manager's array. -1 means unassigned.
    /// </summary>
    public int[] assignedSpawnPoints = new int[] { -1, -1, -1 };

    // ── Cycle Tracking ────────────────────────────────────────────────────────

    /// <summary>
    /// UTC ticks of when the very first slot of this tier was opened in the
    /// current cycle. The 24-hour cycle resets exactly 24 hours after this moment.
    /// 0 means the cycle has not started yet (no boxes opened).
    /// </summary>
    public long cycleStartedAtTicks = 0L;

    /// <summary>
    /// How many boxes of this tier have been opened in the current cycle (0–3).
    /// </summary>
    public int openedCount = 0;

    /// <summary>
    /// Legacy: per-tier cycle stamp from before the single daily sequence. Only read once, to
    /// migrate an older save (see <see cref="TreasureBoxSaveData.sequenceCycleStartTicks"/>).
    /// </summary>
    public long lastTierResetTicks = 0L;

    /// <summary>
    /// Legacy: the old 10-minute cooldown after a carried-over tier. No longer read; kept so older
    /// binary saves still deserialize.
    /// </summary>
    public long pendingResetAvailableAtTicks = 0L;

    /// <summary>
    /// The index of the randomly selected reward item from the tier's exclusiveRewardItems array for this cycle.
    /// </summary>
    public int selectedRewardIndex = -1;


    // ── Convenience ───────────────────────────────────────────────────────────

    /// <summary>Returns true if all 3 slots have been opened this cycle.</summary>
    public bool IsSetComplete => openedCount >= TreasureBoxManager.SLOTS_PER_TIER;

    /// <summary>Returns true if the 24-hour cycle has not started yet.</summary>
    public bool IsFreshCycle => cycleStartedAtTicks == 0L;

    /// <summary>
    /// Resets all slot state for a new 24-hour cycle.
    /// </summary>
    public void ResetCycle()
    {
        cycleStartedAtTicks = 0L;
        openedCount         = 0;
        for (int i = 0; i < TreasureBoxManager.SLOTS_PER_TIER; i++)
        {
            slotOpened[i]           = false;
            slotAvailableAtTicks[i] = 0L;
            assignedSpawnPoints[i]  = -1;
        }
        selectedRewardIndex = -1;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Top-level serializable save object that holds the state for all four tiers.
/// Persisted via <see cref="SaveSystem"/> under the key
/// <see cref="TreasureBoxManager.SAVE_KEY"/>.
/// </summary>
[Serializable]
public class TreasureBoxSaveData
{
    public long globalCycleStartedAtTicks = 0L;
    public bool hasShownChestOnce = false;

    /// <summary>
    /// Ticks of the daily cycle start the current Silver → Diamond sequence belongs to. A sequence
    /// older than today's cycle is replaced at the next tick, unless its active tier is partially
    /// opened — that tier is carried over until it is finished. 0 means not yet set (fresh install or
    /// a save from before this field existed).
    /// </summary>
    [OptionalField] public long sequenceCycleStartTicks = 0L;

    public TreasureBoxTierState silver   = new TreasureBoxTierState();
    public TreasureBoxTierState gold     = new TreasureBoxTierState();
    public TreasureBoxTierState platinum = new TreasureBoxTierState();
    public TreasureBoxTierState diamond  = new TreasureBoxTierState();

    /// <summary>
    /// Returns the <see cref="TreasureBoxTierState"/> for the given tier.
    /// </summary>
    public TreasureBoxTierState GetTierState(TreasureBoxTier tier)
    {
        switch (tier)
        {
            case TreasureBoxTier.Silver:   return silver;
            case TreasureBoxTier.Gold:     return gold;
            case TreasureBoxTier.Platinum: return platinum;
            case TreasureBoxTier.Diamond:  return diamond;
            default:
                UnityEngine.Debug.LogError($"[TreasureBoxSaveData] Unknown tier: {tier}");
                return silver;
        }
    }
}
