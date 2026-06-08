using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;



[System.Serializable]
public class CategoryTab
{
    public ShopItemCategory category;
    public Button tabButton;
    public GameObject categoryPanel; // Enabled when active, disabled when inactive
    public Transform contentParent;  // Parent where item prefabs of this category are spawned
}

public class InGameShopManager : MonoBehaviour
{
    [Header("Dynamic Spawning References")]
    [SerializeField] private GameObject shopItemUIPrefab; // The ShopItemUI prefab to instantiate
    private List<ShopItemUI> spawnedShopItemUIs = new List<ShopItemUI>();

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



    [Header("Category Navigation")]
    public List<CategoryTab> categoryTabs;
    private ShopItemCategory currentCategory = ShopItemCategory.All;

    [Header("Category Sizing Options")]
    [SerializeField] private float selectedTabWidth = 150f;
    [SerializeField] private float unselectedTabWidth = 100f;
    private Coroutine scaleTabsCoroutine;
    private Dictionary<CategoryTab, float> defaultWidths = new Dictionary<CategoryTab, float>();
    private RectTransform tabsParentRect;

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

        // Dynamic Spawning of Shop Items based on Categories
        if (shopItemDatas != null && shopItemUIPrefab != null)
        {
            foreach (var data in shopItemDatas)
            {
                if (data == null) continue;

                // Find matching category tab configuration
                CategoryTab matchingTab = FindTabForCategory(data.itemCategory);
                if (matchingTab != null && matchingTab.contentParent != null)
                {
                    GameObject spawnedObj = Instantiate(shopItemUIPrefab, matchingTab.contentParent);
                    ShopItemUI itemUI = spawnedObj.GetComponent<ShopItemUI>();
                    if (itemUI != null)
                    {
                        itemUI.Initialize(data);

                        // Hook up click listener to purchase/select item
                        if (itemUI.purchaseButton != null)
                        {
                            ShopItemUI currentItem = itemUI;
                            itemUI.purchaseButton.onClick.AddListener(() => SelectAndUseItem(currentItem));
                        }

                        spawnedShopItemUIs.Add(itemUI);
                    }
                }
                else
                {
                    Debug.LogWarning($"[InGameShopManager] No category tab setup or content parent found for category: {data.itemCategory} on item: {data.itemName}");
                }
            }
        }

        // Select the default selected item or first spawned item on start (set selection only)
        if (selectedShopItem != null)
        {
            selectedShopItem = selectedShopItem;
        }
        else if (spawnedShopItemUIs.Count > 0 && spawnedShopItemUIs[0] != null)
        {
            selectedShopItem = spawnedShopItemUIs[0];
        }

        // Initialize default arrow state and panel position (Closed by default)
        SetShopOpen(false, smooth: false);

        // Initialize Category Tabs
        if (categoryTabs != null)
        {
            if (categoryTabs.Count > 0 && categoryTabs[0] != null && categoryTabs[0].tabButton != null)
            {
                tabsParentRect = categoryTabs[0].tabButton.transform.parent as RectTransform;
            }

            foreach (var tab in categoryTabs)
            {
                if (tab != null && tab.tabButton != null)
                {
                    ShopItemCategory cat = tab.category;
                    tab.tabButton.onClick.AddListener(() => FilterByCategory(cat));

                    RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        defaultWidths[tab] = rect.sizeDelta.x;
                    }
                    else
                    {
                        defaultWidths[tab] = unselectedTabWidth;
                    }
                }
            }
        }

        // Default to showing all categories initially
        FilterByCategory(ShopItemCategory.All);
    }

    private void OnDisable()
    {
        if (scaleTabsCoroutine != null)
        {
            StopCoroutine(scaleTabsCoroutine);
            scaleTabsCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        if (openCloseButton != null)
        {
            openCloseButton.onClick.RemoveListener(ToggleShop);
        }

        if (categoryTabs != null)
        {
            foreach (var tab in categoryTabs)
            {
                if (tab != null && tab.tabButton != null)
                {
                    tab.tabButton.onClick.RemoveAllListeners();
                }
            }
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
    /// Selects the given item, checks if the player can afford it, deducts Noor Coins,
    /// closes the shop, prepares placement, and logs the event.
    /// </summary>
    public void SelectAndUseItem(ShopItemUI item)
    {
        if (item == null) return;

        ShopItemData data = item.ItemData;

        // ── Economy Gate ──────────────────────────────────────────────────────
        if (data != null && data.noorCoinCost > 0)
        {
            if (NoorCoinManager.Instance == null)
            {
                Debug.LogError("[InGameShopManager] NoorCoinManager not found in scene. "
                    + "Add a NoorCoinManager GameObject.");
                return;
            }

            if (!NoorCoinManager.Instance.TrySpend(data.noorCoinCost))
            {
                Debug.Log($"[InGameShopManager] Cannot purchase '{data.itemName}': "
                    + $"insufficient Noor Coins (need {data.noorCoinCost}, "
                    + $"have {NoorCoinManager.Instance.Balance}).");
                return; // Abort — player can't afford it
            }

            Debug.Log($"[InGameShopManager] Purchased '{data.itemName}' for "
                + $"{data.noorCoinCost} Noor Coins.");
        }
        // ─────────────────────────────────────────────────────────────────────

        selectedShopItem = item;

        // Close the shop panel
        SetShopOpen(false, smooth: true);

        // Notify placement manager to prepare placing the item
        if (placementManager != null && data != null)
        {
            placementManager.PreparePlacement(data);
        }

        Debug.Log($"Selected and used item: {item.itemNameText?.text}");
    }



    /// <summary>
    /// Filters the shop UI items by the specified category and updates the category panel visibility.
    /// </summary>
    public void FilterByCategory(ShopItemCategory category)
    {
        currentCategory = category;

        // Toggle active state of category panels
        if (categoryTabs != null)
        {
            foreach (var tab in categoryTabs)
            {
                if (tab != null && tab.categoryPanel != null)
                {
                    tab.categoryPanel.SetActive(tab.category == category);
                }
            }
        }

        // Animate/lerp the scale of the tab buttons
        UpdateTabVisuals(category);
    }

    private void UpdateTabVisuals(ShopItemCategory selectedCategory)
    {
        if (scaleTabsCoroutine != null)
        {
            StopCoroutine(scaleTabsCoroutine);
        }
        
        if (gameObject.activeInHierarchy)
        {
            scaleTabsCoroutine = StartCoroutine(WidthTabsRoutine(selectedCategory));
        }
        else
        {
            // Immediate update if manager is not active/playing
            if (categoryTabs != null)
            {
                foreach (var tab in categoryTabs)
                {
                    if (tab != null && tab.tabButton != null)
                    {
                        RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            float defaultWidth = defaultWidths.ContainsKey(tab) ? defaultWidths[tab] : unselectedTabWidth;
                            float targetWidth = (tab.category == selectedCategory) ? selectedTabWidth : defaultWidth;
                            rect.sizeDelta = new Vector2(targetWidth, rect.sizeDelta.y);
                        }
                    }
                }
                if (tabsParentRect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(tabsParentRect);
                }
            }
        }
    }

    private IEnumerator WidthTabsRoutine(ShopItemCategory selectedCategory)
    {
        float duration = 0.15f; // Snappy, juicy animation
        float elapsed = 0f;

        // Store starting width for each tab button
        Dictionary<CategoryTab, float> startWidths = new Dictionary<CategoryTab, float>();
        if (categoryTabs != null)
        {
            foreach (var tab in categoryTabs)
            {
                if (tab != null && tab.tabButton != null)
                {
                    RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        startWidths[tab] = rect.sizeDelta.x;
                    }
                }
            }
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (categoryTabs != null)
            {
                foreach (var tab in categoryTabs)
                {
                    if (tab != null && tab.tabButton != null && startWidths.ContainsKey(tab))
                    {
                        RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            float defaultWidth = defaultWidths.ContainsKey(tab) ? defaultWidths[tab] : unselectedTabWidth;
                            float targetWidth = (tab.category == selectedCategory) ? selectedTabWidth : defaultWidth;
                            float currentWidth = Mathf.Lerp(startWidths[tab], targetWidth, t);
                            rect.sizeDelta = new Vector2(currentWidth, rect.sizeDelta.y);
                        }
                    }
                }
            }

            // Force layout group to rebuild immediately during interpolation
            if (tabsParentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tabsParentRect);
            }

            yield return null;
        }

        // Ensure final state is applied
        if (categoryTabs != null)
        {
            foreach (var tab in categoryTabs)
            {
                if (tab != null && tab.tabButton != null)
                {
                    RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        float defaultWidth = defaultWidths.ContainsKey(tab) ? defaultWidths[tab] : unselectedTabWidth;
                        float targetWidth = (tab.category == selectedCategory) ? selectedTabWidth : defaultWidth;
                        rect.sizeDelta = new Vector2(targetWidth, rect.sizeDelta.y);
                    }
                }
            }
            if (tabsParentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tabsParentRect);
            }
        }
        scaleTabsCoroutine = null;
    }

    /// <summary>
    /// Overload helper to filter categories by integer index (useful for Inspector UnityEvents).
    /// </summary>
    public void FilterByCategoryInt(int categoryIndex)
    {
        FilterByCategory((ShopItemCategory)categoryIndex);
    }

    /// <summary>
    /// Overload helper to filter categories by string name (useful for Inspector UnityEvents).
    /// </summary>
    public void FilterByCategoryString(string categoryName)
    {
        if (System.Enum.TryParse(categoryName, true, out ShopItemCategory result))
        {
            FilterByCategory(result);
        }
        else
        {
            Debug.LogWarning($"[InGameShopManager] Unknown category name: {categoryName}");
        }
    }

    private CategoryTab FindTabForCategory(ShopItemCategory category)
    {
        if (categoryTabs != null)
        {
            foreach (var tab in categoryTabs)
            {
                if (tab != null && tab.category == category)
                {
                    return tab;
                }
            }
        }
        return null;
    }
}
