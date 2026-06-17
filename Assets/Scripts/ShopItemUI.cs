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


    public ShopItemData ItemData { get; private set; }
    public TreasureBoxRewardItemData RewardItemData { get; private set; }

    private CanvasGroup canvasGroup;

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

        // ── Economy: display Noor Coin cost ──────────────────────────────────
        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < data.requiredXPLevel;

        if (itemPriceText != null)
        {
            if (isLevelLocked)
            {
                itemPriceText.text = $"Lvl {data.requiredXPLevel} Req";
            }
            else
            {
                itemPriceText.text = data.noorCoinCost == 0
                    ? "Free"
                    : $"{data.noorCoinCost} \u29DF"; // ⟟ coin glyph (fallback: ⟡)
            }
        }

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

        // Initial affordability tint
        RefreshAffordabilityVisual();
    }

    // ─── Affordability Visuals ────────────────────────────────────────────────

    private void OnEnable()
    {
        NoorCoinManager.OnBalanceChanged += OnBalanceChanged;
        RefreshAffordabilityVisual();
    }

    private void OnDisable()
    {
        NoorCoinManager.OnBalanceChanged -= OnBalanceChanged;
    }

    private void OnBalanceChanged(int _) => RefreshAffordabilityVisual();

    /// <summary>
    /// Tints the price text gold if the player can afford it, red if they cannot.
    /// Only applied for items with a real cost (> 0) that are not locked.
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

        // Locked items don't need affordability tinting
        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < ItemData.requiredXPLevel;
        if (isLevelLocked) return;

        // Free items are always "affordable"
        if (ItemData.noorCoinCost <= 0)
        {
            itemPriceText.color = affordableColor;
            return;
        }

        bool canAfford = NoorCoinManager.Instance != null &&
                         NoorCoinManager.Instance.CanAfford(ItemData.noorCoinCost);

        itemPriceText.color = canAfford ? affordableColor : unaffordableColor;
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
