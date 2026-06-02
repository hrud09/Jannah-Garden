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
    
    public string itemPrice; // String format supports diverse pricing (e.g., "$10", "100 Coins", "Free")

    [Header("Item Visual Category")]
    public ShopItemType shopItemType;

    [Header("Asset References")]
    public GameObject itemPrefab; // Optional prefab reference for instantiating the item in the shop or inventory

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

public enum ShopItemType
{
    Tree1,
    Tree2,
    Tree3,
    House1,
    House2,
    House3
}