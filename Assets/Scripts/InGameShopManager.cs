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

/// <summary>
/// Optional art for one quality band. Every field can be left empty — a tier with no entry still
/// gets its label and colour from <see cref="ShopTaxonomy"/>, this is only for swapping in bespoke
/// card art as the tiers climb.
/// </summary>
[System.Serializable]
public class ShopTierVisuals
{
    public ShopItemTier tier = ShopItemTier.Tier1;

    [Header("Card Art")]
    public Sprite cardBackground;
    public Sprite iconBackground;
    public Sprite tierBadge;
}

public class InGameShopManager : MonoBehaviour
{
    public static InGameShopManager Instance { get; private set; }

    // Tutorial Hooks
    public static event System.Action OnShopOpened;
    public static event System.Action OnShopClosed;
    public static event System.Action<ShopItemData> OnShopItemUsed;

    public List<ShopItemUI> GetSpawnedShopItemUIs() => spawnedShopItemUIs;

    private void Awake()
    {
        Instance = this;
    }
    [Header("Dynamic Spawning References")]
    [SerializeField] private GameObject shopItemUIPrefab; // The ShopItemUI prefab to instantiate
    [SerializeField] private GameObject inventoryItemUIPrefab; // The InventoryItemUI prefab to instantiate
    private List<ShopItemUI> spawnedShopItemUIs = new List<ShopItemUI>();
    private List<InventoryItemUI> spawnedInventoryItemUIs = new List<InventoryItemUI>();
    private bool hasSpawnedItems;

    [Header("Selection Status")]
    public ShopItemUI selectedShopItem;

    [Header("Shop Panel Navigation")]
    public RectTransform shopPanel;
    public Button openCloseButton;
    public GameObject openArrow;
    public GameObject closeArrow;
    public float panelTransitionDuration = 0.5f;
    public AnimationCurve scrollCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Juicy Animation References")]

    public RectTransform mainBg;
    public RectTransform insideBg;
    public RectTransform shopItemPanel;
    public RectTransform inventoryItemPanel;
    public TMP_Text shopText;
    public TMP_Text inventoryText;
    public RectTransform shopCategoryTabsParent;
    public RectTransform inventoryCategoryTabsParent;

    [Header("Juicy Animation Curves")]
    public AnimationCurve openCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 4f),
        new Keyframe(0.45f, 1.15f, 0.5f, -0.5f),
        new Keyframe(0.7f, 0.95f, -0.5f, 0.5f),
        new Keyframe(0.88f, 1.02f, 0.2f, -0.2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );
    public AnimationCurve closeCurve = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(0.2f, 1.05f, 0.5f, -0.5f),
        new Keyframe(0.6f, -0.08f, -2f, 2f),
        new Keyframe(0.85f, 0.03f, 0.5f, -0.5f),
        new Keyframe(1f, 0f, 0f, 0f)
    );



    [Header("Category Navigation")]
    public List<CategoryTab> categoryTabs;

    [Tooltip("Category shown when the shop first opens.")]
    public ShopItemCategory defaultCategory = ShopItemCategory.PlantsAndGardens;

    private ShopItemCategory currentCategory;

    // There is no longer an "All" pseudo-category to park on, so the first FilterByCategory call has
    // nothing to compare against — without this the "already selected" guard would swallow it.
    private bool hasSelectedCategory;

    [Header("Category Sizing Options")]
    [SerializeField] private float selectedTabWidth = 150f;
    [SerializeField] private float unselectedTabWidth = 100f;
    [SerializeField] private Color selectedTabColor = Color.white;
    [SerializeField] private Color unselectedTabColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);

    [Tooltip("Tint the active tab with its collection's accent colour (leaf green for Plants & Gardens, "
           + "water blue for Water of Garden, and so on) instead of the flat Selected Tab Color.")]
    [SerializeField] private bool useCategoryAccentColors = true;

    [Header("Tier Visual Overrides")]
    [Tooltip("Optional per-tier card art. Tiers left out of this list still get their badge label and "
           + "colour from ShopTaxonomy.")]
    public List<ShopTierVisuals> tierVisuals = new List<ShopTierVisuals>();

    private Coroutine categoryTransitionCoroutine;
    private Dictionary<CategoryTab, float> defaultWidths = new Dictionary<CategoryTab, float>();
    private RectTransform tabsParentRect;
    private ShopItemCategory lastCategory;

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
                        txt.text = ShopTaxonomy.GetCategoryName(cat);
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

        // Default to showing the Plants & Gardens collection initially
        FilterByCategory(defaultCategory);
    }

    /// <summary>
    /// Instantiates every shop/inventory item card. Deferred out of <see cref="Start"/> and run once,
    /// the first time the shop panel actually opens — scene load shouldn't pay for ~80+ Instantiate
    /// calls (one per <see cref="ShopItemData"/>/<see cref="TreasureBoxRewardItemData"/>) for a panel
    /// the player may never open this session.
    /// </summary>
    private void EnsureItemsSpawned()
    {
        if (hasSpawnedItems) return;
        hasSpawnedItems = true;

        // Dynamic Spawning of Shop Items based on Categories
        if (shopItemDatas != null && shopItemUIPrefab != null)
        {
            int currentXPLevel = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpLevel : 1;

            // Unlocked first, then by tier so each collection reads Common → Premium Plus, then by
            // the authored sort order inside a tier.
            var sortedShopItems = shopItemDatas
                .OrderBy(d => d != null && currentXPLevel >= d.requiredXPLevel ? 0 : 1)
                .ThenBy(d => d != null ? (int)d.itemTier : 0)
                .ThenBy(d => d != null ? d.sortOrder : 0);

            foreach (var data in sortedShopItems)
            {
                if (data == null) continue;

                CategoryTab matchingTab = FindTabForCategory(data.itemCategory);
                if (matchingTab != null)
                {
                    SpawnShopItemUI(data, matchingTab.contentParent);
                }
                else
                {
                    Debug.LogWarning($"[InGameShopManager] No category tab setup found for category: "
                        + $"{ShopTaxonomy.GetCategoryLongName(data.itemCategory)} on item: {data.itemName}. "
                        + "Run Tools/Shop/Rebuild Category Tabs to add the missing tab.");
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

        // Select the default selected item or first spawned item (set selection only)
        if (selectedShopItem != null)
        {
            selectedShopItem = selectedShopItem;
        }
        else if (spawnedShopItemUIs.Count > 0 && spawnedShopItemUIs[0] != null)
        {
            selectedShopItem = spawnedShopItemUIs[0];
        }
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
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ShopOpenClose);
        SetShopOpen(!isOpen, smooth: true);
    }

    /// <summary>
    /// Explicitly sets the shop open/closed state, updates the arrow UI states, and handles scaling.
    /// </summary>
    public void SetShopOpen(bool open, bool smooth)
    {
        if (open)
        {
            EnsureItemsSpawned();
        }

        isOpen = open;

        if (isOpen) OnShopOpened?.Invoke();
        else OnShopClosed?.Invoke();

        // Toggle visibility of the arrows
        if (openArrow != null) openArrow.SetActive(!isOpen);
        if (closeArrow != null) closeArrow.SetActive(isOpen);

        if (panelTransitionCoroutine != null)
        {
            StopCoroutine(panelTransitionCoroutine);
        }

        if (smooth && gameObject.activeInHierarchy)
        {
            panelTransitionCoroutine = StartCoroutine(TransitionPanelJuicy(isOpen));
        }
        else
        {
            // Immediate state setting without transition
            float scaleY = isOpen ? 1f : 0f;
            
            if (shopPanel != null)
            {
                shopPanel.gameObject.SetActive(isOpen);
            }

            Transform targetBg = mainBg != null ? mainBg : shopPanel;
            if (targetBg != null)
            {
                targetBg.localScale = new Vector3(1f, scaleY, 1f);
            }

            Vector3 innerScale = isOpen ? Vector3.one : Vector3.zero;
            if (insideBg != null) insideBg.localScale = innerScale;
            if (shopText != null) shopText.transform.localScale = innerScale;
            if (inventoryText != null) inventoryText.transform.localScale = innerScale;

            if (shopItemPanel != null) shopItemPanel.gameObject.SetActive(isOpen);
            if (inventoryItemPanel != null) inventoryItemPanel.gameObject.SetActive(isOpen);
            if (shopCategoryTabsParent != null) shopCategoryTabsParent.gameObject.SetActive(isOpen);
            if (inventoryCategoryTabsParent != null) inventoryCategoryTabsParent.gameObject.SetActive(isOpen);
        }
    }

    private IEnumerator TransitionPanelJuicy(bool open)
    {
        float duration = panelTransitionDuration;
        float elapsed = 0f;

        Transform targetBg = mainBg != null ? mainBg : shopPanel;

        if (open)
        {
            if (shopPanel != null) shopPanel.gameObject.SetActive(true);
            if (targetBg != null) targetBg.localScale = new Vector3(1f, 0f, 1f);
            if (insideBg != null) insideBg.localScale = Vector3.zero;
            if (shopText != null) shopText.transform.localScale = Vector3.zero;
            if (inventoryText != null) inventoryText.transform.localScale = Vector3.zero;

            // Deactivate item panels while scaling up
            if (shopItemPanel != null) shopItemPanel.gameObject.SetActive(false);
            if (inventoryItemPanel != null) inventoryItemPanel.gameObject.SetActive(false);
            if (shopCategoryTabsParent != null) shopCategoryTabsParent.gameObject.SetActive(false);
            if (inventoryCategoryTabsParent != null) inventoryCategoryTabsParent.gameObject.SetActive(false);
        }
        else
        {
            // Deactivate item panels immediately when closing
            if (shopItemPanel != null) shopItemPanel.gameObject.SetActive(false);
            if (inventoryItemPanel != null) inventoryItemPanel.gameObject.SetActive(false);
            if (shopCategoryTabsParent != null) shopCategoryTabsParent.gameObject.SetActive(false);
            if (inventoryCategoryTabsParent != null) inventoryCategoryTabsParent.gameObject.SetActive(false);
        }

        AnimationCurve curve = open ? openCurve : closeCurve;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scaleY = curve.Evaluate(t);

            if (targetBg != null)
            {
                targetBg.localScale = new Vector3(1f, scaleY, 1f);
            }

            if (open)
            {
                // Scale up insideBg
                float insideT = Mathf.Clamp01((t - 0.15f) / 0.85f);
                float insideScale = openCurve.Evaluate(insideT);

                if (insideBg != null) insideBg.localScale = new Vector3(insideScale, insideScale, 1f);

                float textT = Mathf.Clamp01((t - 0.3f) / 0.7f);
                float textScale = openCurve.Evaluate(textT);

                if (shopText != null) shopText.transform.localScale = new Vector3(textScale, textScale, 1f);
                if (inventoryText != null) inventoryText.transform.localScale = new Vector3(textScale, textScale, 1f);
            }
            else
            {
                // Scale down insideBg
                float shrinkScale = Mathf.Lerp(1f, 0f, t * 1.5f);
                if (insideBg != null) insideBg.localScale = new Vector3(shrinkScale, shrinkScale, 1f);
                if (shopText != null) shopText.transform.localScale = new Vector3(shrinkScale, shrinkScale, 1f);
                if (inventoryText != null) inventoryText.transform.localScale = new Vector3(shrinkScale, shrinkScale, 1f);
            }

            yield return null;
        }

        float finalScaleY = open ? 1f : 0f;
        if (targetBg != null)
        {
            targetBg.localScale = new Vector3(1f, finalScaleY, 1f);
        }

        Vector3 finalInnerScale = open ? Vector3.one : Vector3.zero;
        if (insideBg != null) insideBg.localScale = finalInnerScale;
        if (shopText != null) shopText.transform.localScale = finalInnerScale;
        if (inventoryText != null) inventoryText.transform.localScale = finalInnerScale;

        // Activate item panels at the end of opening
        if (open)
        {
            if (shopItemPanel != null) shopItemPanel.gameObject.SetActive(true);
            if (inventoryItemPanel != null) inventoryItemPanel.gameObject.SetActive(true);
            if (shopCategoryTabsParent != null) shopCategoryTabsParent.gameObject.SetActive(true);
            if (inventoryCategoryTabsParent != null) inventoryCategoryTabsParent.gameObject.SetActive(true);
        }
        else
        {
            if (shopPanel != null)
            {
                shopPanel.gameObject.SetActive(false);
            }
        }

        panelTransitionCoroutine = null;
    }

    /// <summary>
    /// Entry point for every shop item click. Enforces the level gate, then routes to the flow for the
    /// item's <see cref="ShopAcquisitionType"/>: spend Noor Coins, watch a rewarded ad, or pay real money.
    /// Each flow converges on <see cref="CompleteAcquisition"/> once the player has paid.
    /// </summary>
    public void SelectAndUseItem(ShopItemUI item)
    {
        if (item == null) return;

        ShopItemData data = item.ItemData;
        if (data == null) return;

        // ── Placement Gate — one download/placement in flight at a time ────────
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsPlacing)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Finish placing the current item first");
            }
            return;
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemPurchase);

        // ── Level Gate (applies to every acquisition type) ─────────────────────
        if (PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < data.requiredXPLevel)
        {
            Debug.Log($"[InGameShopManager] Cannot purchase '{data.itemName}': requires level {data.requiredXPLevel}.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast($"Requires Level {data.requiredXPLevel}");
            }
            return; // Abort — player doesn't have the required level
        }

        switch (data.acquisitionType)
        {
            case ShopAcquisitionType.RewardedAd:
                AcquireByWatchingAd(item, data);
                break;

            case ShopAcquisitionType.InAppPurchase:
                AcquireByRealMoney(item, data);
                break;

            default:
                AcquireWithNoorCoins(item, data);
                break;
        }
    }

    /// <summary>
    /// Standard flow: deduct <see cref="ShopItemData.noorCoinCost"/> and hand the item over.
    /// </summary>
    private void AcquireWithNoorCoins(ShopItemUI item, ShopItemData data)
    {
        // ── Economy Gate ──────────────────────────────────────────────────────
        if (data.noorCoinCost > 0)
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

        CompleteAcquisition(item, data);
    }

    /// <summary>
    /// Rewarded-ad flow. For a daily offer the cooldown is checked first, and only started once the ad
    /// has actually been watched — a player who bails out of the ad loses nothing.
    /// </summary>
    private void AcquireByWatchingAd(ShopItemUI item, ShopItemData data)
    {
        // ── Daily Offer Gate ──────────────────────────────────────────────────
        if (data.isDailyOffer)
        {
            // DailyOfferManager creates itself on demand, so this is null only while the app is quitting.
            if (DailyOfferManager.Instance == null) return;

            if (!DailyOfferManager.Instance.IsOfferAvailable(data))
            {
                System.TimeSpan remaining = DailyOfferManager.Instance.GetTimeUntilAvailable(data);
                Debug.Log($"[InGameShopManager] '{data.itemName}' is on cooldown for another "
                    + $"{DailyOfferManager.FormatCooldown(remaining)}.");

                if (ToastMessageManager.Instance != null)
                {
                    ToastMessageManager.Instance.ShowToast($"Come back in {DailyOfferManager.FormatCooldown(remaining)}");
                }

                return; // Abort — already claimed this cycle
            }
        }

        if (AdsManager.Instance == null)
        {
            Debug.LogError("[InGameShopManager] AdsManager not found in scene — cannot show a rewarded ad. "
                + "Add an AdsManager GameObject.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Ads are unavailable right now");
            }
            return;
        }

        Debug.Log($"[InGameShopManager] Showing rewarded ad for '{data.itemName}'.");

        // ShowRewardedAd only calls back once the reward is earned (it falls back to the fake ad panel
        // when no real ad is loaded), so reaching this callback means the player has paid with their time.
        AdsManager.Instance.ShowRewardedAd(() =>
        {
            if (item == null || item.ItemData != data) return; // Shop was torn down mid-ad

            Debug.Log($"[InGameShopManager] Rewarded ad watched — granting '{data.itemName}'.");

            if (data.isDailyOffer && DailyOfferManager.Instance != null)
            {
                DailyOfferManager.Instance.MarkOfferClaimed(data);
            }

            CompleteAcquisition(item, data);
        }, "shop_item");
    }

    /// <summary>
    /// Real-money flow. Delegates to <see cref="IAPManager"/>, which currently fakes the transaction —
    /// see that class for the storefront seam.
    /// </summary>
    private void AcquireByRealMoney(ShopItemUI item, ShopItemData data)
    {
        if (IAPManager.Instance == null)
        {
            Debug.LogError("[InGameShopManager] IAPManager not found in scene — cannot start a purchase. "
                + "Add an IAPManager GameObject.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("The store is unavailable right now");
            }
            return;
        }

        if (IAPManager.Instance.IsPurchasePending)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("A purchase is already in progress");
            }
            return;
        }

        item.SetPurchasePending(true);

        IAPManager.Instance.PurchaseProduct(data, success =>
        {
            if (item == null || item.ItemData != data) return; // Shop was torn down mid-purchase

            item.SetPurchasePending(false);

            if (!success)
            {
                if (ToastMessageManager.Instance != null)
                {
                    ToastMessageManager.Instance.ShowToast("Purchase not completed");
                }
                return;
            }

            CompleteAcquisition(item, data);
        });
    }

    /// <summary>
    /// The player has paid (coins, an ad, or real money) — hand over what they bought.
    /// Grants any Noor Coin reward, then prepares placement if the item is something to put in the garden.
    /// Coin packs and coin-paying ad offers have no prefab, so they stop at the reward and leave the shop open.
    /// </summary>
    private void CompleteAcquisition(ShopItemUI item, ShopItemData data)
    {
        selectedShopItem = item;

        // ── Grant coins (coin packs, ad offers) ───────────────────────────────
        if (data.noorCoinReward > 0)
        {
            if (NoorCoinManager.Instance == null)
            {
                Debug.LogError($"[InGameShopManager] '{data.itemName}' grants {data.noorCoinReward} Noor Coins "
                    + "but there is no NoorCoinManager in the scene — the reward is lost.");
            }
            else
            {
                NoorCoinManager.Instance.Earn(data.noorCoinReward);
                Debug.Log($"[InGameShopManager] Granted {data.noorCoinReward} Noor Coins from '{data.itemName}'. "
                    + $"New balance: {NoorCoinManager.Instance.Balance}.");
            }
        }

        OnShopItemUsed?.Invoke(data);

        // ── Grant the item itself ─────────────────────────────────────────────
        if (!data.IsPlaceable)
        {
            // Nothing to place (coin pack / coin-only ad offer). Keep the shop open so the player can
            // spend what they just earned, and refresh the cards so the new balance is reflected.
            RefreshShopItemVisuals();
            Debug.Log($"[InGameShopManager] '{data.itemName}' has no prefab — reward granted, nothing to place.");
            return;
        }

        if (placementManager == null)
        {
            placementManager = ItemPlacementManager.Instance != null
                ? ItemPlacementManager.Instance
                : FindObjectOfType<ItemPlacementManager>();
        }

        if (placementManager == null)
        {
            Debug.LogError("[InGameShopManager] Item selected but ItemPlacementManager not found in scene.");
            return;
        }

        // The item is paid for but its prefab may still need to download. Keep the shop open (with a
        // "Downloading..." indicator on the card) so the player isn't dropped into an empty scene waiting
        // on a network request — only close the shop and hand off to placement mode once the prefab is
        // actually ready to place.
        item.SetDownloadPending(true);

        placementManager.PreparePlacement(data,
            onReady: () =>
            {
                item.SetDownloadPending(false);
                SetShopOpen(false, smooth: true);
            },
            onFailed: () =>
            {
                item.SetDownloadPending(false);
            },
            onProgress: item.SetDownloadProgress);

        Debug.Log($"Selected and used item: {item.itemNameText?.text}");
    }

    /// <summary>Re-runs the price/affordability pass on every spawned card.</summary>
    private void RefreshShopItemVisuals()
    {
        foreach (var itemUI in spawnedShopItemUIs)
        {
            if (itemUI != null) itemUI.RefreshAffordabilityVisual();
        }
    }

    /// <summary>
    /// Selects the given inventory item, checks if it is unlocked, 
    /// closes the shop, prepares placement.
    /// </summary>
    public void SelectAndUseInventoryItem(ShopItemUI item)
    {
        if (item == null) return;

        TreasureBoxRewardItemData data = item.RewardItemData;
        if (data == null) return;

        // ── Placement Gate — one download/placement in flight at a time ────────
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsPlacing)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Finish placing the current item first");
            }
            return;
        }

        selectedShopItem = item;

        if (placementManager == null)
        {
            placementManager = ItemPlacementManager.Instance != null
                ? ItemPlacementManager.Instance
                : FindObjectOfType<ItemPlacementManager>();
        }

        if (placementManager == null)
        {
            Debug.LogError("[InGameShopManager] Item selected but ItemPlacementManager not found in scene.");
            return;
        }

        if (!data.IsPlaceable)
        {
            // Nothing to download — fall back to the old immediate-close behaviour.
            SetShopOpen(false, smooth: true);
            placementManager.PreparePlacement(data);
            Debug.Log($"Selected and used inventory item: {item.itemNameText?.text}");
            return;
        }

        // As in CompleteAcquisition: keep the shop open with a "Downloading..." indicator on the card
        // until the prefab is actually ready, then close the shop and hand off to placement mode.
        item.SetDownloadPending(true);

        placementManager.PreparePlacement(data,
            onReady: () =>
            {
                item.SetDownloadPending(false);
                SetShopOpen(false, smooth: true);
            },
            onFailed: () =>
            {
                item.SetDownloadPending(false);
            },
            onProgress: item.SetDownloadProgress);

        Debug.Log($"Selected and used inventory item: {item.itemNameText?.text}");
    }



    private void UpdateHeaderTextColors(ShopItemCategory category)
    {
        bool isShop = ShopTaxonomy.IsShopCategory(category);

        Color activeColor = Color.white;
        Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        if (shopText != null)
        {
            shopText.color = isShop ? activeColor : inactiveColor;
        }

        if (inventoryText != null)
        {
            inventoryText.color = isShop ? inactiveColor : activeColor;
        }
    }

    /// <summary>
    /// Filters the shop UI items by the specified category and triggers smooth transitions.
    /// </summary>
    public void FilterByCategory(ShopItemCategory category)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.TabSwitch);
        if (hasSelectedCategory && currentCategory == category) return; // Already selected

        lastCategory = hasSelectedCategory ? currentCategory : category;
        currentCategory = category;
        hasSelectedCategory = true;

        UpdateHeaderTextColors(category);

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
        UpdateHeaderTextColors(selectedCategory);
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
                        tab.tabButton.image.color = GetTabColor(tab.category, isActive);
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
                        Color targetColor = GetTabColor(tab.category, isTarget);

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

    /// <summary>
    /// Fill colour for a tab. The active tab picks up its collection's accent colour so the five
    /// shop sections stay distinguishable at a glance; inactive tabs share one muted tint.
    /// </summary>
    private Color GetTabColor(ShopItemCategory category, bool isActive)
    {
        if (!isActive) return unselectedTabColor;
        if (!useCategoryAccentColors) return selectedTabColor;

        // Keep the authored alpha so a translucent tab stays translucent.
        Color accent = ShopTaxonomy.GetCategoryColor(category);
        accent.a = selectedTabColor.a;
        return accent;
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

    /// <summary>The card art configured for a quality band, or null when the tier has no entry.</summary>
    public ShopTierVisuals GetVisualsForTier(ShopItemTier tier)
    {
        if (tierVisuals != null)
        {
            foreach (var visuals in tierVisuals)
            {
                if (visuals != null && visuals.tier == tier)
                {
                    return visuals;
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
            itemUI.Initialize(data, GetVisualsForTier(data.itemTier));

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
            int qty = InventoryManager.Instance != null ? InventoryManager.Instance.GetItemQuantity(data.itemID) : data.quantity;
            itemUI.Initialize(data, qty);
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
                int qty = InventoryManager.Instance != null ? InventoryManager.Instance.GetItemQuantity(data.itemID) : data.quantity;
                itemUI.Initialize(data, qty);
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
