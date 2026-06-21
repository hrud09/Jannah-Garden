#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class BulkShopItemGeneratorWindow : EditorWindow
{
    [System.Serializable]
    public class PrefabItemConfig
    {
        public GameObject prefab;
        public bool selected = true;
        public string cleanName;
        public ShopItemCategory category;
        public int price;
        public int requiredXP;
        public Sprite icon;
    }

    private string prefabFolder = "Assets/Prefabs/Shop Items";
    private string outputFolder = "Assets/Resources/Shop Items";

    private int defaultPrice = 50;
    private int defaultXPLevel = 1;
    private bool overwriteExisting = false;

    private List<PrefabItemConfig> prefabItems = new List<PrefabItemConfig>();
    private Vector2 scrollPos;

    [MenuItem("Tools/Bulk Shop Item Generator")]
    public static void ShowWindow()
    {
        GetWindow<BulkShopItemGeneratorWindow>("Bulk Shop Item Gen");
    }

    private void OnEnable()
    {
        LoadPrefabs();
    }

    private void LoadPrefabs()
    {
        prefabItems.Clear();

        if (!AssetDatabase.IsValidFolder(prefabFolder))
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string cleanName = CleanName(prefab.name);
            ShopItemCategory category = DetermineCategory(prefab.name);

            // Attempt to check if an asset already exists to populate current values
            string assetName = prefab.name.Replace(" ", "_") + "_Data";
            string assetPath = $"{outputFolder}/{assetName}.asset";
            ShopItemData existingData = AssetDatabase.LoadAssetAtPath<ShopItemData>(assetPath);

            int price = existingData != null ? existingData.noorCoinCost : defaultPrice;
            int xpLevel = existingData != null ? existingData.requiredXPLevel : defaultXPLevel;
            Sprite icon = existingData != null ? existingData.itemIcon : null;
            if (existingData != null)
            {
                category = existingData.itemCategory;
                cleanName = existingData.itemName;
            }
            else
            {
                // Try to find a matching icon in Assets/2D Assets/Icons
                string possibleIconPath = $"Assets/2D Assets/Icons/{prefab.name}.png";
                icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                if (icon == null)
                {
                    possibleIconPath = $"Assets/2D Assets/Icons/{cleanName.Replace(" ", "_")}.png";
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                }
                if (icon == null)
                {
                    possibleIconPath = $"Assets/2D Assets/Icons/{cleanName}.png";
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                }
            }

            prefabItems.Add(new PrefabItemConfig
            {
                prefab = prefab,
                selected = true,
                cleanName = cleanName,
                category = category,
                price = price,
                requiredXP = xpLevel,
                icon = icon
            });
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Bulk Shop Item Data Generator", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        
        // Folders setup
        prefabFolder = EditorGUILayout.TextField("Prefab Source Folder", prefabFolder);
        outputFolder = EditorGUILayout.TextField("Output Resources Folder", outputFolder);

        EditorGUILayout.Space();
        
        // Default configuration
        GUILayout.Label("Default Settings for New Items", EditorStyles.boldLabel);
        defaultPrice = EditorGUILayout.IntField("Default Price", defaultPrice);
        defaultXPLevel = EditorGUILayout.IntField("Default XP Level", defaultXPLevel);
        overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Fields", overwriteExisting);

        EditorGUILayout.Space();

        if (GUILayout.Button("Scan / Refresh Prefabs Folder", GUILayout.Height(30)))
        {
            LoadPrefabs();
        }

        EditorGUILayout.Space();

        // Control buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All"))
        {
            SetAllSelection(true);
        }
        if (GUILayout.Button("Deselect All"))
        {
            SetAllSelection(false);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Scrollview list
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

        // Header
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Gen", GUILayout.Width(35));
        GUILayout.Label("Prefab Name", GUILayout.Width(150));
        GUILayout.Label("Icon", GUILayout.Width(100));
        GUILayout.Label("Display Name", GUILayout.Width(150));
        GUILayout.Label("Category", GUILayout.Width(100));
        GUILayout.Label("Price", GUILayout.Width(60));
        GUILayout.Label("XP Lvl", GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < prefabItems.Count; i++)
        {
            var item = prefabItems[i];
            EditorGUILayout.BeginHorizontal();

            item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(35));
            EditorGUILayout.LabelField(item.prefab.name, GUILayout.Width(150));
            item.icon = (Sprite)EditorGUILayout.ObjectField(item.icon, typeof(Sprite), false, GUILayout.Width(100));
            item.cleanName = EditorGUILayout.TextField(item.cleanName, GUILayout.Width(150));
            item.category = (ShopItemCategory)EditorGUILayout.EnumPopup(item.category, GUILayout.Width(100));
            item.price = EditorGUILayout.IntField(item.price, GUILayout.Width(60));
            item.requiredXP = EditorGUILayout.IntField(item.requiredXP, GUILayout.Width(50));

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Selected Shop Items", GUILayout.Height(40)))
        {
            GenerateSelected();
        }
    }

    private void SetAllSelection(bool value)
    {
        foreach (var item in prefabItems)
        {
            item.selected = value;
        }
    }

    private void GenerateSelected()
    {
        // Ensure output folder exists
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

        int count = 0;
        foreach (var item in prefabItems)
        {
            if (!item.selected) continue;

            string assetName = item.prefab.name.Replace(" ", "_") + "_Data";
            string assetPath = $"{outputFolder}/{assetName}.asset";

            ShopItemData itemData = AssetDatabase.LoadAssetAtPath<ShopItemData>(assetPath);
            bool isNew = false;

            if (itemData == null)
            {
                itemData = CreateInstance<ShopItemData>();
                isNew = true;
            }

            // Assign variables
            if (isNew || overwriteExisting)
            {
                itemData.itemName = item.cleanName;
                itemData.itemDescription = GenerateDescription(item.cleanName, item.category);
                itemData.itemCategory = item.category;
                itemData.noorCoinCost = item.price;
                itemData.requiredXPLevel = item.requiredXP;
            }

            itemData.itemIcon = item.icon;
            itemData.itemPrefab = item.prefab;

            if (string.IsNullOrEmpty(itemData.itemID))
            {
                itemData.itemID = System.Guid.NewGuid().ToString("N");
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(itemData, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(itemData);
            }
            count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", $"Successfully generated/updated {count} ShopItemData assets.", "OK");
        LoadPrefabs(); // Refresh UI values
    }

    private static string CleanName(string rawName)
    {
        string name = rawName;
        if (name.EndsWith(" 1")) name = name.Substring(0, name.Length - 2);
        name = name.Replace("_", " ");
        name = Regex.Replace(name, @"(\B[A-Z]+?(?=[A-Z][a-z])|(?<=[a-z])\B[A-Z])", " $1");
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
