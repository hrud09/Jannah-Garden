using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopItemUI : MonoBehaviour
{
    [Header("UI Component References")]
    public Image itemIcon;
    public TMP_Text itemNameText;
    public TMP_Text itemDescriptionText;
    public TMP_Text itemPriceText;
    public Image itemBackgroundImg;
    public Image itemIconBackgroundImg;
    public Button purchaseButton;
    [Tooltip("Label inside the purchase button. Reads 'Select' for normal items and 'Watch Ad' for rewarded-ad packs.")]
    public TMP_Text purchaseButtonLabel;

    [Header("State Visual References")]
    public GameObject[] lockedVisuals;
    public GameObject[] unlockedVisuals;

    [Header("Category & Tier Visuals")]
    [Tooltip("Reads the collection this item belongs to, e.g. 'Water of Garden'.")]
    public TMP_Text itemCategoryText;
    [Tooltip("Strip or icon tinted with the collection's accent colour.")]
    public Image itemCategoryAccentImg;
    [Tooltip("Reads the quality band, e.g. 'Tier 3 · Premium'.")]
    public TMP_Text itemTierText;
    [Tooltip("Badge behind the tier label. Tinted with the tier colour, and given the tier's sprite "
           + "when the shop supplies one.")]
    public Image itemTierBadgeImg;
    [Tooltip("Optional frame around the whole card, tinted with the tier colour.")]
    public Image itemTierFrameImg;
    [Tooltip("Optional per-tier decoration roots. Index 0 is Tier 1 — only the entry matching this "
           + "item's tier is left active. Leave empty if the card has no per-tier objects.")]
    public GameObject[] tierRoots;
    [Tooltip("Tint the tier badge, frame and per-tier roots with the tier colour from ShopTaxonomy. "
           + "Turn off when the sprites already carry their own colour.")]
    public bool tintTierVisuals = true;

    [Header("Economy Visuals")]
    [Tooltip("Colour of the price label when the player CAN afford the item.")]
    public Color affordableColor = Color.green; // green
    [Tooltip("Colour of the price label when the player CANNOT afford the item.")]
    public Color unaffordableColor = Color.red; // red

    [Header("Acquisition Labels")]
    [Tooltip("Button label shown on a rewarded-ad item that is ready to claim.")]
    public string watchAdLabel = "Watch Ad";
    [Tooltip("Button label shown on every non-ad item.")]
    public string selectLabel = "Select";
    [Tooltip("Price label shown while a real-money purchase is in flight.")]
    public string purchasePendingLabel = "Purchasing...";


    public ShopItemData ItemData { get; private set; }
    public TreasureBoxRewardItemData RewardItemData { get; private set; }

    private CanvasGroup canvasGroup;

    // True while IAPManager has a real-money purchase in flight for this card.
    private bool isPurchasePending;

    // Throttles the daily-offer countdown to one refresh per second.
    private float countdownTimer;

    /// <summary>
    /// Gets the CanvasGroup on this GameObject, adding one dynamically if missing.
    /// </summary>
    public CanvasGroup CanvasGroup
    {
        get
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            return canvasGroup;
        }
    }

    /// <summary>
    /// Initializes the UI components with the values from a ShopItemData asset, the tier styling for
    /// its quality band, and background overrides.
    /// </summary>
    public void Initialize(ShopItemData data, ShopTierVisuals tierVisuals = null,
                           Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;
        ItemData = data;
        isPurchasePending = false;

        // A tier entry can carry its own card art, which wins over the plain overrides.
        if (tierVisuals != null)
        {
            if (tierVisuals.cardBackground != null) customBackground = tierVisuals.cardBackground;
            if (tierVisuals.iconBackground != null) customIconBackground = tierVisuals.iconBackground;
        }

        if (itemIcon != null && data.itemIcon != null)
        {
            itemIcon.sprite = data.itemIcon;
        }

        if (itemNameText != null && !string.IsNullOrEmpty(data.itemName))
        {
            itemNameText.text = data.itemName;
        }

        if (itemDescriptionText != null && !string.IsNullOrEmpty(data.itemDescription))
        {
            itemDescriptionText.text = data.itemDescription;
        }

        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < data.requiredXPLevel;

        if (itemBackgroundImg != null && customBackground != null)
        {
            itemBackgroundImg.sprite = customBackground;
        }

        if (itemIconBackgroundImg != null && customIconBackground != null)
        {
            itemIconBackgroundImg.sprite = customIconBackground;
        }

        // Keep locked visuals active when locked, inactive otherwise
        bool isLocked = isLevelLocked;
        if (lockedVisuals != null)
        {
            foreach (var go in lockedVisuals)
            {
                if (go != null) go.SetActive(isLocked);
            }
        }
        // Keep unlocked visuals active when unlocked, inactive otherwise
        bool isUnlocked = !isLocked;
        if (unlockedVisuals != null)
        {
            foreach (var go in unlockedVisuals)
            {
                if (go != null) go.SetActive(isUnlocked);
            }
        }

        ApplyTaxonomyVisuals(data.itemCategory, data.itemTier, tierVisuals);

        // Price label + affordability tint (both derived from the acquisition type)
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Paints the collection and tier badges. Every reference here is optional, so a card prefab can
    /// opt into as much or as little of this as its layout has room for. Items with no tier (coin
    /// packs, ad offers) hide the tier widgets entirely rather than showing an empty badge.
    /// </summary>
    private void ApplyTaxonomyVisuals(ShopItemCategory category, ShopItemTier tier, ShopTierVisuals tierVisuals)
    {
        // ── Category ──────────────────────────────────────────────────────
        Color categoryColor = ShopTaxonomy.GetCategoryColor(category);

        if (itemCategoryText != null)
        {
            itemCategoryText.text = ShopTaxonomy.GetCategoryName(category);
            itemCategoryText.color = categoryColor;
        }

        if (itemCategoryAccentImg != null)
        {
            // Preserve the authored alpha so a soft accent strip stays soft.
            categoryColor.a = itemCategoryAccentImg.color.a;
            itemCategoryAccentImg.color = categoryColor;
        }

        // ── Tier ──────────────────────────────────────────────────────────
        bool hasTier = tier != ShopItemTier.None;
        Color tierColor = ShopTaxonomy.GetTierColor(tier);

        if (itemTierText != null)
        {
            itemTierText.gameObject.SetActive(hasTier);
            if (hasTier)
            {
                itemTierText.text = ShopTaxonomy.GetTierLabel(tier);
                if (tintTierVisuals) itemTierText.color = tierColor;
            }
        }

        if (itemTierBadgeImg != null)
        {
            itemTierBadgeImg.gameObject.SetActive(hasTier);
            if (hasTier)
            {
                if (tierVisuals != null && tierVisuals.tierBadge != null)
                {
                    itemTierBadgeImg.sprite = tierVisuals.tierBadge;
                }
                if (tintTierVisuals)
                {
                    tierColor.a = itemTierBadgeImg.color.a;
                    itemTierBadgeImg.color = tierColor;
                }
            }
        }

        if (itemTierFrameImg != null && hasTier && tintTierVisuals)
        {
            Color frameColor = ShopTaxonomy.GetTierColor(tier);
            frameColor.a = itemTierFrameImg.color.a;
            itemTierFrameImg.color = frameColor;
        }

        // Only the root matching this item's tier stays on; Tier 1 is index 0.
        if (tierRoots != null && tierRoots.Length > 0)
        {
            int activeIndex = hasTier ? (int)tier - 1 : -1;
            for (int i = 0; i < tierRoots.Length; i++)
            {
                if (tierRoots[i] != null) tierRoots[i].SetActive(i == activeIndex);
            }
        }
    }

    // Affordability Visuals

    private void OnEnable()
    {
        NoorCoinManager.OnBalanceChanged += OnBalanceChanged;
        DailyOfferManager.OnOffersChanged += OnOffersChanged;
        RefreshAffordabilityVisual();
    }

    private void OnDisable()
    {
        NoorCoinManager.OnBalanceChanged -= OnBalanceChanged;
        DailyOfferManager.OnOffersChanged -= OnOffersChanged;
    }

    private void OnBalanceChanged(int _) => RefreshAffordabilityVisual();

    private void OnOffersChanged() => RefreshAffordabilityVisual();

    private void Update()
    {
        // A daily offer on cooldown shows a live countdown, so it has to retick itself. Everything else
        // is event-driven and needs no per-frame work.
        if (!IsDailyOfferOnCooldown()) return;

        countdownTimer -= Time.unscaledDeltaTime;
        if (countdownTimer > 0f) return;

        countdownTimer = 1f;
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Called by <see cref="InGameShopManager"/> while a real-money purchase is in flight, so the card
    /// cannot be clicked twice.
    /// </summary>
    public void SetPurchasePending(bool pending)
    {
        isPurchasePending = pending;
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Rebuilds the price label and its tint from the item's acquisition type, and enables or disables
    /// the purchase button. Gold/green when the item can be acquired, red when it cannot.
    /// </summary>
    public void RefreshAffordabilityVisual()
    {
        if (itemPriceText == null) return;

        if (RewardItemData != null)
        {
            itemPriceText.color = affordableColor;
            SetButtonLabel(selectLabel);
            return;
        }

        if (ItemData == null) return;

        // The button says how the item is acquired; the price label says what it costs or pays out.
        SetButtonLabel(ItemData.acquisitionType == ShopAcquisitionType.RewardedAd ? watchAdLabel : selectLabel);

        // A pending purchase blocks the card regardless of anything else.
        if (isPurchasePending)
        {
            itemPriceText.text = purchasePendingLabel;
            itemPriceText.color = unaffordableColor;
            SetButtonInteractable(false);
            return;
        }

        SetButtonInteractable(true);

        // Locked items advertise the level they need, and don't need affordability tinting.
        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < ItemData.requiredXPLevel;
        if (isLevelLocked)
        {
            itemPriceText.text = $"Lvl {ItemData.requiredXPLevel} Req";
            return;
        }

        switch (ItemData.acquisitionType)
        {
            case ShopAcquisitionType.InAppPurchase:
            {
                // Real money: always "affordable" — the storefront, not the wallet, decides.
                itemPriceText.text = string.IsNullOrEmpty(ItemData.realMoneyPriceLabel)
                    ? "Buy"
                    : ItemData.realMoneyPriceLabel;
                itemPriceText.color = affordableColor;
                break;
            }

            case ShopAcquisitionType.RewardedAd:
            {
                // A daily offer that has been claimed counts down to its next refresh; anything else is
                // one ad away.
                if (IsDailyOfferOnCooldown())
                {
                    System.TimeSpan remaining = DailyOfferManager.Instance.GetTimeUntilAvailable(ItemData);
                    itemPriceText.text = DailyOfferManager.FormatCooldown(remaining);
                    itemPriceText.color = unaffordableColor;
                    SetButtonInteractable(false);
                }
                else
                {
                    // The price slot advertises the payout instead of a cost — the button already says
                    // it costs an ad. A prefab-granting offer has no coin payout, so it falls back to
                    // the ad label.
                    itemPriceText.text = ItemData.noorCoinReward > 0
                        ? $"+{ItemData.noorCoinReward} ⧟" // coin glyph
                        : watchAdLabel;
                    itemPriceText.color = affordableColor;
                }
                break;
            }

            default:
            {
                itemPriceText.text = ItemData.noorCoinCost == 0
                    ? "Free"
                    : $"{ItemData.noorCoinCost} ⧟"; // coin glyph

                // Free items are always "affordable"
                if (ItemData.noorCoinCost <= 0)
                {
                    itemPriceText.color = affordableColor;
                    break;
                }

                bool canAfford = NoorCoinManager.Instance != null &&
                                 NoorCoinManager.Instance.CanAfford(ItemData.noorCoinCost);

                itemPriceText.color = canAfford ? affordableColor : unaffordableColor;
                break;
            }
        }
    }

    /// <summary>True only for a daily-offer ad item whose cooldown has not yet elapsed.</summary>
    private bool IsDailyOfferOnCooldown()
    {
        return ItemData != null
            && ItemData.acquisitionType == ShopAcquisitionType.RewardedAd
            && ItemData.isDailyOffer
            && DailyOfferManager.Instance != null
            && !DailyOfferManager.Instance.IsOfferAvailable(ItemData);
    }

    private void SetButtonInteractable(bool interactable)
    {
        if (purchaseButton != null) purchaseButton.interactable = interactable;
    }

    private void SetButtonLabel(string label)
    {
        if (purchaseButtonLabel != null) purchaseButtonLabel.text = label;
    }

    /// <summary>
    /// Initializes the UI components with the values from a TreasureBoxRewardItemData asset and background overrides.
    /// </summary>
    public void Initialize(TreasureBoxRewardItemData data, Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;
        RewardItemData = data;

        if (itemIcon != null && data.itemIcon != null)
        {
            itemIcon.sprite = data.itemIcon;
        }

        if (itemNameText != null && !string.IsNullOrEmpty(data.itemName))
        {
            itemNameText.text = data.itemName;
        }

        if (itemDescriptionText != null && !string.IsNullOrEmpty(data.itemDescription))
        {
            itemDescriptionText.text = data.itemDescription;
        }

        if (itemPriceText != null)
        {
            itemPriceText.text = "Owned"; // Since there is no isUnlocked, assume they own what is shown in inventory
        }

        if (itemBackgroundImg != null && customBackground != null)
        {
            itemBackgroundImg.sprite = customBackground;
        }

        if (itemIconBackgroundImg != null && customIconBackground != null)
        {
            itemIconBackgroundImg.sprite = customIconBackground;
        }

        // Treasure box rewards are ranked by their own ItemRarity, not by a shop tier, so the tier
        // widgets stay hidden here.
        ApplyTaxonomyVisuals(data.itemCategory, ShopItemTier.None, null);

        bool isLocked = false;
        if (lockedVisuals != null)
        {
            foreach (var go in lockedVisuals)
            {
                if (go != null) go.SetActive(isLocked);
            }
        }

        bool isUnlocked = !isLocked;
        if (unlockedVisuals != null)
        {
            foreach (var go in unlockedVisuals)
            {
                if (go != null) go.SetActive(isUnlocked);
            }
        }

        RefreshAffordabilityVisual();
    }
}
