using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "NewTreasureBoxRewardItem", menuName = "Jannah Garden/Treasure Box Reward Item", order = 20)]
public class TreasureBoxRewardItemData : ScriptableObject
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

    [Header("Puzzle Data")]
    public GameObject[] puzzlePieces;

    [Header("Calculated Rarity")]
    public ItemRarity calculatedRarity;

    private void OnValidate()
    {
        calculatedRarity = GetRarity();
    }

    /// <summary>True if claiming this reward hands the player something to place in the garden.</summary>
    public bool IsPlaceable => itemPrefabRef != null && itemPrefabRef.RuntimeKeyIsValid();

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
