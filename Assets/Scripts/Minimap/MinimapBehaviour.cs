using UnityEngine;
using UnityEngine.UI;

public class MinimapBehaviour : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RectTransform minimapPanel;
    [SerializeField] private Button expandButton;
    [SerializeField] private Button collapseButton;

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

    private bool isExpanded = false;
    private Coroutine transitionCoroutine;

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

        // Apply initial state without animation
        UpdateMinimapState(false);
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
    }

    /// <summary>
    /// Expands the minimap to its large size.
    /// </summary>
    public void ExpandMinimap()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.MinimapExpand);
        isExpanded = true;
        UpdateMinimapState(true);
    }

    /// <summary>
    /// Collapses the minimap to its small size.
    /// </summary>
    public void CollapseMinimap()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.MinimapCollapse);
        isExpanded = false;
        UpdateMinimapState(true);
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
