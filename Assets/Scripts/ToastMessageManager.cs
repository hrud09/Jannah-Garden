using TMPro;
using UnityEngine;
using DG.Tweening;

public class ToastMessageManager : MonoBehaviour
{
    public RectTransform messageHolder;
    public TMP_Text messageText;
    
    [Tooltip("The Y position when the toast is visible on screen.")]
    public float messageHolderRisingY = -425f;
    
    [Tooltip("The Y position when the toast is hidden off screen. Set to a very low negative number to come from the bottom.")]
    public float messageHolderOffscreenY = -760f;
    
    private Vector2 _defaultPos;

    public static ToastMessageManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        if (messageHolder != null)
        {
            _defaultPos = messageHolder.anchoredPosition;
            messageHolder.gameObject.SetActive(false);
        }
    }

    public bool IsShowing => messageHolder != null && messageHolder.gameObject.activeSelf;

    public void ShowToast(string message)
    {
        ShowToast(message, Color.white);
    }

    public void ShowToast(string message, Color textColor)
    {
        if (messageText != null) 
        {
            messageText.text = message;
            messageText.color = textColor;
        }
        
        if (messageHolder != null) 
        {
            messageHolder.gameObject.SetActive(true);
            messageHolder.DOKill();
            
            // Start from bottom off-screen position
            messageHolder.anchoredPosition = new Vector2(_defaultPos.x, messageHolderOffscreenY);
            messageHolder.DOAnchorPosY(messageHolderRisingY, 0.6f).SetEase(Ease.OutElastic, 0.5f);
        }

        CancelInvoke(nameof(HideToast));
        Invoke(nameof(HideToast), 3f);
    }

    private void HideToast()
    {
        if (messageHolder != null) 
        {
            messageHolder.DOKill();
            // Animate back to bottom off-screen position before deactivating
            messageHolder.DOAnchorPosY(messageHolderOffscreenY, 0.3f).SetEase(Ease.InBack).OnComplete(() => 
            {
                messageHolder.gameObject.SetActive(false);
            });
        }
    }
}
