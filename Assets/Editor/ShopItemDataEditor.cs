#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a <see cref="ShopItemData"/> list entry grouped by acquisition type: only the block that
/// belongs to the selected <see cref="ShopAcquisitionType"/> is shown, so a rewarded-ad offer never
/// displays an IAP product ID and a coin item never displays an ad cooldown.
///
/// A PropertyDrawer rather than a CustomEditor — ShopItemData is a plain serializable class living
/// inside a <see cref="ShopItemCategoryGroup.items"/> list, not its own asset, so there is no Editor to
/// attach to.
/// </summary>
[CustomPropertyDrawer(typeof(ShopItemData))]
public class ShopItemDataEditor : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        SerializedProperty itemIcon = property.FindPropertyRelative(nameof(ShopItemData.itemIcon));
        SerializedProperty itemName = property.FindPropertyRelative(nameof(ShopItemData.itemName));
        SerializedProperty itemDescription = property.FindPropertyRelative(nameof(ShopItemData.itemDescription));
        SerializedProperty itemID = property.FindPropertyRelative(nameof(ShopItemData.itemID));

        SerializedProperty acquisitionType = property.FindPropertyRelative(nameof(ShopItemData.acquisitionType));

        SerializedProperty noorCoinCost = property.FindPropertyRelative(nameof(ShopItemData.noorCoinCost));
        SerializedProperty isDailyOffer = property.FindPropertyRelative(nameof(ShopItemData.isDailyOffer));
        SerializedProperty offerCooldownHours = property.FindPropertyRelative(nameof(ShopItemData.offerCooldownHours));
        SerializedProperty iapProductId = property.FindPropertyRelative(nameof(ShopItemData.iapProductId));
        SerializedProperty realMoneyPriceLabel = property.FindPropertyRelative(nameof(ShopItemData.realMoneyPriceLabel));
        SerializedProperty noorCoinReward = property.FindPropertyRelative(nameof(ShopItemData.noorCoinReward));

        SerializedProperty itemCategory = property.FindPropertyRelative(nameof(ShopItemData.itemCategory));
        SerializedProperty itemTier = property.FindPropertyRelative(nameof(ShopItemData.itemTier));
        SerializedProperty sortOrder = property.FindPropertyRelative(nameof(ShopItemData.sortOrder));
        SerializedProperty requiredXPLevel = property.FindPropertyRelative(nameof(ShopItemData.requiredXPLevel));

        SerializedProperty itemPrefabRef = property.FindPropertyRelative(nameof(ShopItemData.itemPrefabRef));
        SerializedProperty itemPlacementModelPrefabRef = property.FindPropertyRelative(nameof(ShopItemData.itemPlacementModelPrefabRef));
        SerializedProperty placementTimerDuration = property.FindPropertyRelative(nameof(ShopItemData.placementTimerDuration));
        SerializedProperty gridFootprintOverride = property.FindPropertyRelative(nameof(ShopItemData.gridFootprintOverride));
        SerializedProperty footprintScale = property.FindPropertyRelative(nameof(ShopItemData.footprintScale));

        property.isExpanded = EditorGUI.Foldout(
            new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
            property.isExpanded,
            string.IsNullOrEmpty(itemName.stringValue) ? label.text : itemName.stringValue,
            true);

        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        void Field(SerializedProperty prop, GUIContent content = null)
        {
            float height = EditorGUI.GetPropertyHeight(prop, content, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), prop, content, true);
            y += height + EditorGUIUtility.standardVerticalSpacing;
        }

        void Section(string title)
        {
            y += 4f;
            EditorGUI.LabelField(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight), title, EditorStyles.boldLabel);
            y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        Section("Item Metadata");
        Field(itemIcon);
        Field(itemName);
        Field(itemDescription);
        Field(itemID);

        Section("Acquisition");
        Field(acquisitionType);

        var type = (ShopAcquisitionType)acquisitionType.enumValueIndex;

        if (type == ShopAcquisitionType.NoorCoins)
        {
            Section("Noor Coin Price");
            Field(noorCoinCost, new GUIContent("Noor Coin Cost"));
        }

        if (type == ShopAcquisitionType.RewardedAd)
        {
            Section("Rewarded Ad");
            Field(isDailyOffer, new GUIContent("Is Daily Offer"));
            using (new EditorGUI.DisabledScope(!isDailyOffer.boolValue))
            {
                Field(offerCooldownHours, new GUIContent("Offer Cooldown Hours"));
            }
        }

        if (type == ShopAcquisitionType.InAppPurchase)
        {
            Section("In-App Purchase");
            Field(iapProductId, new GUIContent("IAP Product ID"));
            Field(realMoneyPriceLabel, new GUIContent("Real Money Price Label"));
        }

        if (type != ShopAcquisitionType.NoorCoins)
        {
            Section("Coin Payout");
            Field(noorCoinReward, new GUIContent("Noor Coin Reward"));
        }

        Section("Shop Category & Unlock");
        Field(itemCategory);

        var category = (ShopItemCategory)itemCategory.intValue;
        if (category != ShopItemCategory.NoorCoins)
        {
            Field(itemTier);
        }

        Field(sortOrder);
        Field(requiredXPLevel);

        Section("Placement");
        Field(itemPrefabRef);
        Field(itemPlacementModelPrefabRef);

        SerializedProperty itemPrefabGuid = itemPrefabRef.FindPropertyRelative("m_AssetGUID");
        bool placeable = !string.IsNullOrEmpty(itemPrefabGuid.stringValue);
        if (placeable)
        {
            Field(placementTimerDuration);
            Field(gridFootprintOverride);
            Field(footprintScale);
        }
        else
        {
            float boxHeight = EditorGUIUtility.singleLineHeight * 2f;
            EditorGUI.HelpBox(new Rect(position.x, y, position.width, boxHeight),
                "No Item Prefab: this item pays out only (e.g. a coin pack) and skips placement.",
                MessageType.Info);
            y += boxHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;

        SerializedProperty acquisitionType = property.FindPropertyRelative(nameof(ShopItemData.acquisitionType));
        SerializedProperty itemCategory = property.FindPropertyRelative(nameof(ShopItemData.itemCategory));
        SerializedProperty itemPrefabRef = property.FindPropertyRelative(nameof(ShopItemData.itemPrefabRef));

        var type = (ShopAcquisitionType)acquisitionType.enumValueIndex;
        var category = (ShopItemCategory)itemCategory.intValue;

        // Foldout + Item Metadata (4 fields + section)
        int lines = 1 + 1 + 4;

        // Acquisition
        lines += 1 + 1;
        if (type == ShopAcquisitionType.NoorCoins) lines += 1 + 1;
        if (type == ShopAcquisitionType.RewardedAd) lines += 1 + 2;
        if (type == ShopAcquisitionType.InAppPurchase) lines += 1 + 2;
        if (type != ShopAcquisitionType.NoorCoins) lines += 1 + 1;

        // Shop Category & Unlock
        lines += 1 + 1;
        if (category != ShopItemCategory.NoorCoins) lines += 1;
        lines += 2; // sortOrder, requiredXPLevel

        // Placement
        lines += 1 + 2;

        SerializedProperty itemPrefabGuid = itemPrefabRef.FindPropertyRelative("m_AssetGUID");
        bool placeable = !string.IsNullOrEmpty(itemPrefabGuid.stringValue);
        lines += placeable ? 3 : 2; // placementTimerDuration + gridFootprintOverride + footprintScale, or a 2-line help box

        return lines * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) + 8f;
    }
}
#endif
