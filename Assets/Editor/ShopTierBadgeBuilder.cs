#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Adds the tier badge to the shop item card prefab and wires it to <see cref="ShopItemUI"/>.
///
/// <see cref="ShopItemUI"/> paints the badge from <see cref="ShopTaxonomy"/> at runtime, but the
/// widgets themselves have to exist on the card. This drops in a plain badge — a rounded sprite with
/// a label, anchored top-right — inheriting the card's own font so it is legible immediately. It is
/// deliberately unstyled: move, resize and reskin it in the prefab afterwards, the wiring survives.
///
/// Safe to re-run: if the card already has a tier label wired, nothing happens.
/// </summary>
public static class ShopTierBadgeBuilder
{
    const string CardPrefabPath = "Assets/Prefabs/UI/Shop Item UI.prefab";
    const string BadgeName = "Tier Badge";

    [MenuItem("Tools/Shop/Add Tier Badge To Item Card")]
    public static void AddTierBadge()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[ShopTierBadge] Could not open \"{CardPrefabPath}\".");
            return;
        }

        try
        {
            var card = root.GetComponent<ShopItemUI>() ?? root.GetComponentInChildren<ShopItemUI>(true);
            if (card == null)
            {
                Debug.LogError($"[ShopTierBadge] No ShopItemUI inside \"{CardPrefabPath}\".");
                return;
            }

            if (card.itemTierText != null && card.itemTierBadgeImg != null)
            {
                Debug.Log($"[ShopTierBadge] \"{CardPrefabPath}\" already has a tier badge — nothing to do.");
                return;
            }

            var cardRect = card.GetComponent<RectTransform>();
            if (cardRect == null)
            {
                Debug.LogError("[ShopTierBadge] The ShopItemUI GameObject has no RectTransform.");
                return;
            }

            // Reuse an existing badge if a previous run left one behind, so re-running does not stack.
            Transform existing = cardRect.Find(BadgeName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var badge = new GameObject(BadgeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.SetParent(cardRect, false);

            // Top-right corner, clear of the icon and the name plate.
            badgeRect.anchorMin = new Vector2(1f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.anchoredPosition = new Vector2(-10f, -10f);
            badgeRect.sizeDelta = new Vector2(150f, 34f);

            var badgeImage = badge.GetComponent<Image>();
            badgeImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            badgeImage.type = Image.Type.Sliced;
            badgeImage.raycastTarget = false; // the whole card is one button
            badgeImage.color = ShopTaxonomy.GetTierColor(ShopItemTier.Tier1);

            var labelObj = new GameObject("Tier Label", typeof(RectTransform));
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.SetParent(badgeRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 2f);
            labelRect.offsetMax = new Vector2(-6f, -2f);

            var label = labelObj.AddComponent<TextMeshProUGUI>();
            label.text = ShopTaxonomy.GetTierLabel(ShopItemTier.Tier1);
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = 22f;
            label.color = Color.black;
            label.raycastTarget = false;

            // Inherit the card's typeface so the badge does not fall back to the default font.
            if (card.itemNameText != null)
            {
                label.font = card.itemNameText.font;
                label.fontSharedMaterial = card.itemNameText.fontSharedMaterial;
            }

            card.itemTierBadgeImg = badgeImage;
            card.itemTierText = label;

            // The badge sprite carries the tint, the label stays readable on top of it.
            card.tintTierVisuals = true;

            EditorUtility.SetDirty(card);
            PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ShopTierBadge] Added \"{BadgeName}\" to \"{CardPrefabPath}\" and wired it to "
                + "ShopItemUI (Item Tier Badge Img + Item Tier Text). Restyle it in the prefab to taste — "
                + "the colour and label are driven by ShopTaxonomy at runtime.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
