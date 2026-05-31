using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

[System.Serializable]
public class ShopItemSelectEvent : UnityEvent<ShopItemUI> { }

public class ShopItemUI : MonoBehaviour
{
    [Tooltip("Optional button reference. If not assigned, will look for a Button component on this GameObject.")]
    public Button selectButton; 

    [Tooltip("Event invoked when this shop item is clicked or selected.")]
    public ShopItemSelectEvent OnSelected = new ShopItemSelectEvent();

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

    private void Awake()
    {
        if (selectButton == null)
        {
            selectButton = GetComponent<Button>();
        }

        if (selectButton != null)
        {
            selectButton.onClick.AddListener(HandleClick);
        }
        
        // Ensure CanvasGroup is initialized early
        if (CanvasGroup != null) { }
    }

    private void HandleClick()
    {
        if (OnSelected != null)
        {
            OnSelected.Invoke(this);
        }
    }
}
