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

    [Header("Ground Alignment")]
    /// <summary>
    /// Lifts <see cref="itemGFX"/> so the bottom of its colliders rests on the root's Y plane.
    /// The root is dropped exactly onto the terrain at placement time, so root Y is ground level —
    /// without this, any model whose geometry hangs below its pivot sinks into the ground.
    /// </summary>
    public bool alignGfxToGround = true;

    /// <summary>Falls back to renderer bounds when the GFX has no colliders to measure.</summary>
    public bool alignUsingRenderersIfNoCollider = true;

    /// <summary>
    /// How far the geometry's lowest point sits below the GFX origin, in root-local units, measured
    /// at the GFX's authored scale. Scales linearly with the growth multiplier.
    /// </summary>
    private float gfxBottomDrop;
    private Vector3 initialGfxLocalPos;
    private bool canAlignGfx;

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

        // Must run while the GFX is still at its authored transform, before any scaling is applied.
        MeasureGfxGroundOffset();
        SetScaleMultiplier(1f);

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
    ///
    /// When ground alignment is active this also re-lifts the GFX, because the drop below the pivot
    /// scales with the model: at multiplier <c>m</c> the geometry hangs <c>gfxBottomDrop * m</c> below
    /// the GFX origin. Correcting by exactly that keeps the model's underside planted on the ground
    /// through the whole growth animation, so it grows up out of the ground rather than hovering.
    /// </summary>
    public void SetScaleMultiplier(float multiplier)
    {
        if (itemGFX != null)
        {
            itemGFX.localScale = initialScale * multiplier;

            if (canAlignGfx)
            {
                // gfxBottomDrop is measured relative to the authored origin, so it already accounts
                // for any authored Y — this replaces that Y rather than adding to it.
                Vector3 p = initialGfxLocalPos;
                p.y = gfxBottomDrop * multiplier;
                itemGFX.localPosition = p;
            }
        }
        else
        {
            transform.localScale = initialScale * multiplier;
        }
    }

    /// <summary>
    /// Measures how far the GFX's collision geometry hangs below the root's Y plane, so
    /// <see cref="SetScaleMultiplier"/> can lift it back up.
    ///
    /// Bounds are gathered in root-local space, which folds in the GFX's own rotation (the models
    /// carry a -90° X rotation) without any special-casing. Colliders are measured from their local
    /// definitions rather than <see cref="Collider.bounds"/>, since the latter is a world-space AABB
    /// that would inflate once the root is rotated at placement time.
    ///
    /// Safe to call again if the GFX is swapped or re-authored.
    /// </summary>
    public void MeasureGfxGroundOffset()
    {
        canAlignGfx = false;
        gfxBottomDrop = 0f;

        if (!alignGfxToGround || itemGFX == null) return;

        initialGfxLocalPos = itemGFX.localPosition;

        if (!TryGetGfxBottomInRootSpace(out float bottomY)) return;

        // How far the geometry reaches below the GFX origin, at the authored scale.
        // Positive means it hangs below and needs lifting.
        gfxBottomDrop = initialGfxLocalPos.y - bottomY;
        canAlignGfx = true;
    }

    /// <summary>Lowest point of the GFX's collision (or renderer) geometry, in root-local space.</summary>
    private bool TryGetGfxBottomInRootSpace(out float bottomY)
    {
        bottomY = 0f;
        bool any = false;
        Matrix4x4 toRoot = transform.worldToLocalMatrix;

        foreach (Collider col in itemGFX.GetComponentsInChildren<Collider>(true))
        {
            // Triggers are gameplay volumes, not the model's physical footprint — measuring them
            // would lift the item off the ground by however far the volume overhangs.
            if (col.isTrigger) continue;
            if (!TryGetColliderLocalBounds(col, out Bounds local)) continue;
            AccumulateMinY(local, toRoot * col.transform.localToWorldMatrix, ref bottomY, ref any);
        }

        if (!any && alignUsingRenderersIfNoCollider)
        {
            foreach (MeshFilter mf in itemGFX.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.TryGetComponent<Renderer>(out _)) continue;
                AccumulateMinY(mf.sharedMesh.bounds, toRoot * mf.transform.localToWorldMatrix, ref bottomY, ref any);
            }

            foreach (SkinnedMeshRenderer sm in itemGFX.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sm.sharedMesh == null) continue;
                AccumulateMinY(sm.sharedMesh.bounds, toRoot * sm.transform.localToWorldMatrix, ref bottomY, ref any);
            }
        }

        return any;
    }

    /// <summary>Collider geometry expressed in its own transform's local space.</summary>
    private static bool TryGetColliderLocalBounds(Collider col, out Bounds local)
    {
        switch (col)
        {
            case MeshCollider mc when mc.sharedMesh != null:
                local = mc.sharedMesh.bounds;
                return true;

            case BoxCollider bc:
                local = new Bounds(bc.center, bc.size);
                return true;

            case SphereCollider sc:
                local = new Bounds(sc.center, Vector3.one * (sc.radius * 2f));
                return true;

            case CapsuleCollider cc:
                Vector3 size = Vector3.one * (cc.radius * 2f);
                // direction: 0 = X, 1 = Y, 2 = Z
                if (cc.direction == 0) size.x = Mathf.Max(cc.height, cc.radius * 2f);
                else if (cc.direction == 1) size.y = Mathf.Max(cc.height, cc.radius * 2f);
                else size.z = Mathf.Max(cc.height, cc.radius * 2f);
                local = new Bounds(cc.center, size);
                return true;

            default:
                local = default;
                return false;
        }
    }

    /// <summary>Transforms all 8 corners of <paramref name="local"/> and tracks the lowest Y.</summary>
    private static void AccumulateMinY(Bounds local, Matrix4x4 m, ref float minY, ref bool any)
    {
        Vector3 c = local.center, e = local.extents;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + (((i & 1) == 0) ? -e.x : e.x),
                c.y + (((i & 2) == 0) ? -e.y : e.y),
                c.z + (((i & 4) == 0) ? -e.z : e.z));

            float y = m.MultiplyPoint3x4(corner).y;

            if (!any) { minY = y; any = true; }
            else if (y < minY) minY = y;
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
