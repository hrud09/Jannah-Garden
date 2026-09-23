#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class TreasureBoxRewardAssigner
{
    [MenuItem("Tools/Treasure Box/Assign Reward Items")]
    public static void AssignRewardsToTreasureBoxes()
    {
        // 1. Find all TreasureBoxData assets
        string[] boxGuids = AssetDatabase.FindAssets("t:TreasureBoxData");
        List<TreasureBoxData> boxDatas = new List<TreasureBoxData>();
        foreach (string guid in boxGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TreasureBoxData data = AssetDatabase.LoadAssetAtPath<TreasureBoxData>(path);
            if (data != null)
            {
                boxDatas.Add(data);
            }
        }

        // 2. Read all TreasureBoxRewardItemData entries from the single aggregator asset
        TreasureBoxRewardItemsData database = TreasureBoxRewardItemsDataUtility.GetOrCreateDatabase();
        List<TreasureBoxRewardItemData> itemDatas = TreasureBoxRewardItemsDataUtility.AllItems(database).ToList();

        // 3. Assign items based on tier/category mapping
        int updatedBoxes = 0;
        string detailMessage = "";
        List<string> skippedNoId = new List<string>();

        foreach (TreasureBoxData boxData in boxDatas)
        {
            ShopItemCategory targetCategory;
            switch (boxData.tier)
            {
                case TreasureBoxTier.Silver:
                    targetCategory = ShopItemCategory.Silver;
                    break;
                case TreasureBoxTier.Gold:
                    targetCategory = ShopItemCategory.Gold;
                    break;
                case TreasureBoxTier.Platinum:
                    targetCategory = ShopItemCategory.Platinum;
                    break;
                case TreasureBoxTier.Diamond:
                    targetCategory = ShopItemCategory.Diamond;
                    break;
                default:
                    continue;
            }

            // Find all items matching this category
            List<TreasureBoxRewardItemData> matchingItems = new List<TreasureBoxRewardItemData>();
            foreach (TreasureBoxRewardItemData item in itemDatas)
            {
                if (item.itemCategory == targetCategory)
                {
                    matchingItems.Add(item);
                }
            }

            List<string> ids = new List<string>();
            foreach (var item in matchingItems)
            {
                if (string.IsNullOrEmpty(item.itemID))
                {
                    skippedNoId.Add(item.itemName);
                    continue;
                }
                ids.Add(item.itemID);
            }

            // Set the exclusiveRewardItemIDs field
            boxData.exclusiveRewardItemIDs = ids;
            EditorUtility.SetDirty(boxData);
            updatedBoxes++;

            detailMessage += $"- {boxData.name} ({boxData.tier}): Assigned {ids.Count} items\n";
        }

        if (updatedBoxes > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string finalSummary = $"Successfully assigned items to {updatedBoxes} Treasure Box Data asset(s).\n\n{detailMessage}";
        if (skippedNoId.Count > 0)
        {
            finalSummary += $"\nWarning: {skippedNoId.Count} item(s) skipped due to missing itemID: {string.Join(", ", skippedNoId)}";
        }
        Debug.Log($"[TreasureBoxRewardAssigner] {finalSummary}");
        EditorUtility.DisplayDialog("Assign Treasure Box Rewards", finalSummary, "OK");
    }
}
#endif
