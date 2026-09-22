using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// One category's treasure box reward items. The category lives on the group, not on each entry —
/// grouping is the source of truth for which rarity tier an item belongs to.
/// </summary>
[System.Serializable]
public class TreasureBoxRewardCategoryGroup
{
    public ShopItemCategory category;
    public List<TreasureBoxRewardItemData> items = new List<TreasureBoxRewardItemData>();
}

/// <summary>
/// The treasure box reward database — every <see cref="TreasureBoxRewardItemData"/> entry in one asset
/// instead of scattered across individual .asset files, organized into one
/// <see cref="TreasureBoxRewardCategoryGroup"/> per rarity tier (Silver/Gold/Platinum/Diamond). Assign
/// this on <see cref="InGameShopManager.inventoryItemsDatabase"/> and <see cref="TreasureBoxManager.rewardsDatabase"/>.
/// </summary>
[CreateAssetMenu(fileName = "TreasureBoxRewardItemsData", menuName = "Shop/Treasure Box Reward Database", order = 2)]
public class TreasureBoxRewardItemsData : ScriptableObject
{
    public List<TreasureBoxRewardCategoryGroup> categories = new List<TreasureBoxRewardCategoryGroup>();

    /// <summary>Every item across every category group, in group order.</summary>
    public IEnumerable<TreasureBoxRewardItemData> AllItems()
    {
        if (categories == null) yield break;
        foreach (var group in categories)
        {
            if (group?.items == null) continue;
            foreach (var item in group.items)
            {
                if (item != null) yield return item;
            }
        }
    }

    /// <summary>Finds an entry by its stable itemID, or null.</summary>
    public TreasureBoxRewardItemData FindByID(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return null;
        return AllItems().FirstOrDefault(i => i.itemID == itemID);
    }
}
