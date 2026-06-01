using UnityEngine;

[CreateAssetMenu(fileName = "NewShopItemData", menuName = "Shop/Item Data", order = 1)]
public class ShopItemData : ScriptableObject
{
    [Header("Item Metadata")]
    public ShopItemType itemType; // Enum to categorize item types (e.g., Tree, House)
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