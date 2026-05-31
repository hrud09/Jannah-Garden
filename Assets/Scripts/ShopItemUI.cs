using UnityEngine;
using UnityEngine.UI;
using System;

public class ShopItemUI : MonoBehaviour
{
    [Tooltip("Optional button reference. If not assigned, will look for a Button component on this GameObject.")]
    public Button selectButton; 

    public event Action<ShopItemUI> OnSelected;

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
    }

    private void HandleClick()
    {
        OnSelected?.Invoke(this);
    }
}
