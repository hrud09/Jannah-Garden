#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws ShopItemData grouped by acquisition type: only the block that belongs to the
/// selected <see cref="ShopAcquisitionType"/> is shown, so a rewarded-ad offer never
/// displays an IAP product ID and a coin item never displays an ad cooldown.
/// </summary>
[CustomEditor(typeof(ShopItemData))]
[CanEditMultipleObjects]
public class ShopItemDataEditor : Editor
{
    SerializedProperty itemIcon, itemName, itemDescription, itemID;
    SerializedProperty acquisitionType;
    SerializedProperty noorCoinCost;
    SerializedProperty isDailyOffer, offerCooldownHours;
    SerializedProperty iapProductId, realMoneyPriceLabel;
    SerializedProperty noorCoinReward;
    SerializedProperty itemCategory, itemTier, sortOrder, requiredXPLevel;
    SerializedProperty itemPrefab, itemPlacementModelPrefab, placementTimerDuration;

    void OnEnable()
    {
        itemIcon = serializedObject.FindProperty(nameof(ShopItemData.itemIcon));
        itemName = serializedObject.FindProperty(nameof(ShopItemData.itemName));
        itemDescription = serializedObject.FindProperty(nameof(ShopItemData.itemDescription));
        itemID = serializedObject.FindProperty(nameof(ShopItemData.itemID));

        acquisitionType = serializedObject.FindProperty(nameof(ShopItemData.acquisitionType));

        noorCoinCost = serializedObject.FindProperty(nameof(ShopItemData.noorCoinCost));
        isDailyOffer = serializedObject.FindProperty(nameof(ShopItemData.isDailyOffer));
        offerCooldownHours = serializedObject.FindProperty(nameof(ShopItemData.offerCooldownHours));
        iapProductId = serializedObject.FindProperty(nameof(ShopItemData.iapProductId));
        realMoneyPriceLabel = serializedObject.FindProperty(nameof(ShopItemData.realMoneyPriceLabel));
        noorCoinReward = serializedObject.FindProperty(nameof(ShopItemData.noorCoinReward));

        itemCategory = serializedObject.FindProperty(nameof(ShopItemData.itemCategory));
        itemTier = serializedObject.FindProperty(nameof(ShopItemData.itemTier));
        sortOrder = serializedObject.FindProperty(nameof(ShopItemData.sortOrder));
        requiredXPLevel = serializedObject.FindProperty(nameof(ShopItemData.requiredXPLevel));

        itemPrefab = serializedObject.FindProperty(nameof(ShopItemData.itemPrefab));
        itemPlacementModelPrefab = serializedObject.FindProperty(nameof(ShopItemData.itemPlacementModelPrefab));
        placementTimerDuration = serializedObject.FindProperty(nameof(ShopItemData.placementTimerDuration));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Section("Item Metadata");
        EditorGUILayout.PropertyField(itemIcon);
        EditorGUILayout.PropertyField(itemName);
        EditorGUILayout.PropertyField(itemDescription);
        EditorGUILayout.PropertyField(itemID);

        Section("Acquisition");
        EditorGUILayout.PropertyField(acquisitionType);

        // With a mixed selection there is no single acquisition type to branch on,
        // so fall back to showing every block.
        bool mixed = acquisitionType.hasMultipleDifferentValues;
        var type = (ShopAcquisitionType)acquisitionType.enumValueIndex;

        if (mixed || type == ShopAcquisitionType.NoorCoins)
        {
            Section("Noor Coin Price");
            EditorGUILayout.PropertyField(noorCoinCost, new GUIContent("Noor Coin Cost"));
        }

        if (mixed || type == ShopAcquisitionType.RewardedAd)
        {
            Section("Rewarded Ad");
            EditorGUILayout.PropertyField(isDailyOffer, new GUIContent("Is Daily Offer"));
            using (new EditorGUI.DisabledScope(!isDailyOffer.boolValue && !isDailyOffer.hasMultipleDifferentValues))
            {
                EditorGUILayout.PropertyField(offerCooldownHours, new GUIContent("Offer Cooldown Hours"));
            }
        }

        if (mixed || type == ShopAcquisitionType.InAppPurchase)
        {
            Section("In-App Purchase");
            EditorGUILayout.PropertyField(iapProductId, new GUIContent("IAP Product ID"));
            EditorGUILayout.PropertyField(realMoneyPriceLabel, new GUIContent("Real Money Price Label"));
        }

        // A coin payout only makes sense for the two types that can hand over coins.
        if (mixed || type != ShopAcquisitionType.NoorCoins)
        {
            Section("Coin Payout");
            EditorGUILayout.PropertyField(noorCoinReward, new GUIContent("Noor Coin Reward"));
        }

        Section("Shop Category & Unlock");
        EditorGUILayout.PropertyField(itemCategory);

        // Coin packs and ad offers live in the Noor Coins tab and are not ranked, so the tier field
        // would only be noise there.
        var category = (ShopItemCategory)itemCategory.intValue;
        bool tiered = itemCategory.hasMultipleDifferentValues || category != ShopItemCategory.NoorCoins;
        if (tiered)
        {
            EditorGUILayout.PropertyField(itemTier);

            if (!itemTier.hasMultipleDifferentValues)
            {
                var tier = (ShopItemTier)itemTier.intValue;
                EditorGUILayout.LabelField(" ", tier == ShopItemTier.None
                    ? "No tier — this item will not show a tier badge."
                    : $"Shows as \"{ShopTaxonomy.GetTierLabel(tier)}\" in "
                      + $"{ShopTaxonomy.GetCategoryLongName(category)}.", EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.PropertyField(sortOrder);
        EditorGUILayout.PropertyField(requiredXPLevel);

        Section("Placement");
        EditorGUILayout.PropertyField(itemPrefab);
        EditorGUILayout.PropertyField(itemPlacementModelPrefab);

        bool placeable = itemPrefab.objectReferenceValue != null || itemPrefab.hasMultipleDifferentValues;
        if (placeable)
        {
            EditorGUILayout.PropertyField(placementTimerDuration);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No Item Prefab: this item pays out only (e.g. a coin pack) and skips placement.",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    static void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
#endif
