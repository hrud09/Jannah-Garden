using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// One treasure box reward item's data. A plain serializable class rather than a ScriptableObject —
/// every item lives as an entry inside one of <see cref="TreasureBoxRewardItemsData.categories"/>'s
/// groups, in one shared asset, instead of as its own .asset file. See <see cref="ShopItemData"/> for
/// the equivalent shop-side migration this mirrors.
/// </summary>
[System.Serializable]
public class TreasureBoxRewardItemData
{
    [Header("Item Metadata")]
    public Sprite itemIcon;

    [TextArea(1, 3)]
    public string itemName;

    [TextArea(3, 10)]
    public string itemDescription;

    [Header("Item Identifier")]
    [Tooltip("Unique identifier for this inventory item.")]
    public string itemID;

    [Header("Asset References")]
    // See ShopItemData.itemPrefabRef for why these are initialized rather than left null.
    [Tooltip("Addressable reference to the real item prefab, downloaded on demand and spawned after " +
             "placement is confirmed.")]
    public AssetReferenceGameObject itemPrefabRef = new AssetReferenceGameObject(string.Empty);
    [Tooltip("Addressable reference to the lightweight ghost/preview prefab shown while the player is " +
             "positioning the item. Falls back to itemPrefabRef if left empty.")]
    public AssetReferenceGameObject itemPlacementModelPrefabRef = new AssetReferenceGameObject(string.Empty);

    [Header("Item State")]
    public ShopItemCategory itemCategory = ShopItemCategory.Silver;
    public int quantity = 0;

    [Header("Unlock Requirements")]
    [Tooltip("The required XP level to unlock and find this item.")]
    public int unlockXPLevel = 1;

    [Header("Economy")]
    [Tooltip("Noor Coins awarded instead if the player already owns this exclusive reward item.")]
    [Min(0)]
    public int noorCoinAmount = 250;

    [Header("Placement Settings")]
    public float placementTimerDuration = 360f; // Required time to fully place the item in the game world in seconds

    [Tooltip("Optional. Overrides the grid footprint, in cells, that this item claims when placed. " +
             "Leave at 0,0 to derive it from the model's own collision bounds. Set it when the " +
             "measured body is misleading — a tree whose canopy collider is far wider than anything " +
             "you would actually plant around, or a path tile that wants deliberate padding.")]
    public Vector2Int gridFootprintOverride = Vector2Int.zero;


    [Header("Puzzle Data")]
    public GameObject[] puzzlePieces;

    /// <summary>True if claiming this reward hands the player something to place in the garden.</summary>
    public bool IsPlaceable => itemPrefabRef != null && itemPrefabRef.RuntimeKeyIsValid();

    /// <summary>
    /// Player-facing name for the active locale, looked up as "item.{itemID}.name" (see
    /// Editor/ShopItemLocalizationKeyGenerator.cs), falling back to the authored <see cref="itemName"/>
    /// when no translation exists yet or outside Play Mode.
    /// </summary>
    public string LocalizedName =>
        LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault($"item.{itemID}.name", itemName)
            : itemName;

    /// <summary>Player-facing description for the active locale — see <see cref="LocalizedName"/>.</summary>
    public string LocalizedDescription =>
        LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault($"item.{itemID}.desc", itemDescription)
            : itemDescription;

    public ItemRarity GetRarity()
    {
        if (unlockXPLevel <= 5) return ItemRarity.Common;
        if (unlockXPLevel <= 10) return ItemRarity.Uncommon;
        if (unlockXPLevel <= 15) return ItemRarity.Rare;
        if (unlockXPLevel <= 20) return ItemRarity.Epic;
        return ItemRarity.Legendary;
    }

    public Color GetRarityColor()
    {
        switch (GetRarity())
        {
            case ItemRarity.Common: return new Color(0.6f, 0.6f, 0.6f); // Gray
            case ItemRarity.Uncommon: return new Color(0.3f, 0.8f, 0.3f); // Green
            case ItemRarity.Rare: return new Color(0.2f, 0.6f, 1f); // Blue
            case ItemRarity.Epic: return new Color(0.7f, 0.3f, 1f); // Purple
            case ItemRarity.Legendary: return new Color(1f, 0.7f, 0.1f); // Orange
            default: return Color.white;
        }
    }
}

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

/// <summary>Player-facing rarity labels, e.g. shown on TreasureBoxConfirmationPanel's badge.</summary>
public static class ItemRarityTaxonomy
{
    public static string GetName(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return Localized("rarity.common", "Common");
            case ItemRarity.Uncommon: return Localized("rarity.uncommon", "Uncommon");
            case ItemRarity.Rare: return Localized("rarity.rare", "Rare");
            case ItemRarity.Epic: return Localized("rarity.epic", "Epic");
            case ItemRarity.Legendary: return Localized("rarity.legendary", "Legendary");
            default: return rarity.ToString();
        }
    }

    private static string Localized(string key, string englishFallback)
    {
        return LocalizationManager.Instance != null ? LocalizationManager.Instance.GetOrDefault(key, englishFallback) : englishFallback;
    }
}
