using UnityEngine;
using UnityEditor;

public class GenerateTreasureBoxData
{
    [MenuItem("Tools/Treasure Box/Generate 20 Treasure Box Items")]
    public static void GenerateItems()
    {
        TreasureBoxRewardItemsData database = TreasureBoxRewardItemsDataUtility.GetOrCreateDatabase();

        ShopItemCategory[] tiers = { ShopItemCategory.Silver, ShopItemCategory.Gold, ShopItemCategory.Platinum, ShopItemCategory.Diamond };

        for (int i = 1; i <= 20; i++)
        {
            TreasureBoxRewardItemData item = new TreasureBoxRewardItemData();
            item.itemName = "Treasure Item " + i;
            item.itemDescription = "A special reward item #" + i;

            // Assign a random tier for testing, 5 of each
            item.itemCategory = tiers[(i - 1) / 5];

            item.itemID = System.Guid.NewGuid().ToString("N");

            TreasureBoxRewardItemsDataUtility.AddItem(database, item);
        }

        TreasureBoxRewardItemsDataUtility.Save(database);
        AssetDatabase.Refresh();
        Debug.Log("Added 20 TreasureBoxRewardItemData entries to " + TreasureBoxRewardItemsDataUtility.DatabaseAssetPath);
    }
}
