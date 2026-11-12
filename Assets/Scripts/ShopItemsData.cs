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

    [Tooltip("How many items of this category are shown under its tab in the shop. "
           + "0 = show every item in the category. Items are picked after the shop's normal sort "
           + "(unlocked first, then cheapest), so the cut keeps the most relevant ones.")]
    [Min(0)]
    public int maxVisibleItems = 0;

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

    /// <summary>
    /// The number of items allowed to be visible under <paramref name="category"/>'s tab, or
    /// <see cref="int.MaxValue"/> when the category has no limit (no group, or its
    /// <see cref="ShopItemCategoryGroup.maxVisibleItems"/> is 0).
    /// </summary>
    public int GetMaxVisibleItems(ShopItemCategory category)
    {
        if (categories != null)
        {
            foreach (var group in categories)
            {
                if (group != null && group.category == category && group.maxVisibleItems > 0)
                    return group.maxVisibleItems;
            }
        }
        return int.MaxValue;
    }
}
