using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One category's items. The category lives on the group, not on each entry — grouping is the source of
/// truth for which collection an item belongs to.
/// </summary>
[System.Serializable]
public class ShopItemCategoryGroup
{
    public ShopItemCategory category;
    public List<ShopItemData> items = new List<ShopItemData>();
}

/// <summary>
/// The shop's item database — every <see cref="ShopItemData"/> entry in one asset instead of scattered
/// across individual .asset files, organized into one <see cref="ShopItemCategoryGroup"/> per category
/// rather than a single flat list. Assign this on <see cref="InGameShopManager.shopItemsDatabase"/>.
/// </summary>
[CreateAssetMenu(fileName = "ShopItemsData", menuName = "Shop/Item Database", order = 0)]
public class ShopItemsData : ScriptableObject
{
    public List<ShopItemCategoryGroup> categories = new List<ShopItemCategoryGroup>();
}
