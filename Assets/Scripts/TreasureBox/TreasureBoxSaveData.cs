using System;

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
    /// UTC ticks (DateTime.Ticks) at which each slot becomes available to open.
    /// Slot 0 is available immediately when the cycle starts.
    /// Slot 1 becomes available 2 hours after slot 0 is opened.
    /// Slot 2 becomes available 2 hours after slot 1 is opened.
    /// </summary>
    public long[] slotAvailableAtTicks = new long[TreasureBoxManager.SLOTS_PER_TIER];

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
        }
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
