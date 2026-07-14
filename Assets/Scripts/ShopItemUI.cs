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

    [Header("State Visual References")]
    public GameObject[] lockedVisuals;
    public GameObject[] unlockedVisuals;

    [Header("Economy Visuals")]
    [Tooltip("Colour of the price label when the player CAN afford the item.")]
    public Color affordableColor = Color.green; // green
    [Tooltip("Colour of the price label when the player CANNOT afford the item.")]
    public Color unaffordableColor = Color.red; // red

    [Header("Acquisition Labels")]
    [Tooltip("Price label shown on a rewarded-ad item that is ready to claim.")]
    public string watchAdLabel = "Watch Ad";
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
    /// Initializes the UI components with the values from a ShopItemData asset and background overrides.
    /// </summary>
    public void Initialize(ShopItemData data, Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;
        ItemData = data;
        isPurchasePending = false;

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

        // Price label + affordability tint (both derived from the acquisition type)
        RefreshAffordabilityVisual();
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
            return;
        }

        if (ItemData == null) return;

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
                    itemPriceText.text = watchAdLabel;
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
