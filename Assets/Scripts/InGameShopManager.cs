using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class InGameShopManager : MonoBehaviour
{
    public ScrollRect scrollRect; // Reference to the ScrollRect component
    public RectTransform selectedItemUIRef;
    public ShopItemUI[] shopItemUIs; // Array of ShopItemUI components

    [Header("Scroll Behavior Settings")]
    public bool smoothScroll = true;
    public float scrollDuration = 0.3f;
    public AnimationCurve scrollCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Selection Status")]
    public ShopItemUI selectedShopItem;

    private Coroutine scrollCoroutine;
    private bool isDragging = false;
    private bool isSnapping = false;

    private void Start()
    {
        // Automatically hook up listener events for selection focus
        if (shopItemUIs != null)
        {
            foreach (var item in shopItemUIs)
            {
                if (item != null)
                {
                    item.OnSelected += HandleItemSelection;

                    // Fallback: If no Button is configured on ShopItemUI, add one dynamically
                    Button btn = item.GetComponent<Button>();
                    if (btn == null)
                    {
                        btn = item.gameObject.AddComponent<Button>();
                        btn.onClick.AddListener(() => HandleItemSelection(item));
                    }
                }
            }
        }

        // Setup ScrollRect event listeners
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);

            // Hook drag events dynamically using EventTrigger to prevent snap conflict
            EventTrigger trigger = scrollRect.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = scrollRect.gameObject.AddComponent<EventTrigger>();
            }

            // Hook Begin Drag
            EventTrigger.Entry beginDragEntry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
            beginDragEntry.callback.AddListener((data) =>
            {
                isDragging = true;
                isSnapping = false;
                if (scrollCoroutine != null)
                {
                    StopCoroutine(scrollCoroutine);
                }
            });
            trigger.triggers.Add(beginDragEntry);

            // Hook End Drag
            EventTrigger.Entry endDragEntry = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
            endDragEntry.callback.AddListener((data) =>
            {
                isDragging = false;
            });
            trigger.triggers.Add(endDragEntry);
        }

        // Determine the initial selected item based on proximity to selectedItemUIRef on start
        UpdateClosestSelection();

        // Snap to the default selected item on start if one is found/assigned
        if (selectedShopItem != null)
        {
            FocusOnItem(selectedShopItem, smooth: false);
        }
    }

    private void OnDestroy()
    {
        if (shopItemUIs != null)
        {
            foreach (var item in shopItemUIs)
            {
                if (item != null)
                {
                    item.OnSelected -= HandleItemSelection;
                }
            }
        }

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
        }
    }

    private void Update()
    {
        if (scrollRect == null || selectedShopItem == null || isDragging || isSnapping) return;

        // If the scroll view is moving slowly (or has stopped) and we are not dragging, snap to closest item
        float velocityThreshold = 150f;
        float speed = scrollRect.velocity.magnitude;

        if (speed < velocityThreshold && speed > 0.01f)
        {
            StartSnapToClosest();
        }
        else if (speed <= 0.01f)
        {
            // It has completely stopped, double check if we need to align
            float refY = selectedItemUIRef.position.y;
            float itemY = selectedShopItem.GetComponent<RectTransform>().position.y;
            float distance = Mathf.Abs(itemY - refY);

            // If it is not aligned (more than 0.1 units off in world space), start alignment snap
            if (distance > 0.1f)
            {
                StartSnapToClosest();
            }
        }
    }

    private void OnScrollValueChanged(Vector2 value)
    {
        UpdateClosestSelection();
    }

    /// <summary>
    /// Evaluates the distance of all shop items to selectedItemUIRef along the Y axis
    /// and assigns the closest item to selectedShopItem.
    /// </summary>
    private void UpdateClosestSelection()
    {
        if (shopItemUIs == null || shopItemUIs.Length == 0 || selectedItemUIRef == null) return;

        float refY = selectedItemUIRef.position.y;
        ShopItemUI closestItem = null;
        float minDistance = float.MaxValue;

        foreach (var item in shopItemUIs)
        {
            if (item == null) continue;

            float itemY = item.GetComponent<RectTransform>().position.y;
            float distance = Mathf.Abs(itemY - refY);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestItem = item;
            }
        }

        if (closestItem != null && closestItem != selectedShopItem)
        {
            selectedShopItem = closestItem;
        }
    }

    private void StartSnapToClosest()
    {
        isSnapping = true;
        scrollRect.velocity = Vector2.zero;
        FocusOnItem(selectedShopItem, smoothScroll);
    }

    private void HandleItemSelection(ShopItemUI selectedItem)
    {
        selectedShopItem = selectedItem;
        FocusOnItem(selectedItem, smoothScroll);
    }

    /// <summary>
    /// Scrolls the ScrollRect to align the target ShopItemUI with selectedItemUIRef.
    /// </summary>
    public void FocusOnItem(ShopItemUI targetItem, bool smooth = true)
    {
        if (scrollRect == null || selectedItemUIRef == null || targetItem == null) return;

        RectTransform content = scrollRect.content;
        if (content == null) return;

        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
        RectTransform targetRect = targetItem.GetComponent<RectTransform>();

        // Get positions relative to the scroll viewport
        Vector3 targetLocalPos = viewport.InverseTransformPoint(targetRect.position);
        Vector3 refLocalPos = viewport.InverseTransformPoint(selectedItemUIRef.position);

        // Difference vector to offset content position
        Vector3 localDiff = refLocalPos - targetLocalPos;
        Vector2 targetAnchoredPos = content.anchoredPosition;

        if (scrollRect.horizontal)
        {
            targetAnchoredPos.x += localDiff.x;
        }
        if (scrollRect.vertical)
        {
            targetAnchoredPos.y += localDiff.y;
        }

        // Clamp to avoid scrolling past content boundaries
        targetAnchoredPos = ClampAnchoredPosition(targetAnchoredPos);

        if (scrollCoroutine != null)
        {
            StopCoroutine(scrollCoroutine);
        }

        if (smooth && gameObject.activeInHierarchy)
        {
            scrollCoroutine = StartCoroutine(SmoothScrollTo(targetAnchoredPos));
        }
        else
        {
            content.anchoredPosition = targetAnchoredPos;
            scrollRect.velocity = Vector2.zero;
            isSnapping = false;
        }
    }

    private IEnumerator SmoothScrollTo(Vector2 targetPos)
    {
        RectTransform content = scrollRect.content;
        Vector2 startPos = content.anchoredPosition;
        float elapsed = 0f;

        scrollRect.StopMovement(); // Reset velocity and drag

        while (elapsed < scrollDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scrollDuration);
            float curveT = scrollCurve.Evaluate(t);
            content.anchoredPosition = Vector2.Lerp(startPos, targetPos, curveT);
            yield return null;
        }

        content.anchoredPosition = targetPos;
        isSnapping = false;
    }

    private Vector2 ClampAnchoredPosition(Vector2 targetAnchoredPosition)
    {
        if (scrollRect == null || scrollRect.content == null) return targetAnchoredPosition;

        RectTransform content = scrollRect.content;
        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;

        // Temporarily set position to compute accurate UI bounds
        Vector2 originalPosition = content.anchoredPosition;
        content.anchoredPosition = targetAnchoredPosition;

        Canvas.ForceUpdateCanvases();

        Vector3[] viewportCorners = new Vector3[4];
        Vector3[] contentCorners = new Vector3[4];
        viewport.GetWorldCorners(viewportCorners);
        content.GetWorldCorners(contentCorners);

        Vector2 viewportMin = viewport.InverseTransformPoint(viewportCorners[0]);
        Vector2 viewportMax = viewport.InverseTransformPoint(viewportCorners[2]);
        Vector2 contentMin = viewport.InverseTransformPoint(contentCorners[0]);
        Vector2 contentMax = viewport.InverseTransformPoint(contentCorners[2]);

        Vector2 shift = Vector2.zero;

        if (scrollRect.horizontal)
        {
            float contentWidth = contentMax.x - contentMin.x;
            float viewportWidth = viewportMax.x - viewportMin.x;

            if (contentWidth <= viewportWidth)
            {
                shift.x = (viewportMin.x + viewportWidth * 0.5f) - (contentMin.x + contentWidth * 0.5f);
            }
            else
            {
                if (contentMin.x > viewportMin.x)
                {
                    shift.x = viewportMin.x - contentMin.x;
                }
                else if (contentMax.x < viewportMax.x)
                {
                    shift.x = viewportMax.x - contentMax.x;
                }
            }
        }

        if (scrollRect.vertical)
        {
            float contentHeight = contentMax.y - contentMin.y;
            float viewportHeight = viewportMax.y - viewportMin.y;

            if (contentHeight <= viewportHeight)
            {
                shift.y = (viewportMin.y + viewportHeight * 0.5f) - (contentMin.y + contentHeight * 0.5f);
            }
            else
            {
                if (contentMin.y > viewportMin.y)
                {
                    shift.y = viewportMin.y - contentMin.y;
                }
                else if (contentMax.y < viewportMax.y)
                {
                    shift.y = viewportMax.y - contentMax.y;
                }
            }
        }

        content.anchoredPosition = originalPosition; // Revert temporary position assignment
        return targetAnchoredPosition + shift;
    }
}
