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
        ShowToast(message, Color.white, ToastTone.Info);
    }

    public void ShowToast(string message, Color textColor)
    {
        ShowToast(message, textColor, ToneFor(textColor));
    }

    /// <summary>Shows a toast with an explicitly chosen chime, for callers that know the severity.</summary>
    public void ShowToast(string message, ToastTone tone)
    {
        ShowToast(message, ColorFor(tone), tone);
    }

    public void ShowToast(string message, Color textColor, ToastTone tone)
    {
        // The tutorial's instruction panel sits in the same lower-screen area as the toast, so stay
        // quiet while it's talking rather than popping up behind/over it. The chime is suppressed
        // with it: no sound should fire for a message the player never sees.
        if (GameOnboardingManager.IsInstructionPanelVisible) return;

        GameSfx.PlayToast(tone);

        if (messageText != null)
        {
            messageText.color = textColor;
            LocalizedRendering.SetText(messageText, message);
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

    /// <summary>
    /// Infers severity from the text colour used by the legacy two-argument overload, so the ~56
    /// existing call sites get a fitting chime without each one being rewritten. Hue is the signal:
    /// reds read as failure, ambers as caution, greens as success, everything else as neutral.
    /// </summary>
    private static ToastTone ToneFor(Color c)
    {
        float h, s, v;
        Color.RGBToHSV(c, out h, out s, out v);

        // Washed-out or near-white text carries no severity of its own.
        if (s < 0.25f) return ToastTone.Info;

        float degrees = h * 360f;
        if (degrees < 20f || degrees >= 330f) return ToastTone.Error;
        if (degrees < 70f) return ToastTone.Warning;
        if (degrees < 170f) return ToastTone.Success;
        return ToastTone.Info;
    }

    /// <summary>Default text colour for a tone, for callers that pass a tone and no colour.</summary>
    private static Color ColorFor(ToastTone tone)
    {
        switch (tone)
        {
            case ToastTone.Success: return new Color(0.68f, 1f, 0.72f);
            case ToastTone.Warning: return new Color(1f, 0.84f, 0.45f);
            case ToastTone.Error:   return new Color(1f, 0.55f, 0.52f);
            case ToastTone.Silent:  return Color.white;
            default:                return Color.white;
        }
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
