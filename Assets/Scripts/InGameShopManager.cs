using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;



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
    public static InGameShopManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
    [Header("Dynamic Spawning References")]
    [SerializeField] private GameObject shopItemUIPrefab; // The ShopItemUI prefab to instantiate
    [SerializeField] private GameObject inventoryItemUIPrefab; // The InventoryItemUI prefab to instantiate
    private List<ShopItemUI> spawnedShopItemUIs = new List<ShopItemUI>();
    private List<InventoryItemUI> spawnedInventoryItemUIs = new List<InventoryItemUI>();

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
    [SerializeField] private Color selectedTabColor = Color.white;
    [SerializeField] private Color unselectedTabColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);

    private Coroutine categoryTransitionCoroutine;
    private Dictionary<CategoryTab, float> defaultWidths = new Dictionary<CategoryTab, float>();
    private RectTransform tabsParentRect;
    private ShopItemCategory lastCategory = ShopItemCategory.Plants;

    private float closedPositionX;
    private bool isOpen = false;
    private Coroutine panelTransitionCoroutine;

    [Header("Shop Item Data Source")]
    public ShopItemData[] shopItemDatas; // Data assets for each shop item

    [Header("Inventory Item Data Source")]
    public TreasureBoxRewardItemData[] inventoryItemDatas;

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
            // Find the "All" category tab configuration once
            CategoryTab allTab = FindTabForCategory(ShopItemCategory.All);
            int currentXPLevel = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpLevel : 1;

            var sortedShopItems = shopItemDatas.OrderBy(d => d != null && currentXPLevel >= d.requiredXPLevel ? 0 : 1);

            foreach (var data in sortedShopItems)
            {
                if (data == null) continue;

                // 1. Spawn under its specific category
                CategoryTab matchingTab = FindTabForCategory(data.itemCategory);
                if (matchingTab != null)
                {
                    SpawnShopItemUI(data, matchingTab.contentParent);
                }
                else
                {
                    Debug.LogWarning($"[InGameShopManager] No category tab setup found for category: {data.itemCategory} on item: {data.itemName}");
                }

                // 2. Also spawn under the "All" category panel (if configured)
                if (allTab != null && allTab.contentParent != null)
                {
                    SpawnShopItemUI(data, allTab.contentParent);
                }
            }
        }

        // Dynamic Spawning of Inventory Items based on Categories
        if (inventoryItemDatas != null && inventoryItemUIPrefab != null)
        {
            int currentXPLevel = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpLevel : 1;
            var sortedInventoryItems = inventoryItemDatas.OrderBy(d => d != null && currentXPLevel >= d.unlockXPLevel ? 0 : 1);

            foreach (var data in sortedInventoryItems)
            {
                if (data == null) continue;

                CategoryTab matchingTab = FindTabForCategory(data.itemCategory);
                if (matchingTab != null)
                {
                    SpawnInventoryItemUI(data, matchingTab.contentParent);
                }
                else
                {
                    Debug.LogWarning($"[InGameShopManager] No category tab setup found for category: {data.itemCategory} on inventory item: {data.itemName}");
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

                    // Dynamically set button text label from category name
                    TMP_Text txt = tab.tabButton.GetComponentInChildren<TMP_Text>();
                    if (txt != null)
                    {
                        txt.text = cat.ToString();
                    }

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

        // Default to showing Plants category initially
        FilterByCategory(ShopItemCategory.Plants);
    }

    private void OnDisable()
    {
        if (categoryTransitionCoroutine != null)
        {
            StopCoroutine(categoryTransitionCoroutine);
            categoryTransitionCoroutine = null;
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

        // ── Level Gate ────────────────────────────────────────────────────────
        if (data != null && PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < data.requiredXPLevel)
        {
            Debug.Log($"[InGameShopManager] Cannot purchase '{data.itemName}': requires level {data.requiredXPLevel}.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast($"Requires Level {data.requiredXPLevel}");
            }
            return; // Abort — player doesn't have the required level
        }

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

                // Show toast message to the player indicating insufficient funds
                if (ToastMessageManager.Instance != null)
                {
                    ToastMessageManager.Instance.ShowToast("Not enough Noor Coins");
                }

                return; // Abort — player can't afford it
            }

            Debug.Log($"[InGameShopManager] Purchased '{data.itemName}' for "
                + $"{data.noorCoinCost} Noor Coins.");
        }
        // ─────────────────────────────────────────────────────────────────────

        selectedShopItem = item;

        // Close the shop panel
        SetShopOpen(false, smooth: true);

        // Notify placement manager to prepare placing the item (spawn preview & show Place button)
        if (data != null)
        {
            if (placementManager == null)
            {
                placementManager = ItemPlacementManager.Instance != null
                    ? ItemPlacementManager.Instance
                    : FindObjectOfType<ItemPlacementManager>();
            }

            if (placementManager != null)
            {
                placementManager.PreparePlacement(data);
            }
            else
            {
                Debug.LogError("[InGameShopManager] Item selected but ItemPlacementManager not found in scene.");
            }
        }

        Debug.Log($"Selected and used item: {item.itemNameText?.text}");
    }

    /// <summary>
    /// Selects the given inventory item, checks if it is unlocked, 
    /// closes the shop, prepares placement.
    /// </summary>
    public void SelectAndUseInventoryItem(ShopItemUI item)
    {
        if (item == null) return;

        TreasureBoxRewardItemData data = item.RewardItemData;

        selectedShopItem = item;

        // Close the shop panel
        SetShopOpen(false, smooth: true);

        // Notify placement manager to prepare placing the item (spawn preview & show Place button)
        if (data != null)
        {
            if (placementManager == null)
            {
                placementManager = ItemPlacementManager.Instance != null
                    ? ItemPlacementManager.Instance
                    : FindObjectOfType<ItemPlacementManager>();
            }

            if (placementManager != null)
            {
                placementManager.PreparePlacement(data);
            }
            else
            {
                Debug.LogError("[InGameShopManager] Item selected but ItemPlacementManager not found in scene.");
            }
        }

        Debug.Log($"Selected and used inventory item: {item.itemNameText?.text}");
    }



    /// <summary>
    /// Filters the shop UI items by the specified category and triggers smooth transitions.
    /// </summary>
    public void FilterByCategory(ShopItemCategory category)
    {
        if (currentCategory == category) return; // Already selected

        lastCategory = currentCategory;
        currentCategory = category;

        if (categoryTransitionCoroutine != null)
        {
            StopCoroutine(categoryTransitionCoroutine);
        }

        if (gameObject.activeInHierarchy && Application.isPlaying)
        {
            categoryTransitionCoroutine = StartCoroutine(CategoryTransitionRoutine(lastCategory, currentCategory));
        }
        else
        {
            ApplyImmediateState(category);
        }
    }

    private void ApplyImmediateState(ShopItemCategory selectedCategory)
    {
        if (categoryTabs != null)
        {
            foreach (var tab in categoryTabs)
            {
                if (tab == null) continue;

                bool isActive = (tab.category == selectedCategory);

                // Set panel active state and opacity
                if (tab.categoryPanel != null)
                {
                    tab.categoryPanel.SetActive(isActive);
                    CanvasGroup cg = GetOrAddCanvasGroup(tab.categoryPanel);
                    if (cg != null)
                    {
                        cg.alpha = isActive ? 1f : 0f;
                    }
                    tab.categoryPanel.transform.localScale = Vector3.one;
                }

                // Set button size and color
                if (tab.tabButton != null)
                {
                    RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        float defaultWidth = defaultWidths.ContainsKey(tab) ? defaultWidths[tab] : unselectedTabWidth;
                        rect.sizeDelta = new Vector2(isActive ? selectedTabWidth : defaultWidth, rect.sizeDelta.y);
                    }
                    if (tab.tabButton.image != null)
                    {
                        tab.tabButton.image.color = isActive ? selectedTabColor : unselectedTabColor;
                    }
                }
            }
            if (tabsParentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tabsParentRect);
            }
        }
    }

    private IEnumerator CategoryTransitionRoutine(ShopItemCategory oldCategory, ShopItemCategory newCategory)
    {
        float duration = 0.25f;
        float elapsed = 0f;

        CategoryTab oldTab = FindTabForCategory(oldCategory);
        CategoryTab newTab = FindTabForCategory(newCategory);

        // Prep starting states for buttons
        Dictionary<CategoryTab, float> startWidths = new Dictionary<CategoryTab, float>();
        Dictionary<CategoryTab, Color> startColors = new Dictionary<CategoryTab, Color>();
        
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
                    if (tab.tabButton.image != null)
                    {
                        startColors[tab] = tab.tabButton.image.color;
                    }
                }
            }
        }

        // Prep panels
        CanvasGroup oldPanelCG = oldTab != null ? GetOrAddCanvasGroup(oldTab.categoryPanel) : null;
        CanvasGroup newPanelCG = newTab != null ? GetOrAddCanvasGroup(newTab.categoryPanel) : null;

        if (newTab != null && newTab.categoryPanel != null)
        {
            newTab.categoryPanel.SetActive(true);
            newTab.categoryPanel.transform.localScale = new Vector3(0.9f, 0.9f, 1f); // Start slightly scaled down
            if (newPanelCG != null) newPanelCG.alpha = 0f;
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 1. Interpolate buttons (Sizing and Color)
            if (categoryTabs != null)
            {
                foreach (var tab in categoryTabs)
                {
                    if (tab != null && tab.tabButton != null)
                    {
                        bool isTarget = (tab.category == newCategory);
                        float defaultWidth = defaultWidths.ContainsKey(tab) ? defaultWidths[tab] : unselectedTabWidth;
                        float targetWidth = isTarget ? selectedTabWidth : defaultWidth;
                        Color targetColor = isTarget ? selectedTabColor : unselectedTabColor;

                        // Lerp width
                        if (startWidths.ContainsKey(tab))
                        {
                            RectTransform rect = tab.tabButton.GetComponent<RectTransform>();
                            if (rect != null)
                            {
                                rect.sizeDelta = new Vector2(Mathf.Lerp(startWidths[tab], targetWidth, t), rect.sizeDelta.y);
                            }
                        }

                        // Lerp color
                        if (tab.tabButton.image != null && startColors.ContainsKey(tab))
                        {
                            tab.tabButton.image.color = Color.Lerp(startColors[tab], targetColor, t);
                        }
                    }
                }
            }

            // Force layout rebuild immediately
            if (tabsParentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tabsParentRect);
            }

            // 2. Interpolate Panels (Fade-out old, Fade-in & Pop new)
            float easeOut = Mathf.Sin(t * Mathf.PI * 0.5f); // Smooth ease-out
            float popScale = Mathf.Lerp(0.9f, 1.02f, easeOut); // Overshoot for juicy pop
            if (t > 0.8f)
            {
                float settleT = (t - 0.8f) / 0.2f;
                popScale = Mathf.Lerp(1.02f, 1.0f, settleT); // Settle back to 1.0
            }

            if (oldPanelCG != null)
            {
                oldPanelCG.alpha = Mathf.Lerp(1f, 0f, t);
                if (oldTab != null && oldTab.categoryPanel != null)
                {
                    oldTab.categoryPanel.transform.localScale = new Vector3(Mathf.Lerp(1f, 0.95f, t), Mathf.Lerp(1f, 0.95f, t), 1f);
                }
            }

            if (newTab != null && newTab.categoryPanel != null)
            {
                newTab.categoryPanel.transform.localScale = new Vector3(popScale, popScale, 1f);
                if (newPanelCG != null)
                {
                    newPanelCG.alpha = Mathf.Lerp(0f, 1f, t);
                }
            }

            yield return null;
        }

        // Ensure final states are set
        ApplyImmediateState(newCategory);

        // Turn off the old panel fully
        if (oldTab != null && oldTab.categoryPanel != null && oldTab.category != newCategory)
        {
            oldTab.categoryPanel.SetActive(false);
        }

        categoryTransitionCoroutine = null;
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

    private void SpawnShopItemUI(ShopItemData data, Transform parent)
    {
        if (parent == null || shopItemUIPrefab == null) return;

        GameObject spawnedObj = Instantiate(shopItemUIPrefab, parent);
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

    private void SpawnInventoryItemUI(TreasureBoxRewardItemData data, Transform parent)
    {
        if (parent == null || inventoryItemUIPrefab == null) return;

        GameObject spawnedObj = Instantiate(inventoryItemUIPrefab, parent);
        InventoryItemUI itemUI = spawnedObj.GetComponent<InventoryItemUI>();
        if (itemUI != null)
        {
            itemUI.Initialize(data, data.quantity);
            spawnedInventoryItemUIs.Add(itemUI);
            // Note: Select button / placing logic is excluded as requested
        }
    }

    public void UpdateInventoryUI(TreasureBoxRewardItemData data)
    {
        if (data == null) return;
        foreach (var itemUI in spawnedInventoryItemUIs)
        {
            if (itemUI != null && itemUI.RewardItemData == data)
            {
                itemUI.Initialize(data, data.quantity);
            }
        }
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = go.AddComponent<CanvasGroup>();
        }
        return cg;
    }
}
