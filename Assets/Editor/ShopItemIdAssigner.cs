#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ShopItemIdAssigner
{
    [MenuItem("Tools/Shop/Assign Shop Item IDs")]
    public static void AssignIdsToAllShopItems()
    {
        ShopItemsData database = ShopItemsDataUtility.GetOrCreateDatabase();

        int assigned = 0;
        int updated = 0;

        System.Collections.Generic.HashSet<string> usedIds = new System.Collections.Generic.HashSet<string>();

        // First collect existing IDs to avoid duplicates
        foreach (var item in ShopItemsDataUtility.AllItems(database))
        {
            if (!string.IsNullOrEmpty(item.itemID))
            {
                usedIds.Add(item.itemID);
            }
        }

        foreach (var item in ShopItemsDataUtility.AllItems(database))
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
            ShopItemsDataUtility.Save(database);
        }

        Debug.Log($"[ShopItemIdAssigner] Processed {assigned} ShopItemData entries. Assigned/Updated IDs on {updated} entries.");
    }

    [MenuItem("Tools/Shop/Assign Random Shop Item Unlock Levels")]
    public static void AssignRandomUnlockLevelsToAllShopItems()
    {
        ShopItemsData database = ShopItemsDataUtility.GetOrCreateDatabase();
        int processed = 0;

        foreach (var item in ShopItemsDataUtility.AllItems(database))
        {
            // Generate a random required XP level between 1 and 15 (inclusive)
            item.requiredXPLevel = UnityEngine.Random.Range(1, 16);
            processed++;
        }

        if (processed > 0)
        {
            ShopItemsDataUtility.Save(database);
        }

        Debug.Log($"[ShopItemIdAssigner] Assigned random requiredXPLevel values (1-15) to {processed} ShopItemData entries.");
    }
}
#endif
