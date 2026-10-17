using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Collapses the quick-action button row behind a single toggle handle.
/// Clicking the handle fades/scales the row open or closed and rotates
/// the handle's arrow to show the current state.
/// </summary>
public class QuickActionDrawerController : MonoBehaviour
{
    public GameObject drawerContentObject;
    public GameObject toggleArrowObject;
    public float animationDuration = 0.25f;
    public bool startOpen = false;

    private RectTransform drawerContent;
    private RectTransform toggleArrow;
    private CanvasGroup drawerCanvasGroup;
    private bool isOpen;
    private Coroutine animationCoroutine;

    private void Awake()
    {
        drawerContent = drawerContentObject != null ? drawerContentObject.GetComponent<RectTransform>() : null;
        toggleArrow = toggleArrowObject != null ? toggleArrowObject.GetComponent<RectTransform>() : null;

        if (drawerContent == null)
        {
            return;
        }

        drawerCanvasGroup = drawerContent.GetComponent<CanvasGroup>();
        if (drawerCanvasGroup == null)
        {
            drawerCanvasGroup = drawerContent.gameObject.AddComponent<CanvasGroup>();
        }

        isOpen = startOpen;
        ApplyStateImmediate(isOpen);
    }

    public void ToggleDrawer()
    {
        isOpen = !isOpen;

        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        animationCoroutine = StartCoroutine(AnimateTo(isOpen));
    }

    private void ApplyStateImmediate(bool open)
    {
        drawerCanvasGroup.alpha = open ? 1f : 0f;
        drawerCanvasGroup.interactable = open;
        drawerCanvasGroup.blocksRaycasts = open;
        drawerContent.localScale = open ? Vector3.one : new Vector3(0.85f, 0.85f, 1f);

        if (toggleArrow != null)
        {
            toggleArrow.localEulerAngles = new Vector3(0f, 0f, open ? 0f : -180f);
        }
    }

    private IEnumerator AnimateTo(bool open)
    {
        float startAlpha = drawerCanvasGroup.alpha;
        float endAlpha = open ? 1f : 0f;
        Vector3 startScale = drawerContent.localScale;
        Vector3 endScale = open ? Vector3.one : new Vector3(0.85f, 0.85f, 1f);
        float startArrowZ = toggleArrow != null ? toggleArrow.localEulerAngles.z : 0f;
        float endArrowZ = open ? 0f : -180f;

        if (open)
        {
            drawerCanvasGroup.blocksRaycasts = true;
        }

        float t = 0f;
        while (t < animationDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / animationDuration);
            float eased = 1f - Mathf.Pow(1f - p, 3f);

            drawerCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, eased);
            drawerContent.localScale = Vector3.Lerp(startScale, endScale, eased);
            if (toggleArrow != null)
            {
                toggleArrow.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(startArrowZ, endArrowZ, eased));
            }

            yield return null;
        }

        drawerCanvasGroup.alpha = endAlpha;
        drawerContent.localScale = endScale;
        drawerCanvasGroup.interactable = open;
        drawerCanvasGroup.blocksRaycasts = open;
        if (toggleArrow != null)
        {
            toggleArrow.localEulerAngles = new Vector3(0f, 0f, endArrowZ);
        }
    }
}
