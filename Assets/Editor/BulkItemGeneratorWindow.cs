#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class BulkItemGeneratorWindow : EditorWindow
{
    // Tab tracking
    private int activeTab = 0;
    private readonly string[] tabTitles = { "Shop Items Generator", "Treasure Box Rewards", "Treasure Box Data Editor" };

    // --- Tab 1: Shop Items Configuration ---
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

    private string shopPrefabFolder = "Assets/Prefabs/Shop Items";
    private string shopOutputFolder = "Assets/Resources/Natural Placeable Shop Items";
    private int shopDefaultPrice = 50;
    private int shopDefaultXPLevel = 1;
    private bool shopOverwriteExisting = false;
    private List<PrefabItemConfig> shopItems = new List<PrefabItemConfig>();
    private Vector2 shopScrollPos;

    // --- Tab 2: Treasure Box Reward Configuration ---
    [System.Serializable]
    public class TreasureRewardConfig
    {
        public GameObject prefab;
        public bool selected = true;
        public string cleanName;
        public ShopItemCategory tier; // Silver, Gold, Platinum, Diamond
        public int unlockXPLevel;
        public int noorCoinAmount;
        public float placementTimerDuration;
        public Sprite icon;
    }

    private string rewardPrefabFolder = "Assets/Prefabs";
    private string rewardOutputFolder = "Assets/Resources/Tressure Box Reward Data";
    private int rewardDefaultXPLevel = 1;
    private int rewardDefaultNoorCoinAmount = 250;
    private float rewardDefaultPlacementDuration = 360f;
    private bool rewardOverwriteExisting = false;
    private List<TreasureRewardConfig> rewardItems = new List<TreasureRewardConfig>();
    private Vector2 rewardScrollPos;

    // --- Tab 3: Treasure Box Data Editor ---
    private TreasureBoxData silverBoxData;
    private TreasureBoxData goldBoxData;
    private TreasureBoxData platinumBoxData;
    private TreasureBoxData diamondBoxData;
    private Vector2 editorScrollPos;

    [MenuItem("Tools/Bulk Item Generator")]
    public static void ShowWindow()
    {
        GetWindow<BulkItemGeneratorWindow>("Bulk Item Generator");
    }

    private void OnEnable()
    {
        LoadShopPrefabs();
        LoadRewardPrefabs();
        LoadTreasureBoxDatas();
    }

    private void LoadTreasureBoxDatas()
    {
        string tbDataFolder = "Assets/Resources/TreasureBoxData";
        silverBoxData = AssetDatabase.LoadAssetAtPath<TreasureBoxData>($"{tbDataFolder}/TreasureBoxData_Silver.asset");
        goldBoxData = AssetDatabase.LoadAssetAtPath<TreasureBoxData>($"{tbDataFolder}/TreasureBoxData_Gold.asset");
        platinumBoxData = AssetDatabase.LoadAssetAtPath<TreasureBoxData>($"{tbDataFolder}/TreasureBoxData_Platinum.asset");
        diamondBoxData = AssetDatabase.LoadAssetAtPath<TreasureBoxData>($"{tbDataFolder}/TreasureBoxData_Diamond.asset");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        activeTab = GUILayout.Toolbar(activeTab, tabTitles, GUILayout.Height(25));
        GUILayout.Space(10);

        if (activeTab == 0)
        {
            DrawShopGeneratorGUI();
        }
        else if (activeTab == 1)
        {
            DrawRewardGeneratorGUI();
        }
        else
        {
            DrawBoxDataEditorGUI();
        }
    }

    // ==========================================
    // SHOP GENERATOR LOGIC (TAB 1)
    // ==========================================

    private void LoadShopPrefabs()
    {
        shopItems.Clear();

        if (!AssetDatabase.IsValidFolder(shopPrefabFolder))
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { shopPrefabFolder });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string cleanName = CleanName(prefab.name);
            ShopItemCategory category = DetermineCategory(prefab.name);

            // Attempt to check if an asset already exists to populate current values
            string assetName = prefab.name.Replace(" ", "_") + "_Data";
            string assetPath = $"{shopOutputFolder}/{assetName}.asset";
            ShopItemData existingData = AssetDatabase.LoadAssetAtPath<ShopItemData>(assetPath);

            int price = existingData != null ? existingData.noorCoinCost : shopDefaultPrice;
            int xpLevel = existingData != null ? existingData.requiredXPLevel : shopDefaultXPLevel;
            Sprite icon = existingData != null ? existingData.itemIcon : null;
            if (existingData != null)
            {
                category = existingData.itemCategory;
                cleanName = existingData.itemName;
            }
            else
            {
                // Try to find a matching icon
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

            shopItems.Add(new PrefabItemConfig
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

    private void DrawShopGeneratorGUI()
    {
        GUILayout.Label("Bulk Shop Item Data Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Folders setup
        shopPrefabFolder = DrawFolderSelector("Prefab Source Folder", shopPrefabFolder);
        shopOutputFolder = DrawFolderSelector("Output Resources Folder", shopOutputFolder);
        EditorGUILayout.Space();

        // Default configuration
        GUILayout.Label("Default Settings for New Shop Items", EditorStyles.boldLabel);
        shopDefaultPrice = EditorGUILayout.IntField("Default Price", shopDefaultPrice);
        shopDefaultXPLevel = EditorGUILayout.IntField("Default XP Level", shopDefaultXPLevel);
        shopOverwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Fields", shopOverwriteExisting);
        EditorGUILayout.Space();

        if (GUILayout.Button("Scan / Refresh Prefabs Folder", GUILayout.Height(30)))
        {
            LoadShopPrefabs();
        }
        EditorGUILayout.Space();

        // Control buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All"))
        {
            SetAllShopSelection(true);
        }
        if (GUILayout.Button("Deselect All"))
        {
            SetAllShopSelection(false);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // Scrollview list
        shopScrollPos = EditorGUILayout.BeginScrollView(shopScrollPos, GUILayout.ExpandHeight(true));

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

        for (int i = 0; i < shopItems.Count; i++)
        {
            var item = shopItems[i];
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
            GenerateSelectedShopItems();
        }
    }

    private void SetAllShopSelection(bool value)
    {
        foreach (var item in shopItems)
        {
            item.selected = value;
        }
    }

    private void GenerateSelectedShopItems()
    {
        EnsureFolderExists(shopOutputFolder);

        int count = 0;
        foreach (var item in shopItems)
        {
            if (!item.selected) continue;

            string assetName = item.prefab.name.Replace(" ", "_") + "_Data";
            string assetPath = $"{shopOutputFolder}/{assetName}.asset";

            ShopItemData itemData = AssetDatabase.LoadAssetAtPath<ShopItemData>(assetPath);
            bool isNew = false;

            if (itemData == null)
            {
                itemData = CreateInstance<ShopItemData>();
                isNew = true;
            }

            if (isNew || shopOverwriteExisting)
            {
                itemData.itemName = item.cleanName;
                itemData.itemDescription = GenerateShopDescription(item.cleanName, item.category);
                itemData.itemCategory = item.category;
                itemData.itemTier = ShopItemClassifier.DetermineTier(item.price);
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
        LoadShopPrefabs();
    }

    // ==========================================
    // REWARD GENERATOR LOGIC (TAB 2)
    // ==========================================

    private void LoadRewardPrefabs()
    {
        rewardItems.Clear();

        if (!AssetDatabase.IsValidFolder(rewardPrefabFolder))
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { rewardPrefabFolder });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // Skip subfolders like Shop Items to avoid cluttering rewards unless they explicitly select it
            if (path.Contains("Prefabs/Shop Items")) continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string cleanName = CleanName(prefab.name);
            ShopItemCategory tier = DetermineRewardTier(prefab.name);

            // Attempt to check if an asset already exists
            string assetName = prefab.name.Replace(" ", "_") + "_Data";
            string assetPath = $"{rewardOutputFolder}/{assetName}.asset";
            TreasureBoxRewardItemData existingData = AssetDatabase.LoadAssetAtPath<TreasureBoxRewardItemData>(assetPath);

            int xpLevel = existingData != null ? existingData.unlockXPLevel : rewardDefaultXPLevel;
            int noorCoins = existingData != null ? existingData.noorCoinAmount : rewardDefaultNoorCoinAmount;
            float duration = existingData != null ? existingData.placementTimerDuration : rewardDefaultPlacementDuration;
            Sprite icon = existingData != null ? existingData.itemIcon : null;
            if (existingData != null)
            {
                tier = existingData.itemCategory;
                cleanName = existingData.itemName;
            }
            else
            {
                // Try to find a matching icon
                string possibleIconPath = $"Assets/2D Assets/Icons/{prefab.name}.png";
                icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                if (icon == null)
                {
                    possibleIconPath = $"Assets/2D Assets/Icons/{cleanName.Replace(" ", "_")}.png";
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(possibleIconPath);
                }
            }

            rewardItems.Add(new TreasureRewardConfig
            {
                prefab = prefab,
                selected = true,
                cleanName = cleanName,
                tier = tier,
                unlockXPLevel = xpLevel,
                noorCoinAmount = noorCoins,
                placementTimerDuration = duration,
                icon = icon
            });
        }
    }

    private void DrawRewardGeneratorGUI()
    {
        GUILayout.Label("Bulk Treasure Box Reward Data Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Folders setup
        rewardPrefabFolder = DrawFolderSelector("Prefab Source Folder", rewardPrefabFolder);
        rewardOutputFolder = DrawFolderSelector("Output Resources Folder", rewardOutputFolder);
        EditorGUILayout.Space();

        // Default configuration
        GUILayout.Label("Default Settings for New Rewards", EditorStyles.boldLabel);
        rewardDefaultXPLevel = EditorGUILayout.IntField("Default Unlock XP Level", rewardDefaultXPLevel);
        rewardDefaultNoorCoinAmount = EditorGUILayout.IntField("Default Noor Coin Backup", rewardDefaultNoorCoinAmount);
        rewardDefaultPlacementDuration = EditorGUILayout.FloatField("Default Placement Duration (s)", rewardDefaultPlacementDuration);
        rewardOverwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Fields", rewardOverwriteExisting);
        EditorGUILayout.Space();

        if (GUILayout.Button("Scan / Refresh Prefabs Folder", GUILayout.Height(30)))
        {
            LoadRewardPrefabs();
        }
        EditorGUILayout.Space();

        // Control buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All"))
        {
            SetAllRewardSelection(true);
        }
        if (GUILayout.Button("Deselect All"))
        {
            SetAllRewardSelection(false);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // Scrollview list
        rewardScrollPos = EditorGUILayout.BeginScrollView(rewardScrollPos, GUILayout.ExpandHeight(true));

        // Header
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Gen", GUILayout.Width(35));
        GUILayout.Label("Prefab Name", GUILayout.Width(150));
        GUILayout.Label("Icon", GUILayout.Width(100));
        GUILayout.Label("Display Name", GUILayout.Width(150));
        GUILayout.Label("Tier", GUILayout.Width(90));
        GUILayout.Label("Unlock XP", GUILayout.Width(70));
        GUILayout.Label("Coins", GUILayout.Width(60));
        GUILayout.Label("Duration", GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < rewardItems.Count; i++)
        {
            var item = rewardItems[i];
            EditorGUILayout.BeginHorizontal();

            item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(35));
            EditorGUILayout.LabelField(item.prefab.name, GUILayout.Width(150));
            item.icon = (Sprite)EditorGUILayout.ObjectField(item.icon, typeof(Sprite), false, GUILayout.Width(100));
            item.cleanName = EditorGUILayout.TextField(item.cleanName, GUILayout.Width(150));
            item.tier = (ShopItemCategory)EditorGUILayout.EnumPopup(item.tier, GUILayout.Width(90));
            item.unlockXPLevel = EditorGUILayout.IntField(item.unlockXPLevel, GUILayout.Width(70));
            item.noorCoinAmount = EditorGUILayout.IntField(item.noorCoinAmount, GUILayout.Width(60));
            item.placementTimerDuration = EditorGUILayout.FloatField(item.placementTimerDuration, GUILayout.Width(60));

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Selected Treasure Box Reward Items", GUILayout.Height(40)))
        {
            GenerateSelectedRewardItems();
        }
    }

    private void SetAllRewardSelection(bool value)
    {
        foreach (var item in rewardItems)
        {
            item.selected = value;
        }
    }

    private void GenerateSelectedRewardItems()
    {
        EnsureFolderExists(rewardOutputFolder);

        int count = 0;
        foreach (var item in rewardItems)
        {
            if (!item.selected) continue;

            string assetName = item.prefab.name.Replace(" ", "_") + "_Data";
            string assetPath = $"{rewardOutputFolder}/{assetName}.asset";

            TreasureBoxRewardItemData itemData = AssetDatabase.LoadAssetAtPath<TreasureBoxRewardItemData>(assetPath);
            bool isNew = false;

            if (itemData == null)
            {
                itemData = CreateInstance<TreasureBoxRewardItemData>();
                isNew = true;
            }

            if (isNew || rewardOverwriteExisting)
            {
                itemData.itemName = item.cleanName;
                itemData.itemDescription = $"A special reward item: {item.cleanName}. Find it to customize your garden!";
                itemData.itemCategory = item.tier;
                itemData.unlockXPLevel = item.unlockXPLevel;
                itemData.noorCoinAmount = item.noorCoinAmount;
                itemData.placementTimerDuration = item.placementTimerDuration;
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

        AssignRewardsToTreasureBoxData();

        EditorUtility.DisplayDialog("Success", $"Successfully generated/updated {count} TreasureBoxRewardItemData assets and assigned them to TreasureBoxData.", "OK");
        LoadRewardPrefabs();
    }

    private void AssignRewardsToTreasureBoxData()
    {
        string tbDataFolder = "Assets/Resources/TreasureBoxData";
        if (!AssetDatabase.IsValidFolder(tbDataFolder))
        {
            Debug.LogWarning($"[BulkItemGeneratorWindow] TreasureBoxData folder not found at: {tbDataFolder}");
            return;
        }

        // Find all TreasureBoxRewardItemData assets in the rewardOutputFolder
        string[] rewardItemGuids = AssetDatabase.FindAssets("t:TreasureBoxRewardItemData", new[] { rewardOutputFolder });
        
        List<TreasureBoxRewardItemData> silverRewards = new List<TreasureBoxRewardItemData>();
        List<TreasureBoxRewardItemData> goldRewards = new List<TreasureBoxRewardItemData>();
        List<TreasureBoxRewardItemData> platinumRewards = new List<TreasureBoxRewardItemData>();
        List<TreasureBoxRewardItemData> diamondRewards = new List<TreasureBoxRewardItemData>();

        foreach (string guid in rewardItemGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TreasureBoxRewardItemData rewardItem = AssetDatabase.LoadAssetAtPath<TreasureBoxRewardItemData>(path);
            if (rewardItem == null) continue;

            switch (rewardItem.itemCategory)
            {
                case ShopItemCategory.Silver:
                    silverRewards.Add(rewardItem);
                    break;
                case ShopItemCategory.Gold:
                    goldRewards.Add(rewardItem);
                    break;
                case ShopItemCategory.Platinum:
                    platinumRewards.Add(rewardItem);
                    break;
                case ShopItemCategory.Diamond:
                    diamondRewards.Add(rewardItem);
                    break;
            }
        }

        // Helper function to load TreasureBoxData, assign and save
        UpdateTierRewards(TreasureBoxTier.Silver, silverRewards, $"{tbDataFolder}/TreasureBoxData_Silver.asset");
        UpdateTierRewards(TreasureBoxTier.Gold, goldRewards, $"{tbDataFolder}/TreasureBoxData_Gold.asset");
        UpdateTierRewards(TreasureBoxTier.Platinum, platinumRewards, $"{tbDataFolder}/TreasureBoxData_Platinum.asset");
        UpdateTierRewards(TreasureBoxTier.Diamond, diamondRewards, $"{tbDataFolder}/TreasureBoxData_Diamond.asset");

        AssetDatabase.SaveAssets();
    }

    private void UpdateTierRewards(TreasureBoxTier tier, List<TreasureBoxRewardItemData> rewards, string assetPath)
    {
        TreasureBoxData tbData = AssetDatabase.LoadAssetAtPath<TreasureBoxData>(assetPath);
        if (tbData != null)
        {
            tbData.exclusiveRewardItems = rewards.ToArray();
            EditorUtility.SetDirty(tbData);
            Debug.Log($"[BulkItemGeneratorWindow] Assigned {rewards.Count} items to {tier} TreasureBoxData.");
        }
        else
        {
            Debug.LogWarning($"[BulkItemGeneratorWindow] TreasureBoxData asset not found at path: {assetPath}");
        }
    }

    // ==========================================
    // UTILITY METHODS
    // ==========================================

    private static void EnsureFolderExists(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
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
        => ShopItemClassifier.DetermineCategory(prefabName);

    private static ShopItemCategory DetermineRewardTier(string prefabName)
        => ShopItemClassifier.DetermineRewardTier(prefabName);

    private static string GenerateShopDescription(string itemName, ShopItemCategory category)
        => ShopItemClassifier.GenerateDescription(itemName, category);

    private string DrawFolderSelector(string label, string currentPath)
    {
        EditorGUILayout.BeginHorizontal();
        string newPath = EditorGUILayout.TextField(label, currentPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            // Set starting path relative to project
            string startDir = Application.dataPath;
            if (!string.IsNullOrEmpty(currentPath) && currentPath.StartsWith("Assets/"))
            {
                startDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, currentPath).Replace("\\", "/");
            }
            
            string absolutePath = EditorUtility.OpenFolderPanel($"Select {label}", startDir, "");
            if (!string.IsNullOrEmpty(absolutePath))
            {
                // Convert absolute path to project relative path starting with "Assets"
                if (absolutePath.StartsWith(Application.dataPath))
                {
                    newPath = "Assets" + absolutePath.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside the Assets directory.", "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        return newPath;
    }

    private void DrawBoxDataEditorGUI()
    {
        GUILayout.Label("Treasure Box Data Editor", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (silverBoxData == null || goldBoxData == null || platinumBoxData == null || diamondBoxData == null)
        {
            if (GUILayout.Button("Load Treasure Box Data Assets", GUILayout.Height(30)))
            {
                LoadTreasureBoxDatas();
            }
            EditorGUILayout.HelpBox("Some or all TreasureBoxData assets are missing. Make sure they are located at Assets/Resources/TreasureBoxData/.", MessageType.Warning);
            return;
        }

        editorScrollPos = EditorGUILayout.BeginScrollView(editorScrollPos, GUILayout.ExpandHeight(true));

        DrawTierEditor(silverBoxData);
        EditorGUILayout.Space();
        DrawTierEditor(goldBoxData);
        EditorGUILayout.Space();
        DrawTierEditor(platinumBoxData);
        EditorGUILayout.Space();
        DrawTierEditor(diamondBoxData);

        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-Assign Rewards from Resources Folder", GUILayout.Height(40)))
        {
            AssignRewardsToTreasureBoxData();
            LoadTreasureBoxDatas();
            EditorUtility.DisplayDialog("Success", "Successfully assigned rewards to respective TreasureBoxData assets from the resources folder.", "OK");
        }
        if (GUILayout.Button("Save All Changes", GUILayout.Height(40)))
        {
            SaveAllBoxDataChanges();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTierEditor(TreasureBoxData data)
    {
        if (data == null) return;

        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label($"{data.tier} Box Data Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        data.tierDisplayName = EditorGUILayout.TextField("Display Name", data.tierDisplayName);
        data.boxPrefab = (GameObject)EditorGUILayout.ObjectField("Box Prefab", data.boxPrefab, typeof(GameObject), false);
        data.spawnCooldownHours = EditorGUILayout.FloatField("Spawn Cooldown (Hours)", data.spawnCooldownHours);
        data.cycleDurationHours = EditorGUILayout.FloatField("Cycle Duration (Hours)", data.cycleDurationHours);
        data.noorCoinEquivalent = EditorGUILayout.IntField("NC Equivalent (Backup)", data.noorCoinEquivalent);
        data.noorCoinPerBox = EditorGUILayout.IntField("Noor Coin Per Box", data.noorCoinPerBox);

        // Display Assigned exclusive rewards list
        EditorGUILayout.Space();
        GUILayout.Label("Assigned Exclusive Rewards", EditorStyles.miniBoldLabel);
        if (data.exclusiveRewardItems == null || data.exclusiveRewardItems.Length == 0)
        {
            GUILayout.Label("No rewards assigned yet.", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < data.exclusiveRewardItems.Length; i++)
            {
                var item = data.exclusiveRewardItems[i];
                if (item != null)
                {
                    EditorGUILayout.LabelField($"- {item.itemName} (ID: {item.itemID})");
                }
            }
        }

        GUILayout.EndVertical();
    }

    private void SaveAllBoxDataChanges()
    {
        if (silverBoxData != null) EditorUtility.SetDirty(silverBoxData);
        if (goldBoxData != null) EditorUtility.SetDirty(goldBoxData);
        if (platinumBoxData != null) EditorUtility.SetDirty(platinumBoxData);
        if (diamondBoxData != null) EditorUtility.SetDirty(diamondBoxData);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Success", "Successfully saved all changes to Treasure Box Data assets.", "OK");
    }
}
#endif
