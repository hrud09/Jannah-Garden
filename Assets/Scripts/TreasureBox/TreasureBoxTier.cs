using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Tier Enum
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The four treasure box tiers, in strict progression order.
/// The integer values double as the unlock prerequisite check: a player must
/// have completed all tiers with a lower value before accessing a higher one.
/// </summary>
public enum TreasureBoxTier
{
    Silver   = 0,
    Gold     = 1,
    Platinum = 2,
    Diamond  = 3
}

// ─────────────────────────────────────────────────────────────────────────────
// Reward Data ScriptableObject
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Data asset that describes a single treasure box tier: its visuals and the
/// rewards granted when a player completes a full set of three boxes.
///
/// Create via: Assets → Create → Jannah Garden → Treasure Box Reward Data
/// </summary>
[CreateAssetMenu(
    fileName = "TreasureBoxData_Silver",
    menuName  = "Jannah Garden/TreasureBoxData",
    order     = 10)]
public class TreasureBoxData : ScriptableObject
{
    // ── Identity ─────────────────────────────────────────────────────────────

    [Header("Tier Identity")]
    [Tooltip("Which tier this data asset represents.")]
    public TreasureBoxTier tier;

    [Tooltip("Player-facing display name shown in UI (e.g. 'Silver Box').")]
    public string tierDisplayName = "Silver Box";

    [Tooltip("Chest sprite shown on the treasure box status HUD for this tier.")]
    public Sprite tierIcon;

    // ── World Spawning ────────────────────────────────────────────────────────

    [Header("World Prefab")]
    [Tooltip("The 3D treasure box prefab spawned in the garden scene when this " +
             "tier's slot timer fires. The prefab should have a TreasureBoxWorldObject " +
             "component (or one will be added automatically at runtime).")]
    public GameObject boxPrefab;

    // ── Spawn Timing (configurable) ───────────────────────────────────────────

    [Header("Spawn Timing")]
    [Tooltip("Hours between successive box spawns within this tier. " +
             "Default is 2. Adjusting this changes how quickly the next slot " +
             "of this tier becomes available after the previous one is opened.")]
    [Min(0.01f)]
    public float spawnCooldownHours = 2f;

    [Tooltip("Hours until the entire tier cycle resets after the first box " +
             "of this tier is opened. Default is 24.")]
    [Min(1f)]
    public float cycleDurationHours = 24f;

    // ── Set-Completion Reward ─────────────────────────────────────────────────

    [Header("Set-Completion Reward")]
    [Tooltip("itemIDs of the exclusive garden items awarded when the player opens all 3 boxes of this " +
             "tier, resolved at runtime against TreasureBoxManager.rewardsDatabase. One item is chosen " +
             "randomly per cycle. TreasureBoxRewardItemData is a plain serializable class rather than a " +
             "ScriptableObject, so it has no asset identity of its own to reference directly here — see " +
             "ShopItemData/ShopItemsData for the equivalent shop-side design.")]
    public List<string> exclusiveRewardItemIDs = new List<string>();

    [Tooltip("Noor Coins awarded instead if the player already owns the exclusive " +
             "reward item. Set this to the item's perceived NC value.")]
    [Min(0)]
    public int noorCoinEquivalent = 250;

    /// <summary>
    /// Localized <see cref="tierDisplayName"/> — there are only four tiers, so the key is derived from
    /// <see cref="tier"/> itself ("treasurebox.tier_name.silver" etc.) rather than a per-asset id.
    /// Falls back to the authored <see cref="tierDisplayName"/> when untranslated or outside Play Mode.
    /// </summary>
    public string LocalizedTierDisplayName =>
        LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault($"treasurebox.tier_name.{tier.ToString().ToLowerInvariant()}", tierDisplayName)
            : tierDisplayName;

    // ── Individual Box Reward (single open) ───────────────────────────────────

    [Header("Per-Box Reward (optional)")]
    [Tooltip("Small NC bonus given each time any single box of this tier is opened, " +
             "in addition to the set-completion reward. Leave at 0 for no per-box coins.")]
    [Min(0)]
    public int noorCoinPerBox = 0;

    /// <summary>
    /// Localized short tier name (e.g. "Silver", not "Silver Box") for callers that only have the enum
    /// value and don't need the full "{0} Box" display name — see
    /// TreasureBoxManager.GetPreviousTierName and TreasureBoxConfirmationPanel's no-reward fallback.
    /// </summary>
    public static string GetLocalizedShortTierName(TreasureBoxTier tier)
    {
        string fallback = tier.ToString();
        return LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault($"treasurebox.tier_short.{tier.ToString().ToLowerInvariant()}", fallback)
            : fallback;
    }

    /// <summary>Localized "{0} Box" fallback for callers with only the enum value, no TreasureBoxData
    /// asset — see TreasureBoxConfirmationPanel.Show's no-reward-configured branch.</summary>
    public static string GetLocalizedTierBoxFallback(TreasureBoxTier tier)
    {
        string fallback = $"{tier} Box";
        return LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault($"treasurebox.tier_name.{tier.ToString().ToLowerInvariant()}", fallback)
            : fallback;
    }
}
