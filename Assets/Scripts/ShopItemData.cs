using UnityEngine;

[CreateAssetMenu(fileName = "NewShopItemData", menuName = "Shop/Item Data", order = 1)]
public class ShopItemData : ScriptableObject
{
    [Header("Item Metadata")]
    public Sprite itemIcon;
    
    [TextArea(1, 3)]
    public string itemName;
    
    [TextArea(3, 10)]
    public string itemDescription;

    [Header("Economy")]
    [Tooltip("Cost of this item in Noor Coins. 0 = free.")]
    public int noorCoinCost = 0;

    [HideInInspector]
    [Tooltip("Legacy string price field. Kept for backwards compatibility only. Use noorCoinCost instead.")]
    public string itemPrice; // Deprecated — use noorCoinCost

    [Header("Item Visual Category")]
    public ShopItemType shopItemType;

    [Header("Shop Category")]
    public ShopItemCategory itemCategory = ShopItemCategory.Plants;

    [Header("Asset References")]
    public GameObject itemPrefab; // The real item prefab spawned after placement is confirmed
    [Tooltip("Lightweight ghost/preview prefab shown while the player is positioning the item. " +
             "Falls back to itemPrefab if left empty.")]
    public GameObject itemPlacementModelPrefab; // Temporary preview shown during placement

    [Header("Item State")]
    public ShopItemState itemState = ShopItemState.Locked;

    [Header("Placement Settings")]
    public float placementTimerDuration = 360f; // Required time to fully place the item in the game world in seconds
}

public enum ShopItemState
{
    Locked,
    Unlocked
}

public enum ShopItemCategory
{
    All,
    Plants,
    Buildings,
    Decorations
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