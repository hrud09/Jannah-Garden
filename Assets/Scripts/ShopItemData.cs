using UnityEngine;

[CreateAssetMenu(fileName = "NewShopItemData", menuName = "Shop/Item Data", order = 1)]
public class ShopItemData : ScriptableObject
{
    // ---------------------------------------------------------------------
    // Identity — shown for every acquisition type.
    // ---------------------------------------------------------------------
    [Header("Item Metadata")]
    public Sprite itemIcon;

    [TextArea(1, 3)]
    public string itemName;

    [TextArea(3, 10)]
    public string itemDescription;

    [Tooltip("Unique identifier for this shop item. If empty, use the Editor menu 'Tools/Assign Shop Item IDs' to generate IDs automatically.")]
    public string itemID;

    // ---------------------------------------------------------------------
    // Acquisition — the switch that decides which block below applies.
    // ---------------------------------------------------------------------
    [Header("Acquisition")]
    [Tooltip("How the player acquires this item: spending Noor Coins, watching a rewarded ad, or paying real money.")]
    public ShopAcquisitionType acquisitionType = ShopAcquisitionType.NoorCoins;

    // ---------------------------------------------------------------------
    // acquisitionType = NoorCoins
    // ---------------------------------------------------------------------
    [Header("Noor Coin Price (acquisitionType = NoorCoins)")]
    [Tooltip("Cost of this item in Noor Coins. 0 = free. Only used when acquisitionType is NoorCoins.")]
    public int noorCoinCost = 0;

    // ---------------------------------------------------------------------
    // acquisitionType = RewardedAd
    // ---------------------------------------------------------------------
    [Header("Rewarded Ad (acquisitionType = RewardedAd)")]
    [Tooltip("When true, this offer can only be claimed once per cooldown window and then shows a countdown.")]
    public bool isDailyOffer = false;

    [Tooltip("Hours before a claimed daily offer becomes available again. 24 = once per day.")]
    public float offerCooldownHours = 24f;

    // ---------------------------------------------------------------------
    // acquisitionType = InAppPurchase
    // ---------------------------------------------------------------------
    [Header("In-App Purchase (acquisitionType = InAppPurchase)")]
    [Tooltip("Store product ID, e.g. 'com.amal.jannahgarden.coins_500'. Must match the product configured in the store.")]
    public string iapProductId;

    [Tooltip("Price shown on the item card, e.g. '$0.99'. A real store integration would overwrite this with the " +
             "localized price fetched from the storefront.")]
    public string realMoneyPriceLabel = "$0.99";

    // ---------------------------------------------------------------------
    // Payout — shared by RewardedAd and InAppPurchase (a coin pack or an ad
    // offer that pays coins instead of handing over a placeable item).
    // ---------------------------------------------------------------------
    [Header("Coin Payout (RewardedAd / InAppPurchase)")]
    [Tooltip("Noor Coins granted to the player when this item is acquired. Used by coin packs (InAppPurchase) " +
             "and by ad offers that pay out coins instead of an item.")]
    public int noorCoinReward = 0;

    // ---------------------------------------------------------------------
    // Shop presentation & gating — shown for every acquisition type.
    // ---------------------------------------------------------------------
    [Header("Shop Category")]
    public ShopItemCategory itemCategory = ShopItemCategory.Plants;

    [Header("Unlock Requirements")]
    [Tooltip("The required XP level to unlock and purchase this item.")]
    public int requiredXPLevel = 1;

    // ---------------------------------------------------------------------
    // Placement — only meaningful when the item hands over a prefab.
    // ---------------------------------------------------------------------
    [Header("Asset References")]
    public GameObject itemPrefab; // The real item prefab spawned after placement is confirmed

    [Tooltip("Lightweight ghost/preview prefab shown while the player is positioning the item. " +
             "Falls back to itemPrefab if left empty.")]
    public GameObject itemPlacementModelPrefab; // Temporary preview shown during placement

    [Header("Placement Settings")]
    public float placementTimerDuration = 360f; // Required time to fully place the item in the game world in seconds

    // ---------------------------------------------------------------------
    // Deprecated
    // ---------------------------------------------------------------------
    [HideInInspector]
    [Tooltip("Legacy string price field. Kept for backwards compatibility only. Use noorCoinCost instead.")]
    public string itemPrice; // Deprecated — use noorCoinCost

    /// <summary>
    /// True if acquiring this item hands the player something to place in the garden.
    /// Coin packs and coin-paying ad offers have no prefab, so they skip placement entirely.
    /// </summary>
    public bool IsPlaceable => itemPrefab != null;
}

/// <summary>
/// How the player pays for a shop item.
/// </summary>
public enum ShopAcquisitionType
{
    /// <summary>Spend <see cref="ShopItemData.noorCoinCost"/> of the in-game currency.</summary>
    NoorCoins,

    /// <summary>Watch a rewarded ad. Combine with <see cref="ShopItemData.isDailyOffer"/> for a daily-refreshing offer.</summary>
    RewardedAd,

    /// <summary>Pay real money through the storefront. Typically a Noor Coin pack.</summary>
    InAppPurchase
}

public enum ShopItemCategory
{
    All,
    Plants,
    Buildings,
    Decorations,
    Silver,
    Gold,
    Platinum,
    Diamond,

    // Noor Coin bundles and the daily ad reward — the section where the player acquires coins rather
    // than spends them. Appended last on purpose: assets serialize this enum by index, so inserting
    // anywhere above would silently recategorize every existing shop item.
    NoorCoins
}

public enum ShopItemType
{
    Tree1,
    Tree2,
    Tree3,
    House1,
    House2,
    House3
}