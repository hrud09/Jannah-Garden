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
    /// Initializes the UI components with the values from a ShopItemData asset.
    /// </summary>
    public void Initialize(ShopItemData data)
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

        if (itemBackgroundImg != null && data.itemBackground != null)
        {
            itemBackgroundImg.sprite = data.itemBackground;
        }

        if (itemIconBackgroundImg != null && data.itemIconBackground != null)
        {
            itemIconBackgroundImg.sprite = data.itemIconBackground;
        }
    }
}
