using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MinimapBehaviour : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI References")]
    [SerializeField] private RectTransform minimapPanel;
    [SerializeField] private Button expandButton;
    [SerializeField] private Button collapseButton;
    [Tooltip("Only visible while expanded. Snaps the minimap camera back to its default position over the player.")]
    [SerializeField] private Button recenterButton;
    [Tooltip("Only visible while expanded. Points the direction arrow toward the next available treasure box.")]
    [SerializeField] private Button showTreasureBoxButton;

    [Header("Camera References & Settings")]
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private float smallCamSize = 10f;
    [SerializeField] private float largeCamSize = 25f;

    [Header("Size Settings")]
    [SerializeField] private Vector2 smallSize = new Vector2(200f, 200f);
    [SerializeField] private Vector2 largeSize = new Vector2(800f, 800f);

    [Header("Juicy Animation Settings")]
    [SerializeField] private float transitionDuration = 0.4f;
    [SerializeField] 
    private AnimationCurve transitionCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 3.5f),      // Start fast
        new Keyframe(0.6f, 1.12f, 0f, 0f),   // Overshoot to 1.12 at 60%
        new Keyframe(0.85f, 0.96f, 0f, 0f),  // Bounce back slightly to 0.96 at 85%
        new Keyframe(1f, 1f, 0f, 0f)         // Settle perfectly at 1.0
    );

    [Header("Pan Limits")]
    [Tooltip("World-space X/Z minimum the minimap camera can be dragged to. Defaults to the terrain's min corner.")]
    [SerializeField] private Vector2 panMin = new Vector2(-100f, -100f);
    [Tooltip("World-space X/Z maximum the minimap camera can be dragged to. Defaults to the terrain's max corner.")]
    [SerializeField] private Vector2 panMax = new Vector2(100f, 100f);

    [Header("Outside Tap Settings")]
    [Tooltip("When expanded, tapping/clicking anywhere outside the minimap collapses it.")]
    [SerializeField] private bool collapseOnOutsideTap = true;
    [Tooltip("Extra UI areas that should NOT count as 'outside' the minimap (e.g. buttons sitting beside the panel).")]
    [SerializeField] private RectTransform[] additionalInsideRects;

    private bool isExpanded = false;
    private Coroutine transitionCoroutine;
    private Canvas rootCanvas;
    private int lastStateChangeFrame = -1;

    // Drag-to-pan (only active while expanded; collapsing resets it)
    private Transform cameraFollowTarget;
    private Vector3 cameraBaseLocalOffset;
    private Vector3 panOffset;
    private Vector2 lastDragLocalPoint;

    public bool IsExpanded => isExpanded;

    private void Start()
    {
        // Fallback to local RectTransform if not set
        if (minimapPanel == null)
        {
            minimapPanel = GetComponent<RectTransform>();
        }

        // Set up button listeners
        if (expandButton != null)
        {
            expandButton.onClick.AddListener(ExpandMinimap);
        }

        if (collapseButton != null)
        {
            collapseButton.onClick.AddListener(CollapseMinimap);
        }

        if (recenterButton != null)
        {
            recenterButton.onClick.AddListener(RecenterMinimap);
        }

        if (showTreasureBoxButton != null)
        {
            showTreasureBoxButton.onClick.AddListener(ShowTreasureBox);
        }

        // Cache the canvas so outside-tap hit tests use the correct event camera
        Canvas canvas = minimapPanel != null
            ? minimapPanel.GetComponentInParent<Canvas>()
            : GetComponentInParent<Canvas>();
        rootCanvas = canvas != null ? canvas.rootCanvas : null;

        // Cache the minimap camera's resting offset from its follow target (usually the player)
        // so dragging can pan it away and CollapseMinimap can snap it back.
        if (minimapCamera != null)
        {
            cameraFollowTarget = minimapCamera.transform.parent;
            cameraBaseLocalOffset = minimapCamera.transform.localPosition;
        }

        // Apply initial state without animation
        UpdateMinimapState(false);
    }

    private void LateUpdate()
    {
        // Re-centers the minimap camera on its follow target every frame (as parenting normally
        // would) while adding the world-space pan offset from dragging on top.
        if (minimapCamera == null || cameraFollowTarget == null) return;

        Vector3 restPosition = cameraFollowTarget.position + cameraBaseLocalOffset;
        Vector3 position = restPosition + panOffset;
        position.x = Mathf.Clamp(position.x, panMin.x, panMax.x);
        position.z = Mathf.Clamp(position.z, panMin.y, panMax.y);

        // Re-derive panOffset from the clamped position so it never overshoots the limits — this
        // keeps dragging back off an edge immediately responsive instead of lagging behind.
        panOffset = position - restPosition;

        minimapCamera.transform.position = position;
    }

    /// <summary>
    /// Begins tracking a drag gesture on the minimap. Only pans while expanded.
    /// </summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isExpanded || minimapPanel == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(minimapPanel, eventData.position, GetEventCamera(), out lastDragLocalPoint);
    }

    /// <summary>
    /// Pans the minimap camera opposite to the pointer delta so the map content follows the drag.
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!isExpanded || minimapPanel == null || minimapCamera == null) return;

        Vector2 currentLocalPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(minimapPanel, eventData.position, GetEventCamera(), out currentLocalPoint)) return;

        Vector2 localDelta = currentLocalPoint - lastDragLocalPoint;
        lastDragLocalPoint = currentLocalPoint;

        float panelSize = minimapPanel.rect.height;
        if (panelSize <= 0f) return;

        float worldUnitsPerLocalUnit = (2f * GetCurrentCameraSize()) / panelSize;

        // The camera can be rotated (e.g. it turns with the player), so screen-right/screen-up
        // aren't fixed world axes — use the camera's current axes to convert the drag correctly.
        Transform camTransform = minimapCamera.transform;
        Vector3 worldDelta = (camTransform.right * localDelta.x + camTransform.up * localDelta.y) * worldUnitsPerLocalUnit;
        panOffset -= worldDelta;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
    }

    private void Update()
    {
        if (!collapseOnOutsideTap || !isExpanded) return;

        // Ignore the very press that expanded the minimap this frame
        if (Time.frameCount == lastStateChangeFrame) return;

        Vector2 pressPosition;
        if (!TryGetPressPosition(out pressPosition)) return;

        if (IsPointerInsideMinimap(pressPosition)) return;

        CollapseMinimap();
    }

    private void OnDisable()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        if (expandButton != null)
        {
            expandButton.onClick.RemoveListener(ExpandMinimap);
        }

        if (collapseButton != null)
        {
            collapseButton.onClick.RemoveListener(CollapseMinimap);
        }

        if (recenterButton != null)
        {
            recenterButton.onClick.RemoveListener(RecenterMinimap);
        }

        if (showTreasureBoxButton != null)
        {
            showTreasureBoxButton.onClick.RemoveListener(ShowTreasureBox);
        }
    }

    /// <summary>
    /// Expands the minimap to its large size.
    /// </summary>
    public void ExpandMinimap()
    {
        if (isExpanded) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.MinimapExpand);
        isExpanded = true;
        lastStateChangeFrame = Time.frameCount;
        UpdateMinimapState(true);
    }

    /// <summary>
    /// Collapses the minimap to its small size.
    /// </summary>
    public void CollapseMinimap()
    {
        if (!isExpanded) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.MinimapCollapse);
        isExpanded = false;
        lastStateChangeFrame = Time.frameCount;
        panOffset = Vector3.zero;
        UpdateMinimapState(true);
    }

    /// <summary>
    /// Snaps the minimap camera back to its default position over the player.
    /// </summary>
    public void RecenterMinimap()
    {
        panOffset = Vector3.zero;
    }

    /// <summary>
    /// Points the direction arrow toward the next available treasure box for the current tier.
    /// </summary>
    public void ShowTreasureBox()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.TreasureBoxShow);

        if (TreasureBoxManager.Instance != null)
        {
            TreasureBoxManager.Instance.PlayShowAnimationForTier(TreasureBoxManager.Instance.GetUpcomingTier());
        }
    }

    /// <summary>
    /// Returns true (and the screen position) on the frame a pointer press begins.
    /// </summary>
    private bool TryGetPressPosition(out Vector2 position)
    {
        position = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
        {
            position = Pointer.current.position.ReadValue();
            return true;
        }
#else
        if (Input.GetMouseButtonDown(0))
        {
            position = Input.mousePosition;
            return true;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                position = touch.position;
                return true;
            }
        }
#endif
        return false;
    }

    /// <summary>
    /// Checks the press against the minimap panel and anything that should count as part of it.
    /// </summary>
    private bool IsPointerInsideMinimap(Vector2 screenPosition)
    {
        Camera eventCamera = GetEventCamera();

        if (ContainsScreenPoint(minimapPanel, screenPosition, eventCamera)) return true;

        // The collapse/expand buttons may sit outside the panel rect — tapping them must not double-fire
        if (collapseButton != null &&
            ContainsScreenPoint(collapseButton.transform as RectTransform, screenPosition, eventCamera)) return true;

        if (expandButton != null &&
            ContainsScreenPoint(expandButton.transform as RectTransform, screenPosition, eventCamera)) return true;

        if (recenterButton != null &&
            ContainsScreenPoint(recenterButton.transform as RectTransform, screenPosition, eventCamera)) return true;

        if (showTreasureBoxButton != null &&
            ContainsScreenPoint(showTreasureBoxButton.transform as RectTransform, screenPosition, eventCamera)) return true;

        if (additionalInsideRects != null)
        {
            for (int i = 0; i < additionalInsideRects.Length; i++)
            {
                if (ContainsScreenPoint(additionalInsideRects[i], screenPosition, eventCamera)) return true;
            }
        }

        return false;
    }

    private static bool ContainsScreenPoint(RectTransform rect, Vector2 screenPosition, Camera eventCamera)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, eventCamera);
    }

    private Camera GetEventCamera()
    {
        if (rootCanvas == null) return null;
        return rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
    }

    /// <summary>
    /// Updates the minimap panel size and button visibility based on current state.
    /// </summary>
    private void UpdateMinimapState(bool animate = true)
    {
        Vector2 targetSize = isExpanded ? largeSize : smallSize;
        float targetCamSize = isExpanded ? largeCamSize : smallCamSize;

        if (animate && Application.isPlaying)
        {
            StartTransition(targetSize, targetCamSize);
        }
        else
        {
            if (minimapPanel != null)
            {
                minimapPanel.sizeDelta = targetSize;
            }
            SetCameraSize(targetCamSize);
        }

        // Set expand button enabled state
        if (expandButton != null)
        {
            expandButton.enabled = !isExpanded;
        }

        // Set collapse button active state
        if (collapseButton != null)
        {
            collapseButton.gameObject.SetActive(isExpanded);
        }

        // The recenter button is only useful (and only visible) while expanded
        if (recenterButton != null)
        {
            recenterButton.gameObject.SetActive(isExpanded);
        }

        // The show-treasure-box button is only useful (and only visible) while expanded
        if (showTreasureBoxButton != null)
        {
            showTreasureBoxButton.gameObject.SetActive(isExpanded);
        }
    }

    private void StartTransition(Vector2 targetSize, float targetCamSize)
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }
        transitionCoroutine = StartCoroutine(TransitionRoutine(targetSize, targetCamSize));
    }

    private System.Collections.IEnumerator TransitionRoutine(Vector2 targetSize, float targetCamSize)
    {
        Vector2 startSize = minimapPanel != null ? minimapPanel.sizeDelta : targetSize;
        float startCamSize = GetCurrentCameraSize();
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / transitionDuration);
            float curveValue = transitionCurve.Evaluate(percent);
            
            if (minimapPanel != null)
            {
                // LerpUnclamped allows the curve to overshoot or undershoot for a juicy spring effect
                minimapPanel.sizeDelta = Vector2.LerpUnclamped(startSize, targetSize, curveValue);
            }
            
            SetCameraSize(Mathf.LerpUnclamped(startCamSize, targetCamSize, curveValue));
            yield return null;
        }

        if (minimapPanel != null)
        {
            minimapPanel.sizeDelta = targetSize;
        }
        SetCameraSize(targetCamSize);
        transitionCoroutine = null;
    }

    private float GetCurrentCameraSize()
    {
        if (minimapCamera == null) return 0f;
        return minimapCamera.orthographic ? minimapCamera.orthographicSize : minimapCamera.fieldOfView;
    }

    private void SetCameraSize(float size)
    {
        if (minimapCamera == null) return;
        if (minimapCamera.orthographic)
        {
            minimapCamera.orthographicSize = size;
        }
        else
        {
            minimapCamera.fieldOfView = size;
        }
    }
}
