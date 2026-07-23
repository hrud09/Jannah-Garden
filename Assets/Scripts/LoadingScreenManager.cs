using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A persistent, self-contained loading screen system.
/// Place this on a GameObject in the Init Scene (or any first scene).
/// It uses DontDestroyOnLoad to survive across all scene transitions.
///
/// Usage:
///   LoadingScreenManager.Instance.LoadScene("Scene Name");
///
/// The UI is built entirely at runtime — no manual prefab wiring needed.
/// Customise colours, sprites, and tips via the Inspector.
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────
    public static LoadingScreenManager Instance { get; private set; }

    // ─────────────────────────────────────────────
    //  Inspector — Init Scene Bootstrap
    // ─────────────────────────────────────────────
    [Header("Init Scene Bootstrap")]
    [Tooltip("Enable this ONLY on the instance that lives in the Init Scene. " +
             "It will automatically start loading the target scene on Start().")]
    public bool isInitScene = false;

    [Tooltip("The scene to auto-load when isInitScene is true.")]
    public string sceneToLoadOnInit = "Jannah Garden";

    // ─────────────────────────────────────────────
    //  Inspector — Visual Settings
    // ─────────────────────────────────────────────
    [Header("Visual Settings")]
    [Tooltip("Background colour of the loading screen.")]
    public Color backgroundColor = new Color(0.04f, 0.06f, 0.10f, 1f);

    [Tooltip("Accent colour used for the progress bar fill and percentage text.")]
    public Color accentColor = new Color(0.40f, 0.78f, 0.55f, 1f);

    [Tooltip("Secondary text colour (status label, tips).")]
    public Color secondaryTextColor = new Color(0.65f, 0.70f, 0.78f, 1f);

    [Tooltip("Optional background sprite. If null a solid colour is used.")]
    public Sprite backgroundSprite;

    [FormerlySerializedAs("logoSprite")]
    [Tooltip("Logo shown above the progress bar while the Jannah Garden scene loads.")]
    public Sprite jannahGardenLogoSprite;

    [Tooltip("Logo shown above the progress bar while the Outer Garden scene loads.")]
    public Sprite outerGardenLogoSprite;

    [Tooltip("Scene name treated as 'Outer Garden' when choosing which logo to show.")]
    public string outerGardenSceneName = "Outer Garden";

    [Tooltip("Scene name treated as 'Jannah Garden' when choosing which logo to show.")]
    public string jannahGardenSceneName = "Jannah Garden";

    [Tooltip("Logo display size in pixels.")]
    public Vector2 logoSize = new Vector2(180f, 180f);

    // ─────────────────────────────────────────────
    //  Inspector — Timing
    // ─────────────────────────────────────────────
    [Header("Timing")]
    [Tooltip("Minimum seconds the loading screen stays visible (prevents flash-loads).")]
    public float minDisplayTime = 1.5f;

    [Tooltip("Duration of the fade-in / fade-out animations.")]
    public float fadeDuration = 0.4f;

    [Tooltip("Seconds to wait after the scene is loaded before fading out, " +
             "so the new scene has time to render its first frames.")]
    public float hideDelay = 0.3f;

    [Tooltip("How fast the displayed progress catches up to the real progress. " +
             "Higher = snappier, lower = smoother.")]
    public float progressLerpSpeed = 3f;

    [Tooltip("Safety cap (seconds) on how long external hold-requests may keep the " +
             "loading screen up after loading finishes (e.g. progressive mesh activation). " +
             "The panel fades out anyway once this elapses.")]
    public float maxHoldTime = 30f;

    // ─────────────────────────────────────────────
    //  Inspector — Loading Tips
    // ─────────────────────────────────────────────
    [Header("Loading Tips (optional)")]
    [Tooltip("Random tips / messages shown while loading. Leave empty to hide the tips text.")]
    [TextArea(2, 4)]
    public string[] loadingTips;

    // ─────────────────────────────────────────────
    //  Public State
    // ─────────────────────────────────────────────
    /// <summary>True while a scene load is in progress.</summary>
    public bool IsLoading { get; private set; }

    // ─────────────────────────────────────────────
    //  Inspector — Canvas Setup
    // ─────────────────────────────────────────────
    [Header("Canvas Settings")]
    [Tooltip("If assigned, the loading screen UI will be built inside this Canvas. " +
             "If left empty, a new canvas will be generated at runtime.")]
    public Canvas targetCanvas;

    // ─────────────────────────────────────────────
    //  Inspector — UI References
    // ─────────────────────────────────────────────
    [Header("UI References (Assigned Automatically or Manually)")]
    public Canvas loadingCanvas;
    public CanvasGroup canvasGroup;
    public GameObject loadingPanel;
    public Image backgroundImage;
    public Image logoImage;
    public Slider progressBar;
    public Image progressBarFill;
    public Image progressBarBackground;
    public TMP_Text percentText;
    public TMP_Text statusText;
    public TMP_Text tipsText;
    public RectTransform spinnerRoot;
    public Image[] spinnerDots;

    // Spinner dot count
    private const int SPINNER_DOT_COUNT = 3;

    // Progress smoothing
    private float _displayedProgress;
    private float _targetProgress;

    // Coroutine handle so we can stop it if LoadScene is called mid-load
    private Coroutine _loadRoutine;

    // External systems can hold the loading screen open until they finish work
    // behind the panel (e.g. OcclusionCullingManager's progressive mesh activation).
    private readonly HashSet<object> _loadHolds = new HashSet<object>();

    /// <summary>
    /// Registers a hold that keeps the loading screen visible after loading completes.
    /// The panel will not fade out until every registered hold has been released
    /// (or <see cref="maxHoldTime"/> elapses). Safe to call while a load is in progress.
    /// </summary>
    public void AddLoadHold(object token)
    {
        if (token != null) _loadHolds.Add(token);
    }

    /// <summary>
    /// Releases a hold previously registered with <see cref="AddLoadHold"/>.
    /// The loading screen fades out once all holds are cleared.
    /// </summary>
    public void RemoveLoadHold(object token)
    {
        if (token != null) _loadHolds.Remove(token);
    }

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        // ── Singleton guard ──
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Find existing loading panel reference if already built in editor
        FindUIReferences();

        // If not found, build it dynamically
        if (loadingPanel == null)
        {
            BuildUI();
        }

        // Start hidden (alpha 0, panel inactive)
        HideImmediate();
    }

    private void FindUIReferences()
    {
        Transform panelTrans = null;
        if (targetCanvas != null)
        {
            panelTrans = targetCanvas.transform.Find("LoadingPanel");
            loadingCanvas = targetCanvas;
        }
        else
        {
            Transform canvasTrans = transform.Find("LoadingScreen_Canvas");
            if (canvasTrans != null)
            {
                loadingCanvas = canvasTrans.GetComponent<Canvas>();
                panelTrans = canvasTrans.Find("LoadingPanel");
            }
        }

        if (panelTrans != null)
        {
            loadingPanel = panelTrans.gameObject;
            canvasGroup = loadingPanel.GetComponent<CanvasGroup>();

            // Resolve references inside
            Transform bgTrans = panelTrans.Find("Background");
            if (bgTrans != null) backgroundImage = bgTrans.GetComponent<Image>();

            Transform contentTrans = panelTrans.Find("Content");
            if (contentTrans != null)
            {
                Transform logoTrans = contentTrans.Find("Logo");
                if (logoTrans != null) logoImage = logoTrans.GetComponent<Image>();
            }

            Transform bottomTrans = panelTrans.Find("BottomArea");
            if (bottomTrans != null)
            {
                Transform statusTrans = bottomTrans.Find("StatusText");
                if (statusTrans != null) statusText = statusTrans.GetComponent<TMP_Text>();

                Transform progressTrans = bottomTrans.Find("ProgressBar");
                if (progressTrans != null)
                {
                    progressBar = progressTrans.GetComponent<Slider>();
                    if (progressBar.fillRect != null)
                    {
                        progressBarFill = progressBar.fillRect.GetComponent<Image>();
                    }
                    Transform trackBgTrans = progressTrans.Find("Background");
                    if (trackBgTrans != null) progressBarBackground = trackBgTrans.GetComponent<Image>();
                }

                Transform percentTrans = bottomTrans.Find("PercentText");
                if (percentTrans != null) percentText = percentTrans.GetComponent<TMP_Text>();

                Transform tipsTrans = bottomTrans.Find("TipsText");
                if (tipsTrans != null) tipsText = tipsTrans.GetComponent<TMP_Text>();

                Transform spinnerTrans = bottomTrans.Find("Spinner");
                if (spinnerTrans != null)
                {
                    spinnerRoot = spinnerTrans.GetComponent<RectTransform>();
                    spinnerDots = spinnerTrans.GetComponentsInChildren<Image>(true);
                }
            }
        }
    }

    private void Start()
    {
        // Automatically start the loading if we are in the Init Scene (by name or index) or if isInitScene is set.
        string currentSceneName = SceneManager.GetActiveScene().name;
        if (isInitScene || currentSceneName == "Init Scene" || SceneManager.GetActiveScene().buildIndex == 0)
        {
            // Show loading screen immediately and begin loading
            ShowImmediate();
            LoadScene(sceneToLoadOnInit);
        }
    }

    private void Update()
    {
        if (!IsLoading) return;

        // Smoothly interpolate the displayed progress toward the target
        _displayedProgress = Mathf.Lerp(_displayedProgress, _targetProgress,
            Time.unscaledDeltaTime * progressLerpSpeed);

        // Update UI
        if (progressBar != null)
            progressBar.value = _displayedProgress;

        if (percentText != null)
            percentText.text = Mathf.RoundToInt(_displayedProgress * 100f) + "%";

        // Animate spinner dots
        AnimateSpinner();
    }

    // ─────────────────────────────────────────────
    //  Inspector — Scene Partitioning / Additive Loading
    // ─────────────────────────────────────────────
    [System.Serializable]
    public struct AdditiveSceneGroup
    {
        [Tooltip("The main base scene name (e.g. 'Outer Garden').")]
        public string baseSceneName;
        [Tooltip("List of sub-scenes to load additively with the base scene (e.g. 'OuterGarden_Art', 'OuterGarden_Props').")]
        public string[] subScenes;
    }

    [Header("Additive Scene Loading Settings")]
    [Tooltip("Define groups of scenes that should be loaded additively when a base scene is loaded.")]
    public AdditiveSceneGroup[] additiveSceneGroups;

    // ─────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────

    /// <summary>
    /// Shows the loading screen and asynchronously loads the given scene.
    /// Also handles additive scene loading if scene group is defined.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_loadRoutine != null)
        {
            StopCoroutine(_loadRoutine);
        }

        _loadRoutine = StartCoroutine(LoadSceneRoutine(sceneName));
    }

    // ─────────────────────────────────────────────
    //  Core Loading Coroutine
    // ─────────────────────────────────────────────

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        IsLoading = true;
        _displayedProgress = 0f;
        _targetProgress = 0f;
        _loadHolds.Clear();

        // Check if this scene has sub-scenes associated with it
        AdditiveSceneGroup activeGroup = default;
        bool hasSubScenes = false;
        if (additiveSceneGroups != null)
        {
            foreach (var group in additiveSceneGroups)
            {
                if (group.baseSceneName == sceneName && group.subScenes != null && group.subScenes.Length > 0)
                {
                    activeGroup = group;
                    hasSubScenes = true;
                    break;
                }
            }
        }

        // Update status text
        SetStatusText("Loading base scene...");

        // Pick a random tip
        if (loadingTips != null && loadingTips.Length > 0 && tipsText != null)
        {
            tipsText.text = loadingTips[Random.Range(0, loadingTips.Length)];
            tipsText.gameObject.SetActive(true);
        }

        // Show the logo that matches the scene being loaded
        ApplyLogoForScene(sceneName);

        // Fade in
        yield return StartCoroutine(FadeIn());

        // Record the time we became visible
        float showTime = Time.unscaledTime;

        // Wait one frame so the UI is fully visible before the async load starts
        yield return null;

        // 1. Begin loading the base scene
        AsyncOperation baseAsyncOp = SceneManager.LoadSceneAsync(sceneName);
        baseAsyncOp.allowSceneActivation = false;

        // Total weight calculation
        // The base scene gets 50% of weight, and additive subscenes share the other 50%
        float baseWeight = hasSubScenes ? 0.5f : 1.0f;

        // Monitor base scene loading progress
        while (baseAsyncOp.progress < 0.9f)
        {
            float baseProgress = Mathf.Clamp01(baseAsyncOp.progress / 0.9f);
            _targetProgress = baseProgress * baseWeight;
            yield return null;
        }

        // Allow base scene to activate
        baseAsyncOp.allowSceneActivation = true;
        while (!baseAsyncOp.isDone)
        {
            yield return null;
        }

        // 2. Load sub-scenes additively if defined
        if (hasSubScenes)
        {
            SetStatusText("Loading environments...");
            List<AsyncOperation> subSceneOps = new List<AsyncOperation>();

            // Start all additive loads
            foreach (string subScene in activeGroup.subScenes)
            {
                AsyncOperation subOp = SceneManager.LoadSceneAsync(subScene, LoadSceneMode.Additive);
                if (subOp != null)
                {
                    subSceneOps.Add(subOp);
                }
            }

            // Monitor progress of all additive sub-scenes
            bool allDone = false;
            while (!allDone)
            {
                allDone = true;
                float totalSubProgress = 0f;

                foreach (var subOp in subSceneOps)
                {
                    totalSubProgress += subOp.progress; // ranges from 0 to 1
                    if (!subOp.isDone)
                    {
                        allDone = false;
                    }
                }

                float avgSubProgress = subSceneOps.Count > 0 ? (totalSubProgress / subSceneOps.Count) : 1f;
                _targetProgress = 0.5f + (avgSubProgress * 0.5f);

                yield return null;
            }
        }

        // Loading is fully done. Fill the bar to 100%.
        _targetProgress = 1f;

        // Enforce minimum display time
        float elapsed = Time.unscaledTime - showTime;
        if (elapsed < minDisplayTime)
        {
            yield return new WaitForSecondsRealtime(minDisplayTime - elapsed);
        }

        // Wait for the displayed progress to visually reach ~100%
        while (_displayedProgress < 0.99f)
        {
            yield return null;
        }

        // Snap to 100%
        _displayedProgress = 1f;
        if (progressBar != null) progressBar.value = 1f;
        if (percentText != null) percentText.text = "100%";

        SetStatusText("Preparing scene...");

        // Wait for any external systems still holding the screen open — e.g. the
        // OcclusionCullingManager activating scene meshes behind the panel. The
        // meshes populate the world while the loading screen stays fully opaque.
        if (_loadHolds.Count > 0)
        {
            float holdStart = Time.unscaledTime;
            while (_loadHolds.Count > 0 && (Time.unscaledTime - holdStart) < maxHoldTime)
            {
                yield return null;
            }
            _loadHolds.Clear();
        }

        // Give the new scenes a moment to render their first frames
        yield return new WaitForSecondsRealtime(hideDelay);

        // Fade out
        yield return StartCoroutine(FadeOut());

        IsLoading = false;
        _loadRoutine = null;
    }

    // ─────────────────────────────────────────────
    //  Fade Animations
    // ─────────────────────────────────────────────

    private IEnumerator FadeIn()
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (canvasGroup == null) yield break;

        canvasGroup.alpha = 0f;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }

    private IEnumerator FadeOut()
    {
        if (canvasGroup == null) yield break;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.SmoothStep(1f, 0f, t / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────
    //  Immediate Show / Hide (no animation)
    // ─────────────────────────────────────────────

    private void ShowImmediate()
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    private void HideImmediate()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────
    //  Spinner Animation
    // ─────────────────────────────────────────────

    private void AnimateSpinner()
    {
        if (spinnerDots == null) return;

        for (int i = 0; i < spinnerDots.Length; i++)
        {
            if (spinnerDots[i] == null) continue;

            // Each dot pulses with a phase offset
            float phase = Time.unscaledTime * 3f + i * 0.7f;
            float alpha = Mathf.Abs(Mathf.Sin(phase));
            Color c = spinnerDots[i].color;
            c.a = Mathf.Lerp(0.25f, 1f, alpha);
            spinnerDots[i].color = c;

            // Subtle scale pulse
            float scale = Mathf.Lerp(0.7f, 1.0f, alpha);
            spinnerDots[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    // ─────────────────────────────────────────────
    //  Helper
    // ─────────────────────────────────────────────

    private void SetStatusText(string text)
    {
        if (statusText != null)
            statusText.text = text;
    }

    // ─────────────────────────────────────────────
    //  Per-Scene Logo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Sets the logo image to the sprite matching the scene being loaded and
    /// hides the logo entirely if no sprite is available for it.
    /// </summary>
    private void ApplyLogoForScene(string sceneName)
    {
        if (logoImage == null) return;

        Sprite sprite = GetLogoForScene(sceneName);
        if (sprite != null)
        {
            logoImage.sprite = sprite;
            logoImage.gameObject.SetActive(true);
        }
        else
        {
            // No logo assigned for this scene — hide it rather than show a stale one.
            logoImage.gameObject.SetActive(false);
        }
    }

    /// <summary>Returns the logo sprite configured for the given scene, with sensible fallbacks.</summary>
    private Sprite GetLogoForScene(string sceneName)
    {
        if (sceneName == outerGardenSceneName)
            return outerGardenLogoSprite != null ? outerGardenLogoSprite : jannahGardenLogoSprite;

        if (sceneName == jannahGardenSceneName)
            return jannahGardenLogoSprite != null ? jannahGardenLogoSprite : outerGardenLogoSprite;

        // Any other scene: default to the Jannah Garden logo.
        return jannahGardenLogoSprite != null ? jannahGardenLogoSprite : outerGardenLogoSprite;
    }

    // ═══════════════════════════════════════════════
    //  RUNTIME UI BUILDER
    //  Builds the entire loading screen UI hierarchy
    //  in code so the prefab needs zero manual wiring.
    // ═══════════════════════════════════════════════
    
    [ContextMenu("Build Loading UI")]
    public void BuildUI()
    {
        // First, check if a loading panel or generated canvas already exists and clean it up
        Transform existingPanel = transform.Find("LoadingPanel");
        if (existingPanel != null)
        {
            DestroyImmediate(existingPanel.gameObject);
        }
        Transform existingCanvas = transform.Find("LoadingScreen_Canvas");
        if (existingCanvas != null)
        {
            DestroyImmediate(existingCanvas.gameObject);
        }

        Transform parentTransform;

        if (targetCanvas != null)
        {
            // If targetCanvas is assigned, we attach the loading panel directly to it.
            // But we must first check if a LoadingPanel already exists under targetCanvas
            Transform existingTargetPanel = targetCanvas.transform.Find("LoadingPanel");
            if (existingTargetPanel != null)
            {
                DestroyImmediate(existingTargetPanel.gameObject);
            }
            parentTransform = targetCanvas.transform;
            loadingCanvas = targetCanvas;
        }
        else
        {
            // ── 1. Canvas (screen-space overlay, highest sort order) ──
            GameObject canvasGO = new GameObject("LoadingScreen_Canvas");
            canvasGO.transform.SetParent(transform, false);

            loadingCanvas = canvasGO.AddComponent<Canvas>();
            loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            loadingCanvas.sortingOrder = 999;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();
            parentTransform = canvasGO.transform;
        }

        // ── 2. Loading Panel (holds everything, has CanvasGroup for fading) ──
        loadingPanel = new GameObject("LoadingPanel");
        loadingPanel.transform.SetParent(parentTransform, false);

        RectTransform panelRT = loadingPanel.AddComponent<RectTransform>();
        StretchFull(panelRT);

        canvasGroup = loadingPanel.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = true; // Block input while loading
        canvasGroup.interactable = false;

        // ── 3. Background Image ──
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(loadingPanel.transform, false);
        RectTransform bgRT = bgGO.AddComponent<RectTransform>();
        StretchFull(bgRT);

        backgroundImage = bgGO.AddComponent<Image>();
        backgroundImage.color = backgroundColor;
        if (backgroundSprite != null)
        {
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = Color.white;
        }

        // ── 4. Decorative vignette overlay ──
        GameObject vignetteGO = new GameObject("Vignette");
        vignetteGO.transform.SetParent(loadingPanel.transform, false);
        RectTransform vignetteRT = vignetteGO.AddComponent<RectTransform>();
        StretchFull(vignetteRT);
        Image vignetteImg = vignetteGO.AddComponent<Image>();
        vignetteImg.color = new Color(0f, 0f, 0f, 0.15f);
        vignetteImg.raycastTarget = false;

        // ── 5. Centre content container ──
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(loadingPanel.transform, false);
        RectTransform contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0.5f, 0.5f);
        contentRT.anchorMax = new Vector2(0.5f, 0.5f);
        contentRT.pivot = new Vector2(0.5f, 0.5f);
        contentRT.sizeDelta = new Vector2(500f, 400f);
        contentRT.anchoredPosition = Vector2.zero;

        // ── 6. Logo (optional) ──
        // Create the logo image if either scene logo is assigned; the correct
        // sprite is chosen per-scene at load time via ApplyLogoForScene().
        Sprite defaultLogo = jannahGardenLogoSprite != null ? jannahGardenLogoSprite : outerGardenLogoSprite;
        if (defaultLogo != null)
        {
            GameObject logoGO = new GameObject("Logo");
            logoGO.transform.SetParent(contentGO.transform, false);
            RectTransform logoRT = logoGO.AddComponent<RectTransform>();
            logoRT.anchorMin = new Vector2(0.5f, 1f);
            logoRT.anchorMax = new Vector2(0.5f, 1f);
            logoRT.pivot = new Vector2(0.5f, 1f);
            logoRT.sizeDelta = logoSize;
            logoRT.anchoredPosition = new Vector2(0f, 0f);

            logoImage = logoGO.AddComponent<Image>();
            logoImage.sprite = defaultLogo;
            logoImage.preserveAspect = true;
            logoImage.raycastTarget = false;
        }
        else
        {
            logoImage = null;
        }

        // ── 7. Bottom area (progress bar region) ──
        GameObject bottomGO = new GameObject("BottomArea");
        bottomGO.transform.SetParent(loadingPanel.transform, false);
        RectTransform bottomRT = bottomGO.AddComponent<RectTransform>();
        bottomRT.anchorMin = new Vector2(0.5f, 0f);
        bottomRT.anchorMax = new Vector2(0.5f, 0f);
        bottomRT.pivot = new Vector2(0.5f, 0f);
        bottomRT.sizeDelta = new Vector2(600f, 200f);
        bottomRT.anchoredPosition = new Vector2(0f, 60f);

        // ── 8. Status text ("Loading...") ──
        GameObject statusGO = new GameObject("StatusText");
        statusGO.transform.SetParent(bottomGO.transform, false);
        RectTransform statusRT = statusGO.AddComponent<RectTransform>();
        statusRT.anchorMin = new Vector2(0f, 1f);
        statusRT.anchorMax = new Vector2(1f, 1f);
        statusRT.pivot = new Vector2(0.5f, 1f);
        statusRT.sizeDelta = new Vector2(0f, 35f);
        statusRT.anchoredPosition = new Vector2(0f, 0f);

        statusText = statusGO.AddComponent<TextMeshProUGUI>();
        statusText.text = "Loading...";
        statusText.fontSize = 22f;
        statusText.color = secondaryTextColor;
        statusText.alignment = TextAlignmentOptions.Left;
        statusText.raycastTarget = false;
        statusText.fontStyle = FontStyles.Normal;

        // ── 9. Progress bar ──
        BuildProgressBar(bottomGO.transform);

        // ── 10. Percentage text ──
        GameObject percentGO = new GameObject("PercentText");
        percentGO.transform.SetParent(bottomGO.transform, false);
        RectTransform percentRT = percentGO.AddComponent<RectTransform>();
        percentRT.anchorMin = new Vector2(1f, 1f);
        percentRT.anchorMax = new Vector2(1f, 1f);
        percentRT.pivot = new Vector2(1f, 1f);
        percentRT.sizeDelta = new Vector2(80f, 35f);
        percentRT.anchoredPosition = new Vector2(0f, 0f);

        percentText = percentGO.AddComponent<TextMeshProUGUI>();
        percentText.text = "0%";
        percentText.fontSize = 22f;
        percentText.color = accentColor;
        percentText.alignment = TextAlignmentOptions.Right;
        percentText.raycastTarget = false;
        percentText.fontStyle = FontStyles.Bold;

        // ── 11. Spinner dots ──
        BuildSpinner(bottomGO.transform);

        // ── 12. Tips text (optional, below progress bar) ──
        GameObject tipsGO = new GameObject("TipsText");
        tipsGO.transform.SetParent(bottomGO.transform, false);
        RectTransform tipsRT = tipsGO.AddComponent<RectTransform>();
        tipsRT.anchorMin = new Vector2(0f, 0f);
        tipsRT.anchorMax = new Vector2(1f, 0f);
        tipsRT.pivot = new Vector2(0.5f, 1f);
        tipsRT.sizeDelta = new Vector2(0f, 50f);
        tipsRT.anchoredPosition = new Vector2(0f, 40f);

        tipsText = tipsGO.AddComponent<TextMeshProUGUI>();
        tipsText.text = "";
        tipsText.fontSize = 17f;
        tipsText.color = new Color(secondaryTextColor.r, secondaryTextColor.g, secondaryTextColor.b, 0.7f);
        tipsText.alignment = TextAlignmentOptions.Center;
        tipsText.fontStyle = FontStyles.Italic;
        tipsText.raycastTarget = false;
        tipsText.enableWordWrapping = true;

        if (loadingTips == null || loadingTips.Length == 0)
        {
            tipsGO.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────
    //  Progress Bar Builder
    // ─────────────────────────────────────────────

    private void BuildProgressBar(Transform parent)
    {
        // Slider root
        GameObject sliderGO = new GameObject("ProgressBar");
        sliderGO.transform.SetParent(parent, false);
        RectTransform sliderRT = sliderGO.AddComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0f, 1f);
        sliderRT.anchorMax = new Vector2(1f, 1f);
        sliderRT.pivot = new Vector2(0.5f, 1f);
        sliderRT.sizeDelta = new Vector2(0f, 14f);
        sliderRT.anchoredPosition = new Vector2(0f, -42f);

        progressBar = sliderGO.AddComponent<Slider>();
        progressBar.minValue = 0f;
        progressBar.maxValue = 1f;
        progressBar.value = 0f;
        progressBar.interactable = false;
        progressBar.transition = Selectable.Transition.None;

        // Background track
        GameObject bgTrack = new GameObject("Background");
        bgTrack.transform.SetParent(sliderGO.transform, false);
        RectTransform bgTrackRT = bgTrack.AddComponent<RectTransform>();
        StretchFull(bgTrackRT);

        progressBarBackground = bgTrack.AddComponent<Image>();
        progressBarBackground.color = new Color(1f, 1f, 1f, 0.08f);

        // Create rounded look by adding outline
        Outline bgOutline = bgTrack.AddComponent<Outline>();
        bgOutline.effectColor = new Color(1f, 1f, 1f, 0.03f);
        bgOutline.effectDistance = new Vector2(0.5f, 0.5f);

        // Fill area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderGO.transform, false);
        RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0f);
        fillAreaRT.anchorMax = new Vector2(1f, 1f);
        fillAreaRT.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRT.offsetMin = new Vector2(2f, 2f);
        fillAreaRT.offsetMax = new Vector2(-2f, -2f);

        // Fill image
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRT = fill.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.pivot = new Vector2(0.5f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;

        progressBarFill = fill.AddComponent<Image>();
        progressBarFill.color = accentColor;

        // Glow overlay on the fill
        GameObject glow = new GameObject("Glow");
        glow.transform.SetParent(fill.transform, false);
        RectTransform glowRT = glow.AddComponent<RectTransform>();
        StretchFull(glowRT);
        Image glowImg = glow.AddComponent<Image>();
        glowImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.3f);
        glowImg.raycastTarget = false;

        // Wire the slider
        progressBar.fillRect = fillRT;
        progressBar.targetGraphic = progressBarFill;
    }

    // ─────────────────────────────────────────────
    //  Spinner Dots Builder
    // ─────────────────────────────────────────────

    private void BuildSpinner(Transform parent)
    {
        GameObject spinnerGO = new GameObject("Spinner");
        spinnerGO.transform.SetParent(parent, false);
        spinnerRoot = spinnerGO.AddComponent<RectTransform>();
        spinnerRoot.anchorMin = new Vector2(0.5f, 0f);
        spinnerRoot.anchorMax = new Vector2(0.5f, 0f);
        spinnerRoot.pivot = new Vector2(0.5f, 0.5f);
        spinnerRoot.sizeDelta = new Vector2(80f, 20f);
        spinnerRoot.anchoredPosition = new Vector2(0f, 80f);

        spinnerDots = new Image[SPINNER_DOT_COUNT];
        float spacing = 24f;
        float startX = -(SPINNER_DOT_COUNT - 1) * spacing * 0.5f;

        for (int i = 0; i < SPINNER_DOT_COUNT; i++)
        {
            GameObject dotGO = new GameObject($"Dot_{i}");
            dotGO.transform.SetParent(spinnerGO.transform, false);
            RectTransform dotRT = dotGO.AddComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.5f, 0.5f);
            dotRT.anchorMax = new Vector2(0.5f, 0.5f);
            dotRT.pivot = new Vector2(0.5f, 0.5f);
            dotRT.sizeDelta = new Vector2(10f, 10f);
            dotRT.anchoredPosition = new Vector2(startX + i * spacing, 0f);

            spinnerDots[i] = dotGO.AddComponent<Image>();
            spinnerDots[i].color = accentColor;
            spinnerDots[i].raycastTarget = false;
        }
    }

    // ─────────────────────────────────────────────
    //  Utility
    // ─────────────────────────────────────────────

    /// <summary>Stretches a RectTransform to fill its parent completely.</summary>
    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
