using TMPro;
using UnityEngine;
using DG.Tweening;

public class ToastMessageManager : MonoBehaviour
{
    public RectTransform messageHolder;
    public TMP_Text messageText;
    public float messageHolderRisingY = -325;
    
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
            // Ensure it starts deactivated
            messageHolder.gameObject.SetActive(false);
        }
    }

    public bool IsShowing => messageHolder != null && messageHolder.gameObject.activeSelf;

    public void ShowToast(string message)
    {
        if (messageText != null) messageText.text = message;
        
        if (messageHolder != null) 
        {
            messageHolder.gameObject.SetActive(true);
            messageHolder.DOKill();
            
            // Start from default position and rise up
            messageHolder.anchoredPosition = _defaultPos;
            messageHolder.DOAnchorPosY(messageHolderRisingY, 0.6f).SetEase(Ease.OutElastic);
        }

        CancelInvoke(nameof(HideToast));
        Invoke(nameof(HideToast), 3f);
    }

    private void HideToast()
    {
        if (messageHolder != null) 
        {
            messageHolder.DOKill();
            // Animate back to default position before deactivating
            messageHolder.DOAnchorPosY(_defaultPos.y, 0.3f).SetEase(Ease.InBack).OnComplete(() => 
            {
                messageHolder.gameObject.SetActive(false);
            });
        }
    }
}
