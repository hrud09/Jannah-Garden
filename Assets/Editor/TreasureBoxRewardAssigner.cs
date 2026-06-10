#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class TreasureBoxRewardAssigner
{
    [MenuItem("Tools/Assign Treasure Box Reward Items")]
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

        // 2. Find all TreasureBoxRewardItemData assets
        string[] itemGuids = AssetDatabase.FindAssets("t:TreasureBoxRewardItemData");
        List<TreasureBoxRewardItemData> itemDatas = new List<TreasureBoxRewardItemData>();
        foreach (string guid in itemGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TreasureBoxRewardItemData item = AssetDatabase.LoadAssetAtPath<TreasureBoxRewardItemData>(path);
            if (item != null)
            {
                itemDatas.Add(item);
            }
        }

        // 3. Assign items based on tier/category mapping
        int updatedBoxes = 0;
        string detailMessage = "";

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

            // Set the exclusiveRewardItems field
            boxData.exclusiveRewardItems = matchingItems.ToArray();
            EditorUtility.SetDirty(boxData);
            updatedBoxes++;

            detailMessage += $"- {boxData.name} ({boxData.tier}): Assigned {matchingItems.Count} items\n";
        }

        if (updatedBoxes > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string finalSummary = $"Successfully assigned items to {updatedBoxes} Treasure Box Data asset(s).\n\n{detailMessage}";
        Debug.Log($"[TreasureBoxRewardAssigner] {finalSummary}");
        EditorUtility.DisplayDialog("Assign Treasure Box Rewards", finalSummary, "OK");
    }
}
#endif
