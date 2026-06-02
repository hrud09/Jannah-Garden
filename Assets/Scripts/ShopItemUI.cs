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

        if (itemPriceText != null && !string.IsNullOrEmpty(data.itemPrice))
        {
            itemPriceText.text = data.itemPrice;
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
        bool isLocked = data.itemState == ShopItemState.Locked;
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
   
    }
}
