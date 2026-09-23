#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.IO;
using System.Text.RegularExpressions;

public class GenerateShopItemDatas
{
    [MenuItem("Tools/Shop/Generate Shop Item Datas")]
    public static void GenerateShopItems()
    {
        string prefabFolder = "Assets/Prefabs/Shop Items";

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        var remoteGroup = AddressableItemAuthoring.GetOrCreateRemoteGroup(settings);

        ShopItemsData database = ShopItemsDataUtility.GetOrCreateDatabase();

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

            ShopItemData itemData = ShopItemsDataUtility.FindByPrefab(database, prefab);
            bool isNew = false;

            if (itemData == null)
            {
                itemData = new ShopItemData();
                isNew = true;
            }

            // Assign details
            itemData.itemName = cleanedName;
            itemData.itemDescription = description;
            itemData.itemCategory = category;
            AddressableItemAuthoring.AssignPrefab(itemData.itemPrefabRef, prefab, settings, remoteGroup);

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

            // Tier follows the price, so it is re-derived even for assets that already exist.
            itemData.itemTier = ShopItemClassifier.DetermineTier(itemData.noorCoinCost);

            if (isNew)
            {
                ShopItemsDataUtility.AddItem(database, itemData);
                createdCount++;
            }
            else
            {
                updatedCount++;
            }
        }

        ShopItemsDataUtility.Save(database);
        AssetDatabase.Refresh();

        Debug.Log($"[GenerateShopItemDatas] Successfully processed prefabs. Created: {createdCount}, Updated: {updatedCount} entries in {ShopItemsDataUtility.DatabaseAssetPath}.");
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
        => ShopItemClassifier.DetermineCategory(prefabName);

    private static string GenerateDescription(string itemName, ShopItemCategory category)
        => ShopItemClassifier.GenerateDescription(itemName, category);
}
#endif
