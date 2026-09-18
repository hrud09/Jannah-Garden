using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryItemUI : MonoBehaviour
{
    [Header("UI Component References")]
    public Image itemIcon;
    public TMP_Text itemNameText;
    public TMP_Text itemDescriptionText;
    public TMP_Text itemQuantityText;
    public Image itemBackgroundImg;
    public Image itemIconBackgroundImg;

    [Header("State Visual References")]
    public GameObject[] lockedVisuals;
    public GameObject[] unlockedVisuals;

    public TreasureBoxRewardItemData RewardItemData { get; private set; }

    /// <summary>
    /// Initializes the UI components with the values from a TreasureBoxRewardItemData asset,
    /// the quantity owned, and optional background overrides.
    /// </summary>
    public void Initialize(TreasureBoxRewardItemData data, int quantityOwned, Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;
        RewardItemData = data;

        if (itemIcon != null && data.itemIcon != null)
        {
            itemIcon.sprite = data.itemIcon;
        }

        ApplyNameAndDescription(data.LocalizedName, data.LocalizedDescription);

        if (itemQuantityText != null)
        {
            itemQuantityText.text = LocalizationManager.Instance.Get("common.quantity_format", quantityOwned);
        }

        if (itemBackgroundImg != null && customBackground != null)
        {
            itemBackgroundImg.sprite = customBackground;
        }

        if (itemIconBackgroundImg != null && customIconBackground != null)
        {
            itemIconBackgroundImg.sprite = customIconBackground;
        }

        RefreshVisualState();
    }

    private void OnEnable()
    {
        if (PlayerXPManager.Instance != null)
        {
            PlayerXPManager.Instance.OnXPChanged += HandleXPChanged;
        }
        LocalizationManager.OnLocaleChanged += RefreshLocalizedLabels;
        RefreshLocalizedLabels();
    }

    private void OnDisable()
    {
        if (PlayerXPManager.Instance != null)
        {
            PlayerXPManager.Instance.OnXPChanged -= HandleXPChanged;
        }
        LocalizationManager.OnLocaleChanged -= RefreshLocalizedLabels;
    }

    private void HandleXPChanged(int newLevel, float currentXP, float xpToNextLevel)
    {
        RefreshVisualState();
    }

    /// <summary>Re-applies the bound reward's localized name/description — see ShopItemUI's identical
    /// pattern for why this needs to run on enable and on every locale change, not just Initialize().</summary>
    private void RefreshLocalizedLabels()
    {
        if (RewardItemData != null) ApplyNameAndDescription(RewardItemData.LocalizedName, RewardItemData.LocalizedDescription);
    }

    /// <summary>Shapes and assigns the name/description labels for the active locale (RTL mirroring,
    /// Arabic/Urdu joining, Bengali HarfBuzz rendering) instead of a raw <c>.text =</c> assignment.</summary>
    private void ApplyNameAndDescription(string name, string description)
    {
        if (itemNameText != null && !string.IsNullOrEmpty(name)) LocalizedRendering.SetText(itemNameText, name);
        if (itemDescriptionText != null && !string.IsNullOrEmpty(description)) LocalizedRendering.SetText(itemDescriptionText, description);
    }

    public void RefreshVisualState()
    {
        if (RewardItemData == null) return;

        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < RewardItemData.unlockXPLevel;

        if (lockedVisuals != null)
        {
            foreach (var go in lockedVisuals)
            {
                if (go != null) go.SetActive(isLevelLocked);
            }
        }
        
        if (unlockedVisuals != null)
        {
            foreach (var go in unlockedVisuals)
            {
                if (go != null) go.SetActive(!isLevelLocked);
            }
        }
    }
}
