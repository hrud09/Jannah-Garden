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
    public GameObject timerHolder;

    /// <summary>The <see cref="TimerHoldingPlank"/> on <see cref="timerHolder"/>, if any — supplies the
    /// signboard text and the timer icon directly rather than searching the hierarchy for them.</summary>
    private TimerHoldingPlank timerHoldingPlank;

    /// <summary>Local offset from the root's ground position applied when pinning <see cref="timerHolder"/>
    /// to the ground — lets the signboard sit forward/aside/embedded rather than exactly at the root.
    /// Configured centrally on <see cref="ItemPlacementManager.timerHolderGroundOffset"/> rather than
    /// per-prefab, so every placed item's signboard uses the same offset. Only used as a fallback until
    /// <see cref="SetTimerAreaXZFromFenceBarrier"/> supplies a real X/Z to pin to.</summary>
    private Vector3 TimerHolderGroundOffset =>
        ItemPlacementManager.Instance != null ? ItemPlacementManager.Instance.timerHolderGroundOffset : Vector3.zero;

    // World X/Z to pin timerHolder to, taken from the FenceBarrier's timerSignBoardReferenceTransform
    // (see SetTimerAreaXZFromFenceBarrier) — set once, right before the barrier spawns, so both the
    // drop-in animation and the steady-state pin use it from the very first frame. Y always comes from
    // the root's own ground level, never from this.
    private bool _hasTimerAreaXZOverride;
    private float _timerAreaOverrideX;
    private float _timerAreaOverrideZ;

    /// <summary>
    /// Pins where <see cref="timerHolder"/>'s X/Z will settle to <paramref name="referenceTransform"/>'s
    /// current X/Z (its own Y is ignored — the signboard always sits at ground level). Called once by
    /// <see cref="ItemPlacementManager"/> right after it spawns this item's FenceBarrier, before
    /// <see cref="Initialize"/>, so the plant-in animation targets the right spot from its first frame.
    /// Captured as plain floats rather than keeping the Transform live, since the FenceBarrier is pooled
    /// and despawns (and can be reused elsewhere) well before this item finishes growing.
    /// </summary>
    public void SetTimerAreaXZFromFenceBarrier(Transform referenceTransform)
    {
        if (referenceTransform == null) return;

        _hasTimerAreaXZOverride = true;
        _timerAreaOverrideX = referenceTransform.position.x;
        _timerAreaOverrideZ = referenceTransform.position.z;
    }

    /// <summary>How far along the root-to-signboard line <see cref="TimerHolderGroundPosition"/> settles
    /// once the placement timer has finished (<see cref="IsFullyPlaced"/>), where 1 is the raw
    /// FenceBarrier/offset spot and 0 is right on top of the root. Halving it pulls the plank in to sit
    /// 50% closer to the placed item than that raw spot. Before the timer is up the plank stays at the
    /// raw spot (fraction 1) — see <see cref="TimerHolderGroundPosition"/>.</summary>
    private const float TimerHolderDistanceFractionWhenComplete = 0.5f;

    /// <summary>Where <see cref="timerHolder"/> should rest: the FenceBarrier-supplied X/Z when available,
    /// otherwise the legacy offset-based spot. While the placement timer is still running this is the raw
    /// spot; once <see cref="IsFullyPlaced"/> it is pulled in to <see cref="TimerHolderDistanceFractionWhenComplete"/>
    /// of the way from the root. Y is always re-sampled from the actual terrain/grid height at that X/Z
    /// (not just copied from the root) so the signboard stays grounded even when its X/Z sits off to the
    /// side of the root on sloped ground.</summary>
    private Vector3 TimerHolderGroundPosition
    {
        get
        {
            Vector3 root = transform.position;
            Vector3 p;
            if (_hasTimerAreaXZOverride)
            {
                p = root;
                p.x = _timerAreaOverrideX;
                p.z = _timerAreaOverrideZ;
            }
            else
            {
                p = root + transform.rotation * TimerHolderGroundOffset;
            }

            float fraction = IsFullyPlaced ? TimerHolderDistanceFractionWhenComplete : 1f;
            p.x = Mathf.Lerp(root.x, p.x, fraction);
            p.z = Mathf.Lerp(root.z, p.z, fraction);

            p.y = SampleGroundHeight(p);
            return p;
        }
    }

    /// <summary>Horizontal direction the signboard should face: straight out from the root/item position
    /// towards wherever <see cref="TimerHolderGroundPosition"/> sits, so it always reads as planted facing
    /// away from the item rather than towards it. Falls back to the item's own forward when the signboard
    /// sits right on top of the root (no meaningful direction to derive).</summary>
    private Vector3 TimerHolderOutwardDirection
    {
        get
        {
            Vector3 groundPos = TimerHolderGroundPosition;
            Vector3 dir = new Vector3(groundPos.x - transform.position.x, 0f, groundPos.z - transform.position.z);

            return dir.sqrMagnitude > 0.0001f ? -dir.normalized : -transform.forward;
        }
    }

    private static float SampleGroundHeight(Vector3 world)
    {
        if (GardenGrid.Instance != null && GardenGrid.Instance.IsReady)
        {
            return GardenGrid.Instance.SampleHeight(world);
        }

        Terrain terrain = Terrain.activeTerrain;
        return terrain != null ? terrain.SampleHeight(world) + terrain.transform.position.y : world.y;
    }

    [Header("Timer Area Planting Animation")]
    /// <summary>How high above the ground the signboard starts its drop.</summary>
    public float timerPlantDropHeight = 4f;
    /// <summary>Local-space horizontal offset it swings in from, on top of the drop height, so the
    /// descent reads as a diagonal swoop rather than a straight vertical fall.</summary>
    public float timerPlantSwingDistance = 0.6f;
    /// <summary>How long the fall itself takes, before the impact squash.</summary>
    public float timerPlantFallDuration = 0.35f;
    /// <summary>How long the ground-impact squash/rebound takes once it lands.</summary>
    public float timerPlantImpactDuration = 0.18f;
    /// <summary>How hard it squashes on impact, as a fraction of its scale.</summary>
    public float timerPlantImpactStrength = 0.35f;

    private Coroutine _timerPlantRoutine;
    private bool _isPlantingTimerHolder;

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

    /// <summary>Fired exactly once, the moment <see cref="remainingDuration"/> reaches zero while tracking
    /// (never for an item that was already fully grown when placed - see <see cref="alreadyCompletedOnStart"/>).
    /// Used by <see cref="ItemPlacementManager"/> to take down the fence barrier around a freshly-placed item.</summary>
    public event System.Action OnFullyPlaced;

    private Vector3 initialScale;
    public Vector3 InitialScale => initialScale;

    /// <summary>
    /// The block of lattice cells this item is standing on, as registered with <see cref="GardenGrid"/>.
    /// Runtime-only and deliberately not serialized: the save file stores world positions, and the area
    /// is re-derived from those on load, so a stale value here could never outlive a session.
    /// </summary>
    [System.NonSerialized]
    public RectInt gridArea;

    /// <summary>True once <see cref="gridArea"/> has been filled in for this placement.</summary>
    [System.NonSerialized]
    public bool hasGridArea;

    /// <summary>Records the cells this item claimed, so a relocate can hand exactly those back.</summary>
    public void SetGridArea(RectInt area)
    {
        gridArea = area;
        hasGridArea = true;
    }

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
        CacheInstancedMaterials();
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

        if (timerHolder)
        {
            timerHoldingPlank = timerHolder.GetComponent<TimerHoldingPlank>();

            // The signboard/timer must not be visible on the ghost preview - it only appears once
            // the item is actually placed (Start/Initialize turn it back on via ApplyStateVisuals).
            StopPlantingRoutine();
            timerHolder.SetActive(false);
        }
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
    /// Measured by <see cref="GfxBounds"/> in root-local space, which folds in the GFX's own rotation
    /// (the models carry a -90° X rotation) without any special-casing. The same measurement backs
    /// <see cref="ItemFootprint"/>, so how far an item sinks and how many cells it claims can never
    /// drift apart.
    ///
    /// Safe to call again if the GFX is swapped or re-authored.
    /// </summary>
    public void MeasureGfxGroundOffset()
    {
        canAlignGfx = false;
        gfxBottomDrop = 0f;

        if (!alignGfxToGround || itemGFX == null) return;

        initialGfxLocalPos = itemGFX.localPosition;

        if (!GfxBounds.TryGetLocalBounds(transform, itemGFX, alignUsingRenderersIfNoCollider, out Bounds local)) return;

        float bottomY = local.min.y;

        // How far the geometry reaches below the GFX origin, at the authored scale.
        // Positive means it hangs below and needs lifting.
        gfxBottomDrop = initialGfxLocalPos.y - bottomY;
        canAlignGfx = true;
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
        // Awake (which resolves timerHoldingPlank) has already run by the time this is called on a
        // freshly instantiated object, but fall back to a lookup just in case timerHolder was assigned
        // after Awake.
        if (timerHoldingPlank == null && timerHolder != null)
        {
            timerHoldingPlank = timerHolder.GetComponent<TimerHoldingPlank>();
        }

        if (timerHoldingPlank != null && timerHoldingPlank.signBoardText != null)
        {
            int minutes = Mathf.FloorToInt(duration / 60f);
            int seconds = Mathf.FloorToInt(duration % 60f);
            timerHoldingPlank.signBoardText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    private void Start()
    {
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
                StopPlantingRoutine();
                timerHolder.SetActive(false);
            }

            UpdateSaturation(1f, -1);

            SetScaleMultiplier(1f);
        }
        else if (isTracking)
        {
            if (timerHolder != null)
            {
                // A pooled instance may still have its icon switched off from a previous item that
                // finished growing - a fresh countdown always starts with it back on.
                if (timerHoldingPlank != null && timerHoldingPlank.timerIcon != null)
                {
                    timerHoldingPlank.timerIcon.SetActive(true);
                }

                PlantTimerHolder();
            }

            float timeRatio = placementDuration > 0f
                ? Mathf.Clamp01(1f - (remainingDuration / placementDuration))
                : 0f;

            UpdateSaturation(0f, -1);
            SetScaleMultiplier(Mathf.Lerp(0.2f, 1f, timeRatio));
        }
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

            // Once fully grown, the signboard stays up but switches from a countdown to naming the
            // item, and the clock icon goes away since there's nothing left to count down.
            if (timerHoldingPlank != null)
            {
                if (timerHoldingPlank.signBoardText != null)
                {
                    timerHoldingPlank.signBoardText.text = prefabName;
                }

                if (timerHoldingPlank.timerIcon != null)
                {
                    timerHoldingPlank.timerIcon.SetActive(false);
                }
            }

            OnFullyPlaced?.Invoke();
        }
        else
        {
            if (timerHoldingPlank != null && timerHoldingPlank.signBoardText != null)
            {
                int minutes = Mathf.FloorToInt(remainingDuration / 60f);
                int seconds = Mathf.FloorToInt(remainingDuration % 60f);
                timerHoldingPlank.signBoardText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
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

    /// <summary>
    /// The timer holder carries a physical signboard/plank, so unlike the growing model it must never
    /// lift off the ground — pin its world Y to the root's (which sits at ground level, see
    /// <see cref="alignGfxToGround"/>) every frame regardless of what parent or growth animation it
    /// inherits from.
    /// </summary>
    private void LateUpdate()
    {
        // While the plant-in animation owns the position, let it drive; otherwise keep the
        // signboard pinned to ground level every frame regardless of what parent or growth
        // animation it inherits from.
        if (timerHolder == null || _isPlantingTimerHolder) return;

        timerHolder.transform.position = TimerHolderGroundPosition;
        timerHolder.transform.rotation = Quaternion.LookRotation(TimerHolderOutwardDirection, Vector3.up);
    }

    /// <summary>(Re)starts the drop-and-plant animation for <see cref="timerHolder"/>, activating it first
    /// if needed. Called every time tracking begins — a fresh placement or a relocate drop — so the
    /// signboard always slams into the ground rather than just popping into view.</summary>
    private void PlantTimerHolder()
    {
        if (timerHolder == null) return;

        StopPlantingRoutine();
        timerHolder.SetActive(true);
        _timerPlantRoutine = StartCoroutine(AnimateTimerHolderPlanting());
    }

    private void StopPlantingRoutine()
    {
        if (_timerPlantRoutine != null)
        {
            StopCoroutine(_timerPlantRoutine);
            _timerPlantRoutine = null;
        }
        _isPlantingTimerHolder = false;
    }

    /// <summary>
    /// Drops the signboard in from above along a diagonal, accelerating path — like it's been thrown
    /// down and driven into the ground — then squashes and rebounds on impact for the "planted" feel.
    /// </summary>
    private System.Collections.IEnumerator AnimateTimerHolderPlanting()
    {
        _isPlantingTimerHolder = true;

        Transform t = timerHolder.transform;
        Vector3 baseLocalScale = t.localScale;

        // Face outward from the item for the whole drop, not just once it settles.
        t.rotation = Quaternion.LookRotation(TimerHolderOutwardDirection, Vector3.up);

        // The swing direction comes from the item's own facing so the swoop always reads as
        // "in front of / to the side of" the item rather than a fixed world axis.
        Vector3 swingDir = transform.rotation * Vector3.back;

        float elapsed = 0f;
        while (elapsed < timerPlantFallDuration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / timerPlantFallDuration);

            Vector3 groundPos = TimerHolderGroundPosition;

            // Vertical: eases in hard (u^3) so it lingers up high then slams down at the end.
            float fallT = u * u * u;
            float height = Mathf.Lerp(timerPlantDropHeight, 0f, fallT);

            // Horizontal: settles out smoothly, fading to zero before the fall finishes so the
            // final motion is a straight downward slam rather than a lateral drift.
            float swingT = 1f - Mathf.Pow(1f - u, 2f);
            float swingAmount = Mathf.Lerp(timerPlantSwingDistance, 0f, swingT);

            t.position = groundPos + Vector3.up * height + swingDir * swingAmount;

            yield return null;
        }

        Vector3 finalGroundPos = TimerHolderGroundPosition;
        t.position = finalGroundPos;

        // Impact squash: flatten on the way down, rebound past neutral, then settle - the stake
        // being forcefully driven into the ground.
        elapsed = 0f;
        while (elapsed < timerPlantImpactDuration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / timerPlantImpactDuration);
            float squash = Mathf.Sin(u * Mathf.PI) * timerPlantImpactStrength * (1f - u);

            t.localScale = new Vector3(
                baseLocalScale.x * (1f + squash),
                baseLocalScale.y * (1f - squash * 1.5f),
                baseLocalScale.z * (1f + squash));
            t.position = finalGroundPos;

            yield return null;
        }

        t.localScale = baseLocalScale;
        t.position = finalGroundPos;

        _isPlantingTimerHolder = false;
        _timerPlantRoutine = null;
    }

    /// <summary>
    /// Per-renderer instanced material arrays, indexed the same as <see cref="itemRenderers"/>.
    /// Populated once in <see cref="Awake"/>: <c>Renderer.materials</c> allocates a new array
    /// (and instantiates materials) on every call, and this is read every frame while a placed
    /// item's saturation/scale animation is ticking, so the cached copy avoids re-allocating.
    /// </summary>
    private Material[][] _cachedMaterials;

    private void CacheInstancedMaterials()
    {
        if (itemRenderers == null)
        {
            _cachedMaterials = null;
            return;
        }

        _cachedMaterials = new Material[itemRenderers.Length][];
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            _cachedMaterials[i] = itemRenderers[i] != null ? itemRenderers[i].materials : null;
        }
    }

    public void UpdateSaturation(float saturationValue, int materialIndex = -1)
    {
        if (itemRenderers == null || itemRenderers.Length == 0) return;
        if (_cachedMaterials == null || _cachedMaterials.Length != itemRenderers.Length) CacheInstancedMaterials();

        for (int r = 0; r < itemRenderers.Length; r++)
        {
            Material[] mats = _cachedMaterials[r];
            if (mats == null) continue;

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
        if (_cachedMaterials == null || _cachedMaterials.Length != itemRenderers.Length) CacheInstancedMaterials();

        for (int r = 0; r < itemRenderers.Length; r++)
        {
            Material[] mats = _cachedMaterials[r];
            if (mats == null) continue;

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
