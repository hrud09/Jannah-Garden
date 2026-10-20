#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Authors the "get more Noor Coins" popup inside the In-Game Shop prefab and wires it to
/// <see cref="InGameShopManager.noorCoinStorePopup"/>.
///
/// The scrolling body is a clone of the shop's own Noor Coins category panel, so the popup inherits
/// whatever viewport, grid and card spacing that tab already has instead of inventing a second
/// layout that drifts from it. Everything else — the dim backdrop, the window plate, the two labels
/// and the close button — is built here, borrowing sprites and fonts from the shop so the panel
/// reads as part of the same UI kit.
///
/// Safe to re-run: an existing popup is torn out and rebuilt from scratch.
/// </summary>
public static class NoorCoinPopupBuilder
{
    const string ShopPrefabPath = "Assets/Prefabs/UI/In-Game Shop.prefab";
    const string PopupName = "Noor Coin Popup";

    [MenuItem("Tools/Shop/Build Noor Coin Popup")]
    public static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ShopPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[NoorCoinPopup] Could not open \"{ShopPrefabPath}\".");
            return;
        }

        try
        {
            var manager = root.GetComponentInChildren<InGameShopManager>(true);
            if (manager == null)
            {
                Debug.LogError($"[NoorCoinPopup] No InGameShopManager inside \"{ShopPrefabPath}\".");
                return;
            }

            CategoryTab coinTab = manager.categoryTabs?.FirstOrDefault(
                t => t != null && t.category == ShopItemCategory.NoorCoins
                     && t.categoryPanel != null && t.contentParent != null);

            if (coinTab == null)
            {
                Debug.LogError("[NoorCoinPopup] The shop has no Noor Coins tab with both a Category "
                    + "Panel and a Content Parent — run Tools/Shop/Rebuild Category Tabs first.");
                return;
            }

            // Rebuild from scratch so re-running picks up changes to this builder.
            Transform existing = root.transform.Find(PopupName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            GameObject popupGO = NewUIObject(PopupName, root.transform);
            Stretch(popupGO.GetComponent<RectTransform>());
            popupGO.transform.SetAsLastSibling(); // draws over the shop panel and its open button

            // The component lives on the always-active root and toggles the overlay beneath it, so its
            // Awake runs (and NoorCoinStorePopup.Instance is set) even while the popup is hidden.
            var popup = popupGO.AddComponent<NoorCoinStorePopup>();

            GameObject overlay = NewUIObject("Overlay", popupGO.transform);
            Stretch(overlay.GetComponent<RectTransform>());
            CanvasGroup group = overlay.AddComponent<CanvasGroup>();

            // ── Backdrop ──────────────────────────────────────────────────────
            GameObject backdrop = NewUIObject("Backdrop", overlay.transform);
            Stretch(backdrop.GetComponent<RectTransform>());
            Image backdropImg = backdrop.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.72f);
            Button backdropBtn = backdrop.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.targetGraphic = backdropImg;

            // ── Window ────────────────────────────────────────────────────────
            GameObject window = NewUIObject("Window", overlay.transform);
            RectTransform windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.06f, 0.13f);
            windowRect.anchorMax = new Vector2(0.94f, 0.87f);
            windowRect.offsetMin = Vector2.zero;
            windowRect.offsetMax = Vector2.zero;

            Image windowImg = window.AddComponent<Image>();
            Image shopPlate = manager.mainBg != null ? manager.mainBg.GetComponent<Image>() : null;
            if (shopPlate != null && shopPlate.sprite != null)
            {
                windowImg.sprite = shopPlate.sprite;
                windowImg.type = shopPlate.type;
                windowImg.pixelsPerUnitMultiplier = shopPlate.pixelsPerUnitMultiplier;
                windowImg.color = shopPlate.color;
            }
            else
            {
                windowImg.color = new Color(0.98f, 0.96f, 0.90f, 1f);
            }

            TMP_FontAsset font = manager.shopText != null ? manager.shopText.font : null;
            Color headingColor = manager.shopText != null ? manager.shopText.color : Color.white;

            // ── Labels ────────────────────────────────────────────────────────
            TMP_Text title = NewLabel("Title (TMP)", window.transform, font, 46f, FontStyles.Bold,
                                      headingColor, "Get Noor Coins");
            TopAnchored(title.rectTransform, height: 64f, topOffset: -18f, sidePadding: 40f);

            TMP_Text message = NewLabel("Message (TMP)", window.transform, font, 30f, FontStyles.Normal,
                                        headingColor, "You need 120 more Noor Coins");
            TopAnchored(message.rectTransform, height: 52f, topOffset: -86f, sidePadding: 40f);

            // ── Offer list (a clone of the shop's Noor Coins tab) ─────────────
            string contentPath = RelativePath(coinTab.categoryPanel.transform, coinTab.contentParent);

            GameObject list = Object.Instantiate(coinTab.categoryPanel, window.transform);
            list.name = "Offer List";
            list.SetActive(true);

            RectTransform listRect = list.GetComponent<RectTransform>();
            if (listRect != null)
            {
                listRect.anchorMin = Vector2.zero;
                listRect.anchorMax = Vector2.one;
                listRect.pivot = new Vector2(0.5f, 0.5f);
                listRect.offsetMin = new Vector2(28f, 110f);   // leaves room for the close button
                listRect.offsetMax = new Vector2(-28f, -150f); // leaves room for the two labels
                listRect.localScale = Vector3.one;
            }

            Transform content = string.IsNullOrEmpty(contentPath) ? list.transform : list.transform.Find(contentPath);
            if (content == null)
            {
                Debug.LogWarning($"[NoorCoinPopup] Could not find \"{contentPath}\" inside the cloned "
                    + "Noor Coins panel — falling back to the clone's root as the card parent.");
                content = list.transform;
            }

            // The cloned panel carries no cards (they are spawned at runtime), but clear any that a
            // designer left in by hand so the popup doesn't ship with dead duplicates.
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                if (content.GetChild(i).GetComponent<ShopItemUI>() != null)
                {
                    Object.DestroyImmediate(content.GetChild(i).gameObject);
                }
            }

            // ── Close button ──────────────────────────────────────────────────
            // A straight clone of the Select button off a shop card, rather than a fresh Image given
            // that button's sprite: the pill is nine-sliced and only reads correctly at the size and
            // border settings it was authored with, which a hand-built rect stretches out of shape.
            Button cardButton = CardPurchaseButton(manager);
            GameObject closeGO;
            TMP_Text closeLabel;

            if (cardButton != null)
            {
                closeGO = Object.Instantiate(cardButton.gameObject, window.transform);
                closeGO.name = "Close Button";
                closeGO.SetActive(true);

                Button cloned = closeGO.GetComponent<Button>();
                cloned.onClick = new Button.ButtonClickedEvent();
                cloned.interactable = true;

                closeLabel = closeGO.GetComponentInChildren<TMP_Text>(true);
                if (closeLabel != null) closeLabel.text = "Close";

                // Strip anything that only makes sense on a card.
                foreach (var layout in closeGO.GetComponents<LayoutElement>()) Object.DestroyImmediate(layout);
            }
            else
            {
                closeGO = NewUIObject("Close Button", window.transform);
                Image plainPlate = closeGO.AddComponent<Image>();
                plainPlate.color = new Color(0.20f, 0.38f, 0.24f, 1f);
                closeGO.AddComponent<Button>().targetGraphic = plainPlate;

                closeLabel = NewLabel("Label (TMP)", closeGO.transform, font, 32f, FontStyles.Bold,
                                      Color.white, "Close");
                Stretch(closeLabel.rectTransform);
            }

            RectTransform closeRect = closeGO.GetComponent<RectTransform>();
            Vector2 authoredSize = cardButton != null
                ? cardButton.GetComponent<RectTransform>().rect.size
                : new Vector2(260f, 72f);

            closeRect.anchorMin = new Vector2(0.5f, 0f);
            closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.sizeDelta = authoredSize; // the size the pill art was drawn for
            closeRect.anchoredPosition = new Vector2(0f, 22f);
            closeRect.localScale = Vector3.one;
            closeRect.localRotation = Quaternion.identity;

            Button closeBtn = closeGO.GetComponent<Button>();

            // ── Wiring ────────────────────────────────────────────────────────
            popup.panelRoot = overlay;
            popup.canvasGroup = group;
            popup.window = windowRect;
            popup.contentParent = content;
            popup.shopItemUIPrefab = null; // borrows the shop's own card prefab
            popup.titleText = title;
            popup.messageText = message;
            popup.closeButton = closeBtn;
            popup.closeButtonLabel = closeLabel;
            popup.backdropButton = backdropBtn;

            manager.noorCoinStorePopup = popup;

            overlay.SetActive(false);

            EditorUtility.SetDirty(popupGO);
            PrefabUtility.SaveAsPrefabAsset(root, ShopPrefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[NoorCoinPopup] Built \"{PopupName}\" in {ShopPrefabPath} — cards will spawn "
                + $"under \"{content.name}\". Labels are placeholders; they are rewritten from the "
                + "active locale at runtime.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The purchase button on the shop's card prefab, cloned as the popup's close button so
    /// the two share one pill. Null when the prefab or its button reference is missing.</summary>
    static Button CardPurchaseButton(InGameShopManager manager)
    {
        ShopItemUI card = manager.ShopItemUIPrefab != null
            ? manager.ShopItemUIPrefab.GetComponent<ShopItemUI>()
            : null;

        return card != null ? card.purchaseButton : null;
    }

    static GameObject NewUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static void TopAnchored(RectTransform rect, float height, float topOffset, float sidePadding)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(sidePadding, 0f);
        rect.offsetMax = new Vector2(-sidePadding, 0f);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        rect.anchoredPosition = new Vector2(0f, topOffset);
    }

    static TMP_Text NewLabel(string name, Transform parent, TMP_FontAsset font, float size,
                             FontStyles style, Color color, string placeholder)
    {
        GameObject go = NewUIObject(name, parent);
        var label = go.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.text = placeholder;
        return label;
    }

    /// <summary>Path of <paramref name="child"/> relative to <paramref name="ancestor"/>, as
    /// Transform.Find expects it. Empty when the two are the same object.</summary>
    static string RelativePath(Transform ancestor, Transform child)
    {
        var parts = new List<string>();
        for (Transform t = child; t != null && t != ancestor; t = t.parent)
        {
            parts.Add(t.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
