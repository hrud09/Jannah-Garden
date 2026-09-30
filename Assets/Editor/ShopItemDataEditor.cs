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
///
/// Drawing and measuring run through the same <see cref="Layout"/> pass. Keeping them as two separate
/// routines is what made entries overlap: the old height estimate counted every field as one line, but
/// itemName/itemDescription are TextAreas several lines tall and each section header adds padding, so
/// the list reserved far less room than OnGUI actually painted.
/// </summary>
[CustomPropertyDrawer(typeof(ShopItemData))]
public class ShopItemDataEditor : PropertyDrawer
{
    /// <summary>Extra gap above a bold section header, and the padding below the last field.</summary>
    private const float SectionSpacing = 6f;
    private const float BottomPadding = 4f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Layout(position, property, label, draw: true);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Width only matters for wrapping help-box text, which is measured at a fixed line count below,
        // so a zero-width probe rect measures the same as the real one.
        return Layout(new Rect(0f, 0f, 0f, 0f), property, label, draw: false);
    }

    /// <summary>
    /// Walks the item's fields once, advancing a cursor. With <paramref name="draw"/> false nothing is
    /// painted and only the total height is returned, guaranteeing the two passes can never disagree.
    /// </summary>
    private float Layout(Rect position, SerializedProperty property, GUIContent label, bool draw)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        if (draw)
        {
            SerializedProperty nameProp = property.FindPropertyRelative(nameof(ShopItemData.itemName));
            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, lineHeight),
                property.isExpanded,
                string.IsNullOrEmpty(nameProp.stringValue) ? label.text : nameProp.stringValue,
                true);
        }

        if (!property.isExpanded) return lineHeight;

        float y = position.y + lineHeight + spacing;

        void Field(string fieldName, GUIContent content = null)
        {
            SerializedProperty prop = property.FindPropertyRelative(fieldName);
            if (prop == null) return;

            float height = EditorGUI.GetPropertyHeight(prop, content, true);
            if (draw) EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), prop, content, true);
            y += height + spacing;
        }

        void Section(string title)
        {
            y += SectionSpacing;
            if (draw)
            {
                EditorGUI.LabelField(new Rect(position.x, y, position.width, lineHeight), title, EditorStyles.boldLabel);
            }
            y += lineHeight + spacing;
        }

        if (draw) EditorGUI.indentLevel++;

        Section("Item Metadata");
        Field(nameof(ShopItemData.itemIcon));
        Field(nameof(ShopItemData.itemName));
        Field(nameof(ShopItemData.itemDescription));
        Field(nameof(ShopItemData.itemID));

        Section("Acquisition");
        Field(nameof(ShopItemData.acquisitionType));

        SerializedProperty acquisitionType = property.FindPropertyRelative(nameof(ShopItemData.acquisitionType));
        var type = (ShopAcquisitionType)acquisitionType.enumValueIndex;

        if (type == ShopAcquisitionType.NoorCoins)
        {
            Section("Noor Coin Price");
            Field(nameof(ShopItemData.noorCoinCost), new GUIContent("Noor Coin Cost"));
        }

        if (type == ShopAcquisitionType.RewardedAd)
        {
            SerializedProperty isDailyOffer = property.FindPropertyRelative(nameof(ShopItemData.isDailyOffer));

            Section("Rewarded Ad");
            Field(nameof(ShopItemData.isDailyOffer), new GUIContent("Is Daily Offer"));
            using (new EditorGUI.DisabledScope(!isDailyOffer.boolValue))
            {
                Field(nameof(ShopItemData.offerCooldownHours), new GUIContent("Offer Cooldown Hours"));
            }
        }

        if (type == ShopAcquisitionType.InAppPurchase)
        {
            Section("In-App Purchase");
            Field(nameof(ShopItemData.iapProductId), new GUIContent("IAP Product ID"));
            Field(nameof(ShopItemData.realMoneyPriceLabel), new GUIContent("Real Money Price Label"));
        }

        if (type != ShopAcquisitionType.NoorCoins)
        {
            Section("Coin Payout");
            Field(nameof(ShopItemData.noorCoinReward), new GUIContent("Noor Coin Reward"));
        }

        Section("Shop Category & Unlock");
        Field(nameof(ShopItemData.itemCategory));

        SerializedProperty itemCategory = property.FindPropertyRelative(nameof(ShopItemData.itemCategory));
        var category = (ShopItemCategory)itemCategory.intValue;
        if (category != ShopItemCategory.NoorCoins)
        {
            Field(nameof(ShopItemData.itemTier));
        }

        Field(nameof(ShopItemData.sortOrder));
        Field(nameof(ShopItemData.requiredXPLevel));

        Section("Placement");
        Field(nameof(ShopItemData.itemPrefabRef));
        Field(nameof(ShopItemData.itemPlacementModelPrefabRef));

        SerializedProperty itemPrefabRef = property.FindPropertyRelative(nameof(ShopItemData.itemPrefabRef));
        SerializedProperty itemPrefabGuid = itemPrefabRef?.FindPropertyRelative("m_AssetGUID");
        bool placeable = itemPrefabGuid != null && !string.IsNullOrEmpty(itemPrefabGuid.stringValue);

        if (placeable)
        {
            Field(nameof(ShopItemData.placementTimerDuration));
            Field(nameof(ShopItemData.gridFootprintOverride));
            Field(nameof(ShopItemData.footprintScale));
        }
        else
        {
            float boxHeight = lineHeight * 2f;
            if (draw)
            {
                EditorGUI.HelpBox(new Rect(position.x, y, position.width, boxHeight),
                    "No Item Prefab: this item pays out only (e.g. a coin pack) and skips placement.",
                    MessageType.Info);
            }
            y += boxHeight + spacing;
        }

        if (draw) EditorGUI.indentLevel--;

        return y - position.y + BottomPadding;
    }
}
#endif
