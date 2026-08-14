using UnityEngine;
using UnityEngine.AddressableAssets;

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
    public ShopItemCategory itemCategory = ShopItemCategory.PlantsAndGardens;

    [Tooltip("Quality band inside the category. Drives the tier badge and frame on the item card. " +
             "Coin packs and ad offers have no tier.")]
    public ShopItemTier itemTier = ShopItemTier.Tier1;

    [Tooltip("Sort order in the shop UI. Lower numbers appear first.")]
    public int sortOrder = 0;

    [Header("Unlock Requirements")]
    [Tooltip("The required XP level to unlock and purchase this item.")]
    public int requiredXPLevel = 1;

    // ---------------------------------------------------------------------
    // Placement — only meaningful when the item hands over a prefab.
    // ---------------------------------------------------------------------
    [Header("Asset References")]
    // Initialized rather than left null: AssetReferenceGameObject has no parameterless constructor, and
    // a ScriptableObject created via CreateInstance (as every shop-item generator editor tool does)
    // starts with true C# nulls here until Unity's serializer round-trips it once — these initializers
    // make itemPrefabRef/itemPlacementModelPrefabRef safe to read immediately after CreateInstance too.
    [Tooltip("Addressable reference to the real item prefab, downloaded on demand and spawned after " +
             "placement is confirmed.")]
    public AssetReferenceGameObject itemPrefabRef = new AssetReferenceGameObject(string.Empty);

    [Tooltip("Addressable reference to the lightweight ghost/preview prefab shown while the player is " +
             "positioning the item. Falls back to itemPrefabRef if left empty.")]
    public AssetReferenceGameObject itemPlacementModelPrefabRef = new AssetReferenceGameObject(string.Empty);

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
    public bool IsPlaceable => itemPrefabRef != null && itemPrefabRef.RuntimeKeyIsValid();
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

/// <summary>
/// The tab an item lives under. Values are pinned explicitly because assets serialize this enum by
/// index — renumbering a member silently recategorizes every asset that already holds the old value.
/// The five shop collections come first, then the treasure-box rarity tabs used by the inventory
/// side of the panel, then the Noor Coin section.
/// </summary>
public enum ShopItemCategory
{
    // ── Shop collections ──────────────────────────────────────────────────
    /// <summary>Trees, flowers, planters and greenery.</summary>
    PlantsAndGardens = 0,       // was the unused "All" slot

    /// <summary>Fountains, pools, ponds, channels, cascades and wells.</summary>
    WaterOfGarden = 1,          // was Plants

    /// <summary>Arches, gateways, columns, bridges, paths and buildings.</summary>
    Architecture = 2,           // was Buildings

    /// <summary>Mosaics, rugs, calligraphy, vessels and altars.</summary>
    DecorAndSacredObjects = 3,  // was Decorations

    /// <summary>Lanterns, stars, light effects and floating landscape pieces.</summary>
    CelestialAndLight = 9,      // appended: 4-8 were already taken by the tabs below

    // ── Inventory (treasure box) rarity tabs ──────────────────────────────
    Silver = 4,
    Gold = 5,
    Platinum = 6,
    Diamond = 7,

    // Noor Coin bundles and the daily ad reward — the section where the player acquires coins rather
    // than spends them.
    NoorCoins = 8
}

/// <summary>
/// Quality band within a category. Every collection runs Tier 1 through Tier 4, so the tier says how
/// premium an item is relative to its own category rather than across the whole shop.
/// </summary>
public enum ShopItemTier
{
    /// <summary>Coin packs and ad offers — nothing to rank.</summary>
    None = 0,
    Tier1 = 1,  // Common
    Tier2 = 2,  // Standard
    Tier3 = 3,  // Premium
    Tier4 = 4   // Premium Plus
}

/// <summary>
/// Labels and colours for the shop taxonomy. Enum names cannot carry spaces or ampersands, so every
/// player-facing string for a category or tier is spelled out here and nowhere else — the tab bar,
/// the item card and the editor tools all read from this one table.
/// </summary>
public static class ShopTaxonomy
{
    /// <summary>The five collections a placeable item can belong to, in shop order.</summary>
    public static readonly ShopItemCategory[] ShopCategories =
    {
        ShopItemCategory.PlantsAndGardens,
        ShopItemCategory.WaterOfGarden,
        ShopItemCategory.Architecture,
        ShopItemCategory.DecorAndSacredObjects,
        ShopItemCategory.CelestialAndLight
    };

    /// <summary>The rarity tabs on the inventory side of the panel.</summary>
    public static readonly ShopItemCategory[] InventoryCategories =
    {
        ShopItemCategory.Silver,
        ShopItemCategory.Gold,
        ShopItemCategory.Platinum,
        ShopItemCategory.Diamond
    };

    /// <summary>True for the collections that live on the shop side of the panel.</summary>
    public static bool IsShopCategory(ShopItemCategory category)
    {
        switch (category)
        {
            case ShopItemCategory.PlantsAndGardens:
            case ShopItemCategory.WaterOfGarden:
            case ShopItemCategory.Architecture:
            case ShopItemCategory.DecorAndSacredObjects:
            case ShopItemCategory.CelestialAndLight:
            case ShopItemCategory.NoorCoins:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The label shown on a category tab.</summary>
    public static string GetCategoryName(ShopItemCategory category)
    {
        switch (category)
        {
            case ShopItemCategory.PlantsAndGardens: return "Plants & Gardens";
            case ShopItemCategory.WaterOfGarden: return "Water of Garden";
            case ShopItemCategory.Architecture: return "Architecture";
            case ShopItemCategory.DecorAndSacredObjects: return "Decor & Sacred";
            case ShopItemCategory.CelestialAndLight: return "Celestial & Light";
            case ShopItemCategory.NoorCoins: return "Noor Coins";
            default: return category.ToString();
        }
    }

    /// <summary>The full category name, used in tooltips and generated item descriptions.</summary>
    public static string GetCategoryLongName(ShopItemCategory category)
    {
        switch (category)
        {
            case ShopItemCategory.DecorAndSacredObjects: return "Decor & Sacred Objects";
            case ShopItemCategory.CelestialAndLight: return "Celestial & Light / Landscape";
            default: return GetCategoryName(category);
        }
    }

    /// <summary>Accent colour for a category tab, so each collection reads at a glance.</summary>
    public static Color GetCategoryColor(ShopItemCategory category)
    {
        switch (category)
        {
            case ShopItemCategory.PlantsAndGardens: return new Color(0.40f, 0.76f, 0.38f); // leaf green
            case ShopItemCategory.WaterOfGarden: return new Color(0.29f, 0.66f, 0.87f);    // water blue
            case ShopItemCategory.Architecture: return new Color(0.83f, 0.70f, 0.47f);     // sandstone
            case ShopItemCategory.DecorAndSacredObjects: return new Color(0.75f, 0.45f, 0.78f); // mosaic violet
            case ShopItemCategory.CelestialAndLight: return new Color(0.99f, 0.83f, 0.36f); // lantern gold
            case ShopItemCategory.NoorCoins: return new Color(1.00f, 0.76f, 0.20f);        // coin amber
            default: return Color.white;
        }
    }

    /// <summary>Short tier label for the badge on an item card, e.g. "Tier 3".</summary>
    public static string GetTierName(ShopItemTier tier)
    {
        return tier == ShopItemTier.None ? string.Empty : $"Tier {(int)tier}";
    }

    /// <summary>The rarity word players read, e.g. "Premium Plus".</summary>
    public static string GetTierQuality(ShopItemTier tier)
    {
        switch (tier)
        {
            case ShopItemTier.Tier1: return "Common";
            case ShopItemTier.Tier2: return "Standard";
            case ShopItemTier.Tier3: return "Premium";
            case ShopItemTier.Tier4: return "Premium Plus";
            default: return string.Empty;
        }
    }

    /// <summary>Badge label combining both, e.g. "Tier 3 · Premium".</summary>
    public static string GetTierLabel(ShopItemTier tier)
    {
        return tier == ShopItemTier.None
            ? string.Empty
            : $"{GetTierName(tier)} · {GetTierQuality(tier)}";
    }

    /// <summary>Badge colour, climbing from muted stone to gold as the tier rises.</summary>
    public static Color GetTierColor(ShopItemTier tier)
    {
        switch (tier)
        {
            case ShopItemTier.Tier1: return new Color(0.72f, 0.74f, 0.72f); // stone
            case ShopItemTier.Tier2: return new Color(0.45f, 0.78f, 0.95f); // sky
            case ShopItemTier.Tier3: return new Color(0.76f, 0.53f, 0.95f); // amethyst
            case ShopItemTier.Tier4: return new Color(1.00f, 0.79f, 0.28f); // gold
            default: return Color.white;
        }
    }
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