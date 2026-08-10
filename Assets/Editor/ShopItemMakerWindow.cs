#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class ShopItemMakerWindow : EditorWindow
{
    private string outputFolder = "Assets/Resources/Natural Placeable Shop Items";

    // Item Metadata
    private Sprite itemIcon;
    private string itemName = "New Item";
    private string itemDescription = "Description here...";

    // Acquisition
    private ShopAcquisitionType acquisitionType = ShopAcquisitionType.NoorCoins;
    
    // - Noor Coins
    private int noorCoinCost = 50;
    
    // - Rewarded Ad
    private bool isDailyOffer = false;
    private float offerCooldownHours = 24f;
    
    // - IAP
    private string iapProductId = "";
    private string realMoneyPriceLabel = "$0.99";
    
    // - Payout (Ad/IAP)
    private int noorCoinReward = 0;

    // Presentation
    private ShopItemCategory itemCategory = ShopItemCategory.PlantsAndGardens;
    private ShopItemTier itemTier = ShopItemTier.Tier1;
    private int requiredXPLevel = 1;
    private int sortOrder = 0;

    // Prefabs
    private GameObject itemPrefab;
    private GameObject itemPlacementModelPrefab;
    private float placementTimerDuration = 360f;

    private Vector2 scrollPos;

    [MenuItem("Tools/Shop Item Maker")]
    public static void ShowWindow()
    {
        GetWindow<ShopItemMakerWindow>("Shop Item Maker");
    }

    private void OnGUI()
    {
        GUILayout.Label("Shop Item Maker", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawMetadataSection();
        EditorGUILayout.Space();

        DrawAcquisitionSection();
        EditorGUILayout.Space();

        DrawPresentationSection();
        EditorGUILayout.Space();

        DrawPrefabsSection();
        EditorGUILayout.Space();

        EditorGUILayout.EndScrollView();

        DrawFooterSection();
    }

    private void DrawMetadataSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("1. Basic Information", EditorStyles.boldLabel);
        itemName = EditorGUILayout.TextField("Item Name", itemName);
        
        EditorGUILayout.LabelField("Description");
        itemDescription = EditorGUILayout.TextArea(itemDescription, GUILayout.Height(40));
        
        itemIcon = (Sprite)EditorGUILayout.ObjectField("Icon", itemIcon, typeof(Sprite), false);
        EditorGUILayout.EndVertical();
    }

    private void DrawAcquisitionSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("2. How To Acquire", EditorStyles.boldLabel);
        acquisitionType = (ShopAcquisitionType)EditorGUILayout.EnumPopup("Acquisition Type", acquisitionType);

        EditorGUI.indentLevel++;
        switch (acquisitionType)
        {
            case ShopAcquisitionType.NoorCoins:
                noorCoinCost = EditorGUILayout.IntField("Noor Coin Cost", noorCoinCost);
                break;
            
            case ShopAcquisitionType.RewardedAd:
                isDailyOffer = EditorGUILayout.Toggle("Is Daily Offer?", isDailyOffer);
                if (isDailyOffer)
                {
                    offerCooldownHours = EditorGUILayout.FloatField("Cooldown (Hours)", offerCooldownHours);
                }
                noorCoinReward = EditorGUILayout.IntField("Reward Payout (Coins)", noorCoinReward);
                break;

            case ShopAcquisitionType.InAppPurchase:
                iapProductId = EditorGUILayout.TextField("Product ID", iapProductId);
                realMoneyPriceLabel = EditorGUILayout.TextField("Price Label", realMoneyPriceLabel);
                noorCoinReward = EditorGUILayout.IntField("Reward Payout (Coins)", noorCoinReward);
                break;
        }
        EditorGUI.indentLevel--;

        EditorGUILayout.EndVertical();
    }

    private void DrawPresentationSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("3. Requirements & Sorting", EditorStyles.boldLabel);
        itemCategory = (ShopItemCategory)EditorGUILayout.EnumPopup("Category", itemCategory);
        itemTier = (ShopItemTier)EditorGUILayout.EnumPopup("Tier", itemTier);
        requiredXPLevel = EditorGUILayout.IntField("Required XP Level", requiredXPLevel);
        sortOrder = EditorGUILayout.IntField("Sort Order (Lowest First)", sortOrder);
        EditorGUILayout.EndVertical();
    }

    private void DrawPrefabsSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("4. In-Game Prefabs", EditorStyles.boldLabel);
        itemPrefab = (GameObject)EditorGUILayout.ObjectField("Item Prefab", itemPrefab, typeof(GameObject), false);
        itemPlacementModelPrefab = (GameObject)EditorGUILayout.ObjectField("Placement Ghost Prefab", itemPlacementModelPrefab, typeof(GameObject), false);
        placementTimerDuration = EditorGUILayout.FloatField("Placement Duration (s)", placementTimerDuration);
        EditorGUILayout.EndVertical();
    }

    private void DrawFooterSection()
    {
        EditorGUILayout.BeginVertical("box");
        
        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string startDir = Application.dataPath;
            if (!string.IsNullOrEmpty(outputFolder) && outputFolder.StartsWith("Assets/"))
            {
                startDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, outputFolder).Replace("\\", "/");
            }
            string absolutePath = EditorUtility.OpenFolderPanel("Select Output Folder", startDir, "");
            if (!string.IsNullOrEmpty(absolutePath) && absolutePath.StartsWith(Application.dataPath))
            {
                outputFolder = "Assets" + absolutePath.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Generate Shop Item", GUILayout.Height(40)))
        {
            GenerateShopItem();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
    }

    private void GenerateShopItem()
    {
        if (string.IsNullOrEmpty(itemName))
        {
            EditorUtility.DisplayDialog("Error", "Please provide an item name.", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            EditorUtility.DisplayDialog("Error", $"Invalid output folder: {outputFolder}", "OK");
            return;
        }

        string cleanName = itemName.Replace(" ", "_");
        string assetPath = $"{outputFolder}/{cleanName}_Data.asset";

        // Create new instance
        ShopItemData itemData = CreateInstance<ShopItemData>();

        // Set Metadata
        itemData.itemName = itemName;
        itemData.itemDescription = itemDescription;
        itemData.itemIcon = itemIcon;
        itemData.itemID = System.Guid.NewGuid().ToString("N");

        // Set Acquisition
        itemData.acquisitionType = acquisitionType;
        itemData.noorCoinCost = noorCoinCost;
        itemData.isDailyOffer = isDailyOffer;
        itemData.offerCooldownHours = offerCooldownHours;
        itemData.iapProductId = iapProductId;
        itemData.realMoneyPriceLabel = realMoneyPriceLabel;
        itemData.noorCoinReward = noorCoinReward;

        // Set Presentation
        itemData.itemCategory = itemCategory;
        itemData.itemTier = itemTier;
        itemData.requiredXPLevel = requiredXPLevel;
        itemData.sortOrder = sortOrder;

        // Set Prefabs
        itemData.itemPrefab = itemPrefab;
        itemData.itemPlacementModelPrefab = itemPlacementModelPrefab;
        itemData.placementTimerDuration = placementTimerDuration;

        // Save
        AssetDatabase.CreateAsset(itemData, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Highlight the newly created asset
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = itemData;

        Debug.Log($"[ShopItemMaker] Created new shop item '{itemName}' at {assetPath}");
    }
}
#endif
