using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class ShopItemVisuals
{
    public ShopItemType itemType;
    [Header("Item Visual Backgrounds")]
    public Sprite itemBackground;
    public Sprite itemIconBackground;
}

public class InGameShopManager : MonoBehaviour
{
    public ShopItemUI[] shopItemUIs; // Array of ShopItemUI components

    [Header("Selection Status")]
    public ShopItemUI selectedShopItem;

    [Header("Shop Panel Navigation")]
    public RectTransform shopPanel;
    public Button openCloseButton;
    public GameObject openArrow;
    public GameObject closeArrow;
    public float openedPositionX = 0f;
    public float panelTransitionDuration = 0.3f;
    public AnimationCurve scrollCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Item Type Visual Overrides")]
    public List<ShopItemVisuals> itemTypeVisuals; // Configured list in Inspector

    private float closedPositionX;
    private bool isOpen = false;
    private Coroutine panelTransitionCoroutine;

    [Header("Shop Item Data Source")]
    public ShopItemData[] shopItemDatas; // Data assets for each shop item

    [Header("Placement Reference")]
    public ItemPlacementManager placementManager;

    private void Start()
    {
        // Save initial X position as the closed state position
        if (shopPanel != null)
        {
            closedPositionX = shopPanel.anchoredPosition.x;
        }

        // Hook up open/close button click listener
        if (openCloseButton != null)
        {
            openCloseButton.onClick.AddListener(ToggleShop);
        }

        // Initialize shop items with data from ScriptableObjects and visual overrides
        if (shopItemUIs != null && shopItemDatas != null)
        {
            for (int i = 0; i < shopItemUIs.Length; i++)
            {
                if (shopItemUIs[i] != null && i < shopItemDatas.Length && shopItemDatas[i] != null)
                {
                    ShopItemData data = shopItemDatas[i];
                    Sprite bg = null;
                    Sprite iconBg = null;

                    // Match visuals from the global category override settings
                    ShopItemVisuals visuals = GetVisualsForType(data.shopItemType);
                    if (visuals != null)
                    {
                        bg = visuals.itemBackground;
                        iconBg = visuals.itemIconBackground;
                    }

                    shopItemUIs[i].Initialize(data, bg, iconBg);
                }
            }
        }

        // Register click listeners for purchase buttons to select and use items
        if (shopItemUIs != null)
        {
            foreach (var itemUI in shopItemUIs)
            {
                if (itemUI != null && itemUI.purchaseButton != null)
                {
                    ShopItemUI currentItem = itemUI;
                    itemUI.purchaseButton.onClick.AddListener(() => SelectAndUseItem(currentItem));
                }
            }
        }

        // Select the default selected item or first item on start
        if (selectedShopItem != null)
        {
            SelectAndUseItem(selectedShopItem);
        }
        else if (shopItemUIs != null && shopItemUIs.Length > 0 && shopItemUIs[0] != null)
        {
            SelectAndUseItem(shopItemUIs[0]);
        }

        // Initialize default arrow state and panel position (Closed by default)
        SetShopOpen(false, smooth: false);
    }

    private void OnDestroy()
    {
        if (openCloseButton != null)
        {
            openCloseButton.onClick.RemoveListener(ToggleShop);
        }
    }

    /// <summary>
    /// Public method to toggle the open/closed state of the shop.
    /// </summary>
    public void ToggleShop()
    {
        SetShopOpen(!isOpen, smooth: true);
    }

    /// <summary>
    /// Explicitly sets the shop open/closed state, updates the arrow UI states, and slides the panel.
    /// </summary>
    public void SetShopOpen(bool open, bool smooth)
    {
        isOpen = open;

        // Toggle visibility of the arrows
        if (openArrow != null) openArrow.SetActive(!isOpen);
        if (closeArrow != null) closeArrow.SetActive(isOpen);

        float targetX = isOpen ? openedPositionX : closedPositionX;

        if (panelTransitionCoroutine != null)
        {
            StopCoroutine(panelTransitionCoroutine);
        }

        if (smooth && gameObject.activeInHierarchy)
        {
            panelTransitionCoroutine = StartCoroutine(TransitionPanel(targetX));
        }
        else
        {
            if (shopPanel != null)
            {
                Vector2 pos = shopPanel.anchoredPosition;
                pos.x = targetX;
                shopPanel.anchoredPosition = pos;
            }
        }
    }

    private IEnumerator TransitionPanel(float targetX)
    {
        if (shopPanel == null) yield break;

        Vector2 startPos = shopPanel.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < panelTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panelTransitionDuration);
            float curveT = scrollCurve != null ? scrollCurve.Evaluate(t) : t;
            Vector2 currentPos = shopPanel.anchoredPosition;
            currentPos.x = Mathf.Lerp(startPos.x, targetX, curveT);
            shopPanel.anchoredPosition = currentPos;
            yield return null;
        }

        Vector2 finalPos = shopPanel.anchoredPosition;
        finalPos.x = targetX;
        shopPanel.anchoredPosition = finalPos;
    }

    /// <summary>
    /// Selects the given item, closes the shop, starts placement, and logs the selection/use event.
    /// </summary>
    public void SelectAndUseItem(ShopItemUI item)
    {
        if (item == null) return;
        selectedShopItem = item;

        // Close the shop panel
        SetShopOpen(false, smooth: true);

        // Notify placement manager to start placing the item
        if (placementManager != null && item.ItemData != null)
        {
            placementManager.StartPlacement(item.ItemData);
        }

        Debug.Log($"Selected and used item: {item.itemNameText?.text}");
    }

    /// <summary>
    /// Helper method to retrieve visual background settings for a specific ShopItemType.
    /// </summary>
    public ShopItemVisuals GetVisualsForType(ShopItemType type)
    {
        if (itemTypeVisuals != null)
        {
            foreach (var visuals in itemTypeVisuals)
            {
                if (visuals != null && visuals.itemType == type)
                {
                    return visuals;
                }
            }
        }
        return null;
    }
}
