#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TreasureBoxRewardItemIdAssigner
{
    [MenuItem("Tools/Treasure Box/Assign Reward Item IDs")]
    public static void AssignIdsToAllRewardItems()
    {
        TreasureBoxRewardItemsData database = TreasureBoxRewardItemsDataUtility.GetOrCreateDatabase();

        int assigned = 0;
        int updated = 0;

        System.Collections.Generic.HashSet<string> usedIds = new System.Collections.Generic.HashSet<string>();

        // First collect existing IDs to avoid duplicates
        foreach (var item in TreasureBoxRewardItemsDataUtility.AllItems(database))
        {
            if (!string.IsNullOrEmpty(item.itemID))
            {
                usedIds.Add(item.itemID);
            }
        }

        foreach (var item in TreasureBoxRewardItemsDataUtility.AllItems(database))
        {
            bool needSave = false;
            string current = item.itemID;

            if (string.IsNullOrEmpty(current) || usedIds.Contains(current))
            {
                string newId;
                do
                {
                    newId = System.Guid.NewGuid().ToString("N");
                } while (usedIds.Contains(newId));

                item.itemID = newId;
                usedIds.Add(newId);
                needSave = true;
            }

            if (needSave)
            {
                updated++;
            }

            assigned++;
        }

        if (updated > 0)
        {
            TreasureBoxRewardItemsDataUtility.Save(database);
        }

        Debug.Log($"[TreasureBoxRewardItemIdAssigner] Processed {assigned} TreasureBoxRewardItemData entries. Assigned/Updated IDs on {updated} entries.");
        EditorUtility.DisplayDialog(
            "Assign IDs",
            $"Processed {assigned} TreasureBoxRewardItemData entries. Assigned/Updated IDs on {updated} entries.",
            "OK"
        );
    }
}
#endif
