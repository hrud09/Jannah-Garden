#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

public class GenerateShopItemDatas
{
    [MenuItem("Tools/Generate Shop Item Datas")]
    public static void GenerateShopItems()
    {
        string prefabFolder = "Assets/Prefabs/Shop Items";
        string outputFolder = "Assets/Resources/Shop Items";

        // Ensure the output folder exists
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            string[] folders = outputFolder.Split('/');
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

        // Find all prefabs in the prefab folder
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
        int createdCount = 0;
        int updatedCount = 0;

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null) continue;

            string rawName = prefab.name;
            string cleanedName = CleanName(rawName);
            ShopItemCategory category = DetermineCategory(rawName);
            string description = GenerateDescription(cleanedName, category);

            // Asset name matching the clean/raw name style
            string assetName = rawName.Replace(" ", "_") + "_Data";
            string assetPath = $"{outputFolder}/{assetName}.asset";

            ShopItemData itemData = AssetDatabase.LoadAssetAtPath<ShopItemData>(assetPath);
            bool isNew = false;

            if (itemData == null)
            {
                itemData = ScriptableObject.CreateInstance<ShopItemData>();
                isNew = true;
            }

            // Assign details
            itemData.itemName = cleanedName;
            itemData.itemDescription = description;
            itemData.itemCategory = category;
            itemData.itemPrefab = prefab;

            // Try to find a matching icon in Assets/2D Assets/Icons
            if (itemData.itemIcon == null)
            {
                string possibleIconPath = $"Assets/2D Assets/Icons/{prefab.name}.png";
                Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                if (icon == null)
                {
                    possibleIconPath = $"Assets/2D Assets/Icons/{cleanedName.Replace(" ", "_")}.png";
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                }
                if (icon == null)
                {
                    possibleIconPath = $"Assets/2D Assets/Icons/{cleanedName}.png";
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                }
                itemData.itemIcon = icon;
            }

            // Generate UUID if empty
            if (string.IsNullOrEmpty(itemData.itemID))
            {
                itemData.itemID = System.Guid.NewGuid().ToString("N");
            }

            // Default unlock settings if new
            if (isNew)
            {
                itemData.requiredXPLevel = 1;
                itemData.noorCoinCost = 50; // default cost
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(itemData, assetPath);
                createdCount++;
            }
            else
            {
                EditorUtility.SetDirty(itemData);
                updatedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[GenerateShopItemDatas] Successfully processed prefabs. Created: {createdCount}, Updated: {updatedCount} assets in {outputFolder}.");
    }

    private static string CleanName(string rawName)
    {
        string name = rawName;
        // Strip trailing " 1"
        if (name.EndsWith(" 1"))
        {
            name = name.Substring(0, name.Length - 2);
        }

        // Replace underscores with spaces
        name = name.Replace("_", " ");

        // Insert spaces before capital letters (camelCase to space separated)
        name = Regex.Replace(name, @"(\B[A-Z]+?(?=[A-Z][a-z])|(?<=[a-z])\B[A-Z])", " $1");

        // Clean up multiple spaces
        name = Regex.Replace(name, @"\s+", " ");

        return name.Trim();
    }

    private static ShopItemCategory DetermineCategory(string prefabName)
    {
        string lower = prefabName.ToLower();
        if (lower.Contains("tree") || 
            lower.Contains("bush") || 
            lower.Contains("flower") || 
            lower.Contains("grass") || 
            lower.Contains("reed") || 
            lower.Contains("cattail") || 
            lower.Contains("lily") || 
            lower.Contains("leaf") || 
            lower.Contains("plant") || 
            lower.Contains("meadow"))
        {
            return ShopItemCategory.Plants;
        }

        if (lower.Contains("building") || lower.Contains("house"))
        {
            return ShopItemCategory.Buildings;
        }

        return ShopItemCategory.Decorations;
    }

    private static string GenerateDescription(string itemName, ShopItemCategory category)
    {
        switch (category)
        {
            case ShopItemCategory.Plants:
                return $"A beautiful green {itemName} to add vibrant life and natural beauty to your garden.";
            case ShopItemCategory.Buildings:
                return $"An elegant {itemName} structure to provide comfort and architectural beauty to your estate.";
            case ShopItemCategory.Decorations:
            default:
                return $"A unique {itemName} decoration to personalize and enhance the atmosphere of your garden.";
        }
    }
}
#endif
