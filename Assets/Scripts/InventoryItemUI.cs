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

        if (itemNameText != null && !string.IsNullOrEmpty(data.itemName))
        {
            itemNameText.text = data.itemName;
        }

        if (itemDescriptionText != null && !string.IsNullOrEmpty(data.itemDescription))
        {
            itemDescriptionText.text = data.itemDescription;
        }

        if (itemQuantityText != null)
        {
            itemQuantityText.text = $"x{quantityOwned}";
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
    }

    private void OnDisable()
    {
        if (PlayerXPManager.Instance != null)
        {
            PlayerXPManager.Instance.OnXPChanged -= HandleXPChanged;
        }
    }

    private void HandleXPChanged(int newLevel, float currentXP, float xpToNextLevel)
    {
        RefreshVisualState();
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
