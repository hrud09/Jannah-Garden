using UnityEngine;
using UnityEditor;

public class GenerateTreasureBoxData
{
    [MenuItem("Tools/Treasure Box/Generate 20 Treasure Box Items")]
    public static void GenerateItems()
    {
        string folderPath = "Assets/Resources/Tressure Box Reward Data";
        
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            // Try to create the folder if it doesn't exist
            string[] folders = folderPath.Split('/');
            string currentPath = folders[0];
            for (int i = 1; i < folders.Length; i++)
            {
                string nextPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = nextPath;
            }
        }

        ShopItemCategory[] tiers = { ShopItemCategory.Silver, ShopItemCategory.Gold, ShopItemCategory.Platinum, ShopItemCategory.Diamond };

        for (int i = 1; i <= 20; i++)
        {
            TreasureBoxRewardItemData item = ScriptableObject.CreateInstance<TreasureBoxRewardItemData>();
            item.itemName = "Treasure Item " + i;
            item.itemDescription = "A special reward item #" + i;
            
            // Assign a random tier for testing, 5 of each
            item.itemCategory = tiers[(i - 1) / 5];
            
            string assetPath = folderPath + "/TreasureBoxItem_" + i + ".asset";
            AssetDatabase.CreateAsset(item, assetPath);
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created 20 TreasureBoxRewardItemData assets in " + folderPath);
    }
}
