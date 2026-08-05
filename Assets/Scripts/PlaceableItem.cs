using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Where a placed item came from, so returning it to the Asset Store knows what to give back:
/// Noor Coins for a shop purchase, a stack of one for a treasure box reward.
/// </summary>
public enum PlacedItemSource
{
    /// <summary>Placed before source tracking existed, or the source asset is gone. Falls back to a prefab-name lookup.</summary>
    Unknown = 0,
    ShopItem = 1,
    InventoryItem = 2
}

public class PlaceableItem : MonoBehaviour
{
    [Header("Placement Time Settings")]
    public float placementDuration = 60f; // Total placement duration in seconds
    public float remainingDuration = 60f; // Remaining time in seconds

    [HideInInspector]
    public string uniqueId;
    [HideInInspector]
    public string prefabName;

    /// <summary>Which shop/inventory asset this was placed from. Used by the return-to-store flow.</summary>
    [HideInInspector]
    public PlacedItemSource sourceKind = PlacedItemSource.Unknown;

    /// <summary>The <c>itemID</c> of the asset named by <see cref="sourceKind"/>, when it had one.</summary>
    [HideInInspector]
    public string sourceItemId;

    [Header("UI References (Optional)")]
    private TMP_Text timerText;
    public GameObject timerHolder;

    [Header("Renderers")]
    public Renderer[] itemRenderers;

    [Header("Tree Settings")]
    public bool isTree = false;

    [Header("Building Settings")]
    public bool isBuilding = false;

    [Header("GFX Reference (Optional)")]
    public Transform itemGFX;

    private bool isTracking = false;
    private bool alreadyCompletedOnStart = false;
    private bool hasStarted = false;
    private bool isHighlighted = false;

    /// <summary>True once the placement timer has run out and the item is fully grown.</summary>
    public bool IsFullyPlaced => remainingDuration <= 0f;

    private Vector3 initialScale;
    public Vector3 InitialScale => initialScale;

    private void Awake()
    {
        // Automatically add collider if not found
        if (GetComponent<Collider>() == null)
        {
            gameObject.AddComponent<BoxCollider>();
        }

        // Automatically populate itemRenderers if empty
        if (itemRenderers == null || itemRenderers.Length == 0)
        {
            itemRenderers = GetComponentsInChildren<Renderer>(true);
        }
        // Auto-detect tree or building based on name if not set
        if (!isTree && !isBuilding)
        {
            string lowerName = gameObject.name.ToLower();
            if (lowerName.Contains("tree"))
            {
                isTree = true;
            }
            else if (lowerName.Contains("building") || lowerName.Contains("house"))
            {
                isBuilding = true;
            }
        }

        // Capture the prefab's original scale before any tracking logic modifies it
        initialScale = (itemGFX != null) ? itemGFX.localScale : transform.localScale;

        if(timerHolder) timerText = timerHolder.GetComponentInChildren<TMP_Text>();
    }

    private void OnEnable()
    {
        if (RuntimeEnvironmentGenerator.Instance != null)
        {
            RuntimeEnvironmentGenerator.Instance.RegisterObject(gameObject);
        }
    }

    private void OnDisable()
    {
        // Pooled objects come back with whatever state they left with — never with a stale outline.
        SetHighlight(false);

        if (RuntimeEnvironmentGenerator.Instance != null && !RuntimeEnvironmentGenerator.Instance.IsDeactivatedByGenerator(gameObject))
        {
            RuntimeEnvironmentGenerator.Instance.UnregisterObject(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (RuntimeEnvironmentGenerator.Instance != null)
        {
            RuntimeEnvironmentGenerator.Instance.UnregisterObject(gameObject);
        }
    }

    /// <summary>
    /// Helper method to set the scale multiplier on the itemGFX if assigned, otherwise the root transform.
    /// </summary>
    public void SetScaleMultiplier(float multiplier)
    {
        if (itemGFX != null)
        {
            itemGFX.localScale = initialScale * multiplier;
        }
        else
        {
            transform.localScale = initialScale * multiplier;
        }
    }

    /// <summary>
    /// Initializes tracking values for this item.
    ///
    /// Safe to call more than once on the same instance: relocating an item respawns it from the object
    /// pool, which never re-runs <see cref="Start"/>, so the visual state is applied here instead once
    /// the object has started.
    /// </summary>
    public void Initialize(string id, float totalDur, float remainingDur)
    {
        this.uniqueId = id;
        this.placementDuration = totalDur;
        this.remainingDuration = Mathf.Max(0f, remainingDur);
        this.alreadyCompletedOnStart = this.remainingDuration <= 0f;
        this.isTracking = !alreadyCompletedOnStart;

        if (hasStarted)
        {
            ApplyStateVisuals();
        }
    }

    /// <summary>Records which shop or inventory asset this item was placed from.</summary>
    public void SetSource(PlacedItemSource kind, string itemId)
    {
        sourceKind = kind;
        sourceItemId = itemId;
    }

    /// <summary>
    /// Writes the formatted duration to the timer label without starting the countdown.
    /// Call this during the preview / pre-confirmation phase so the player can see
    /// how long the item will take before they commit to placing it.
    /// </summary>
    public void PreviewTimer(float duration)
    {
        // The floating timer UI is created in Start(), which hasn't run yet
        // when this is called on a freshly instantiated object, so we need to
        // bootstrap the text reference ourselves if it's missing.
        if (timerText == null)
        {
            timerText = GetComponentInChildren<TMPro.TMP_Text>();
        }

        if (timerText == null)
        {
            CreateFloatingTimerUI();
        }

        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(duration / 60f);
            int seconds = Mathf.FloorToInt(duration % 60f);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    private void Start()
    {
        // Make the timerHolder face the camera
        if (timerHolder != null && timerHolder.GetComponent<Billboard>() == null)
        {
            timerHolder.AddComponent<Billboard>();
        }

        // Auto-detect a Text Mesh Pro text field in children if not assigned
        if (timerText == null)
        {
            timerText = GetComponentInChildren<TMP_Text>();
        }

        // Dynamically create a floating world-space billboard timer if missing
        if (timerText == null)
        {
            CreateFloatingTimerUI();
        }

        hasStarted = true;
        ApplyStateVisuals();
    }

    /// <summary>
    /// Brings the model's saturation, scale and timer label in line with <see cref="remainingDuration"/>.
    /// Called on Start and again whenever the item is re-initialized (a relocation, a state pushed down
    /// from the cloud) so a pooled object never keeps the previous item's look.
    /// </summary>
    private void ApplyStateVisuals()
    {
        // If it was already completed on start/load, disable the timer holder immediately
        if (alreadyCompletedOnStart)
        {
            isTracking = false;
            remainingDuration = 0f;
            if (timerHolder != null)
            {
                timerHolder.SetActive(false);
            }

            UpdateSaturation(1f, -1);

            SetScaleMultiplier(1f);
        }
        else if (isTracking)
        {
            if (timerHolder != null)
            {
                timerHolder.SetActive(true);
            }

            float timeRatio = placementDuration > 0f
                ? Mathf.Clamp01(1f - (remainingDuration / placementDuration))
                : 0f;

            UpdateSaturation(0f, -1);
            SetScaleMultiplier(Mathf.Lerp(0.2f, 1f, timeRatio));
        }
    }

    private void CreateFloatingTimerUI()
    {
        // Create Canvas container
        GameObject canvasGo = new GameObject("FloatingTimerCanvas");
        canvasGo.transform.SetParent(this.transform);
        canvasGo.transform.localPosition = new Vector3(0, 3.5f, 0); // Positioned above the model
        canvasGo.transform.localRotation = Quaternion.identity;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3, 1);

        // Create TMP Text container
        GameObject textGo = new GameObject("TimerText");
        textGo.transform.SetParent(canvasGo.transform);
        textGo.transform.localPosition = Vector3.zero;
        textGo.transform.localRotation = Quaternion.identity;

        timerText = textGo.AddComponent<TextMeshPro>();
        timerText.fontSize = 4;
        timerText.alignment = TextAlignmentOptions.Center;
        timerText.color = Color.yellow;

        // Apply a Billboard effect to rotate towards the camera
        textGo.AddComponent<Billboard>();

        // Store the canvas in timerHolder so it can be disabled later
        timerHolder = canvasGo;
    }

    private void Update()
    {
        if (!isTracking) return;

        remainingDuration -= Time.deltaTime;

        if (remainingDuration <= 0f)
        {
            remainingDuration = 0f;
            isTracking = false; // Stop tracking so we don't repeatedly trigger this

            UpdateSaturation(1f, -1);
            SetScaleMultiplier(1f);

            if (timerText != null)
            {
                timerText.text = "Completed!";
            }

            // Start coroutine to hide the timer holder after 5 seconds
            StartCoroutine(DisableTimerHolderAfterDelay(5f));
        }
        else
        {
            if (timerText != null)
            {
                int minutes = Mathf.FloorToInt(remainingDuration / 60f);
                int seconds = Mathf.FloorToInt(remainingDuration % 60f);
                timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }

            // Update stepped saturation
            float timeRatio = 1f - (remainingDuration / placementDuration);
            float currentSat = 1f;
            if (timeRatio < 0.25f) currentSat = 0f;
            else if (timeRatio < 0.5f) currentSat = 0.25f;
            else if (timeRatio < 0.75f) currentSat = 0.5f;
            else if (timeRatio < 1f) currentSat = 0.75f;

            if (isTree || isBuilding)
            {
                UpdateSaturation(currentSat, 0); // Material 0 (trunk / structure) follows normal
                if (remainingDuration <= 10f)
                {
                    float detailSat = 1f - (remainingDuration / 10f);
                    UpdateSaturationForIndices(detailSat, 1); // Material 1+ (leaves / roof / details) fades in
                }
                else
                {
                    UpdateSaturationForIndices(0f, 1);
                }
            }
            else
            {
                UpdateSaturation(currentSat, -1);
            }

            // Update smooth gradual scale from 0.2x to 1.0x
            SetScaleMultiplier(Mathf.Lerp(0.2f, 1.0f, timeRatio));
        }
    }

    private System.Collections.IEnumerator DisableTimerHolderAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (timerHolder != null)
        {
            timerHolder.SetActive(false);
        }
    }

    public void UpdateSaturation(float saturationValue, int materialIndex = -1)
    {
        if (itemRenderers == null || itemRenderers.Length == 0) return;

        foreach (var renderer in itemRenderers)
        {
            if (renderer == null) continue;
            
            // Using .materials creates instances of the materials if not already created,
            // which is safe here so we don't modify shared materials for other objects.
            Material[] mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (materialIndex != -1 && i != materialIndex) continue;

                Material mat = mats[i];
                if (mat != null && mat.HasProperty("_Saturation"))
                {
                    mat.EnableKeyword("BASE_SATURATION");
                    mat.SetFloat("_Saturation", saturationValue);
                }
            }
        }
    }

    /// <summary>
    /// Draws (or clears) the selection outline, so the player can see which placed item the relocate /
    /// return options will act on. Mirrors <see cref="TreasureBox.SetOutline"/> — same OmniShade URP
    /// properties, same pass names.
    /// </summary>
    public void SetHighlight(bool enable)
    {
        if (isHighlighted == enable) return;
        isHighlighted = enable;

        if (itemRenderers == null) return;

        foreach (var renderer in itemRenderers)
        {
            if (renderer == null) continue;

            foreach (Material mat in renderer.materials)
            {
                if (mat == null || !mat.HasProperty("_Outline")) continue;

                mat.SetFloat("_Outline", enable ? 1f : 0f);

                // "SRPDefaultUnlit" is the outline pass under URP; "Always" under Built-in.
                string outlinePassName = mat.shader.name.Contains("URP") ? "SRPDefaultUnlit" : "Always";
                mat.SetShaderPassEnabled(outlinePassName, enable);

                if (enable)
                {
                    mat.EnableKeyword("OUTLINE");
                    mat.DisableKeyword("OUTLINE_PASS_DISABLED");
                }
                else
                {
                    mat.DisableKeyword("OUTLINE");
                    mat.EnableKeyword("OUTLINE_PASS_DISABLED");
                }
            }
        }
    }

    /// <summary>
    /// Updates saturation for all materials starting from a specific index.
    /// Useful for desaturating/saturating roof/leaves/details (index >= 1) dynamically.
    /// </summary>
    public void UpdateSaturationForIndices(float saturationValue, int startIndex)
    {
        if (itemRenderers == null || itemRenderers.Length == 0) return;

        foreach (var renderer in itemRenderers)
        {
            if (renderer == null) continue;
            
            Material[] mats = renderer.materials;
            for (int i = startIndex; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat != null && mat.HasProperty("_Saturation"))
                {
                    mat.EnableKeyword("BASE_SATURATION");
                    mat.SetFloat("_Saturation", saturationValue);
                }
            }
        }
    }
}

/// <summary>
/// Simple helper behavior to rotate text towards the camera.
/// </summary>
public class Billboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            transform.LookAt(transform.position + mainCam.transform.rotation * Vector3.forward,
                             mainCam.transform.rotation * Vector3.up);
        }
    }
}
