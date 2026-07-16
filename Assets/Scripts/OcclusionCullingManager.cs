using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Runtime occlusion culling manager for the Outer Garden scene.
/// Performs frustum + distance-based culling on registered renderers,
/// batching checks across multiple frames to avoid CPU spikes on mobile.
/// 
/// Works alongside RuntimeEnvironmentGenerator:
/// - RuntimeEnvironmentGenerator handles SetActive (full activation/deactivation by distance)
/// - OcclusionCullingManager handles Renderer.enabled (visual culling by frustum + distance tiers)
/// </summary>
public class OcclusionCullingManager : MonoBehaviour
{
    public static OcclusionCullingManager Instance { get; private set; }

    // ─── Culling Mode ────────────────────────────────────────────────
    public enum CullingMode
    {
        /// <summary>Only this occlusion system is active (RuntimeEnvironmentGenerator ignored).</summary>
        OcclusionOnly,
        /// <summary>Only RuntimeEnvironmentGenerator distance culling is active.</summary>
        DistanceOnly,
        /// <summary>Both systems work together. Occlusion handles renderers; distance handles SetActive.</summary>
        Combined
    }

    [Header("Culling Mode")]
    [Tooltip("How this system interacts with RuntimeEnvironmentGenerator.")]
    public CullingMode cullingMode = CullingMode.Combined;

    // ─── Distance Tiers ──────────────────────────────────────────────
    [Header("Distance Tiers")]
    [Tooltip("Objects closer than this are never frustum-culled (always fully visible).")]
    public float nearDistance = 15f;
    [Tooltip("Between near and mid, small objects (low priority) may be culled when outside frustum.")]
    public float midDistance = 40f;
    [Tooltip("Beyond this distance, everything except 'Never Cull' objects is culled.")]
    public float farDistance = 60f;
    [Tooltip("Beyond this distance, shadows are disabled even for visible objects.")]
    public float shadowDistance = 30f;

    // ─── Performance ─────────────────────────────────────────────────
    [Header("Performance")]
    [Tooltip("How many frames to wait between full culling passes (1 = every frame, 2 = every other frame).")]
    [Range(1, 6)]
    public int checkEveryNFrames = 2;
    [Tooltip("Maximum number of objects to process per frame to spread the load.")]
    [Range(16, 256)]
    public int batchSize = 64;

    // ─── Filters ─────────────────────────────────────────────────────
    [Header("Filters")]
    [Tooltip("Only cull objects on these layers. Leave 'Everything' to cull all layers.")]
    public LayerMask cullableLayers = ~0;

    // ─── Camera ──────────────────────────────────────────────────────
    [Header("Camera (Auto-detected if empty)")]
    [Tooltip("The camera used for frustum calculations. Auto-detects Camera.main if null.")]
    public Camera cullingCamera;

    // ─── Debug ───────────────────────────────────────────────────────
    [Header("Debug")]
    [Tooltip("Draw gizmo spheres showing culling tier distances in Scene view.")]
    public bool showDebugGizmos = true;
    [Tooltip("Log culling statistics to the console each pass.")]
    public bool logStats = false;

    // ─── Runtime State ───────────────────────────────────────────────
    /// <summary>All registered culling entries.</summary>
    private readonly List<CullEntry> _entries = new List<CullEntry>();
    private readonly HashSet<Renderer> _registeredRenderers = new HashSet<Renderer>();

    /// <summary>Index into _entries for the next batch to process.</summary>
    private int _batchCursor = 0;

    /// <summary>Cached frustum planes, recalculated each culling frame.</summary>
    private Plane[] _frustumPlanes = new Plane[6];

    /// <summary>Frame counter for throttling.</summary>
    private int _frameCounter = 0;

    // ─── Stats ───────────────────────────────────────────────────────
    private int _lastVisibleCount;
    private int _lastCulledCount;
    private int _lastShadowOnlyCount;

    /// <summary>Number of objects visible after last culling pass.</summary>
    public int VisibleCount => _lastVisibleCount;
    /// <summary>Number of objects culled after last culling pass.</summary>
    public int CulledCount => _lastCulledCount;
    /// <summary>Number of objects in shadow-only mode after last culling pass.</summary>
    public int ShadowOnlyCount => _lastShadowOnlyCount;
    /// <summary>Total registered objects.</summary>
    public int TotalRegistered => _entries.Count;
    /// <summary>Whether the occlusion system is actively culling.</summary>
    public bool IsOcclusionCullingActive => cullingMode != CullingMode.DistanceOnly && enabled;

    // ─── Internal Types ──────────────────────────────────────────────
    private class CullEntry
    {
        public Renderer renderer;
        public Transform transform;
        public Bounds worldBounds;
        public OcclusionCullingZone zone; // nullable — per-object overrides
        public bool wasCulled;
        public bool wasShadowOnly;
        public ShadowCastingMode originalShadowMode;
    }

    private enum ShadowCastingMode
    {
        Off = 0,
        On = 1,
        TwoSided = 2,
        ShadowsOnly = 3
    }

    // ─── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (cullingCamera == null)
        {
            cullingCamera = Camera.main;
        }

        if (cullingCamera == null)
        {
            Debug.LogWarning("[OcclusionCullingManager] No camera found. Culling disabled until a camera is available.");
        }
    }

    private void LateUpdate()
    {
        if (cullingMode == CullingMode.DistanceOnly) return;
        if (_entries.Count == 0) return;

        // Throttle: only run on every N-th frame
        _frameCounter++;
        if (_frameCounter % checkEveryNFrames != 0) return;

        // Auto-detect camera if lost
        if (cullingCamera == null)
        {
            cullingCamera = Camera.main;
            if (cullingCamera == null) return;
        }

        // Recalculate frustum planes once per culling frame
        GeometryUtility.CalculateFrustumPlanes(cullingCamera, _frustumPlanes);
        Vector3 camPos = cullingCamera.transform.position;

        // Process a batch of entries
        int processed = 0;
        int visible = 0, culled = 0, shadowOnly = 0;

        // If the batch cursor wrapped, reset stats
        if (_batchCursor == 0)
        {
            _lastVisibleCount = 0;
            _lastCulledCount = 0;
            _lastShadowOnlyCount = 0;
        }

        while (processed < batchSize && _entries.Count > 0)
        {
            if (_batchCursor >= _entries.Count)
            {
                _batchCursor = 0;
                break; // We've completed a full pass
            }

            var entry = _entries[_batchCursor];
            _batchCursor++;
            processed++;

            // Skip destroyed renderers
            if (entry.renderer == null || entry.transform == null)
            {
                _entries.RemoveAt(_batchCursor - 1);
                _registeredRenderers.Remove(entry.renderer);
                _batchCursor--;
                continue;
            }

            // Skip objects already fully deactivated by RuntimeEnvironmentGenerator
            if (!entry.renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            ProcessEntry(entry, camPos, ref visible, ref culled, ref shadowOnly);
        }

        _lastVisibleCount += visible;
        _lastCulledCount += culled;
        _lastShadowOnlyCount += shadowOnly;

        if (logStats && _batchCursor == 0)
        {
            Debug.Log($"[OcclusionCulling] Visible: {_lastVisibleCount} | Culled: {_lastCulledCount} | ShadowOnly: {_lastShadowOnlyCount} | Total: {_entries.Count}");
        }
    }

    // ─── Core Culling Logic ──────────────────────────────────────────

    private void ProcessEntry(CullEntry entry, Vector3 camPos, ref int visible, ref int culled, ref int shadowOnly)
    {
        // Determine effective settings (zone overrides vs global)
        bool neverCull = false;
        float effectiveNear = nearDistance;
        float effectiveMid = midDistance;
        float effectiveFar = farDistance;
        float effectiveShadow = shadowDistance;
        OcclusionCullingZone.CullingPriority priority = OcclusionCullingZone.CullingPriority.Medium;

        if (entry.zone != null && entry.zone.enabled)
        {
            neverCull = entry.zone.neverCull;
            priority = entry.zone.priority;

            if (entry.zone.useCustomDistances)
            {
                effectiveNear = entry.zone.customNearDistance;
                effectiveMid = entry.zone.customMidDistance;
                effectiveFar = entry.zone.customFarDistance;
            }

            if (entry.zone.customShadowDistance > 0f)
            {
                effectiveShadow = entry.zone.customShadowDistance;
            }
        }

        // Never-cull objects are always visible
        if (neverCull)
        {
            SetVisible(entry);
            visible++;
            return;
        }

        // Calculate distance
        float distSqr = (entry.transform.position - camPos).sqrMagnitude;
        float nearSqr = effectiveNear * effectiveNear;
        float midSqr = effectiveMid * effectiveMid;
        float farSqr = effectiveFar * effectiveFar;
        float shadowSqr = effectiveShadow * effectiveShadow;

        // --- Tier 1: Near Distance → always visible ---
        if (distSqr <= nearSqr)
        {
            SetVisible(entry);
            visible++;
            return;
        }

        // --- Tier 4: Beyond Far Distance → always culled ---
        if (distSqr > farSqr)
        {
            SetCulled(entry);
            culled++;
            return;
        }

        // --- Frustum Check (for mid-range objects) ---
        // Update the world bounds from the renderer
        entry.worldBounds = entry.renderer.bounds;
        bool inFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, entry.worldBounds);

        if (!inFrustum)
        {
            // Object is outside the camera frustum
            SetCulled(entry);
            culled++;
            return;
        }

        // Object is in frustum and within range — it's visible
        // But check if shadows should be disabled at this distance
        if (distSqr > shadowSqr)
        {
            // Check zone setting for shadow-only behavior
            if (entry.zone != null && entry.zone.shadowOnlyMode && entry.zone.enabled)
            {
                SetShadowOnly(entry);
                shadowOnly++;
                return;
            }

            // Otherwise, visible but with shadows disabled for performance
            SetVisibleNoShadow(entry);
            visible++;
            return;
        }

        // Fully visible with shadows
        SetVisible(entry);
        visible++;
    }

    // ─── State Transitions ───────────────────────────────────────────

    private void SetVisible(CullEntry entry)
    {
        if (entry.wasCulled || entry.wasShadowOnly)
        {
            entry.renderer.enabled = true;
            RestoreShadowMode(entry);
            entry.wasCulled = false;
            entry.wasShadowOnly = false;
        }
    }

    private void SetVisibleNoShadow(CullEntry entry)
    {
        entry.renderer.enabled = true;
        entry.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        entry.wasCulled = false;
        entry.wasShadowOnly = false;
    }

    private void SetShadowOnly(CullEntry entry)
    {
        entry.renderer.enabled = true;
        entry.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        entry.wasCulled = false;
        entry.wasShadowOnly = true;
    }

    private void SetCulled(CullEntry entry)
    {
        if (!entry.wasCulled)
        {
            entry.renderer.enabled = false;
            entry.wasCulled = true;
            entry.wasShadowOnly = false;
        }
    }

    private void RestoreShadowMode(CullEntry entry)
    {
        entry.renderer.shadowCastingMode = (UnityEngine.Rendering.ShadowCastingMode)(int)entry.originalShadowMode;
    }

    // ─── Registration API ────────────────────────────────────────────

    /// <summary>
    /// Registers a single Renderer to be tracked by the occlusion culling system.
    /// </summary>
    public void RegisterRenderer(Renderer rend)
    {
        if (rend == null) return;
        if (_registeredRenderers.Contains(rend)) return;

        // Layer check
        if (((1 << rend.gameObject.layer) & cullableLayers.value) == 0) return;

        var entry = new CullEntry
        {
            renderer = rend,
            transform = rend.transform,
            worldBounds = rend.bounds,
            zone = rend.GetComponentInParent<OcclusionCullingZone>(),
            wasCulled = false,
            wasShadowOnly = false,
            originalShadowMode = (ShadowCastingMode)(int)rend.shadowCastingMode
        };

        _entries.Add(entry);
        _registeredRenderers.Add(rend);
    }

    /// <summary>
    /// Registers all Renderers on a GameObject and its children.
    /// </summary>
    public void RegisterGameObject(GameObject go)
    {
        if (go == null) return;

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers)
        {
            RegisterRenderer(rend);
        }
    }

    /// <summary>
    /// Unregisters a single Renderer from the occlusion culling system and restores its state.
    /// </summary>
    public void UnregisterRenderer(Renderer rend)
    {
        if (rend == null) return;
        if (!_registeredRenderers.Contains(rend)) return;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].renderer == rend)
            {
                // Restore renderer state before removing
                var entry = _entries[i];
                entry.renderer.enabled = true;
                RestoreShadowMode(entry);

                _entries.RemoveAt(i);
                break;
            }
        }

        _registeredRenderers.Remove(rend);
    }

    /// <summary>
    /// Unregisters all Renderers on a GameObject and its children.
    /// </summary>
    public void UnregisterGameObject(GameObject go)
    {
        if (go == null) return;

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers)
        {
            UnregisterRenderer(rend);
        }
    }

    /// <summary>
    /// Scans the entire scene and registers all renderers on the cullable layers.
    /// Call this once when the scene loads, or from the editor window.
    /// </summary>
    public void ScanAndRegisterAll()
    {
        // Clear existing registrations
        RestoreAllAndClear();

        Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);
        int registered = 0;

        foreach (var rend in allRenderers)
        {
            if (rend == null) continue;

            // Skip UI elements (Canvas renderers)
            if (rend is CanvasRenderer) continue;

            // Skip if not on a cullable layer
            if (((1 << rend.gameObject.layer) & cullableLayers.value) == 0) continue;

            // Skip the culling camera itself
            if (cullingCamera != null && rend.gameObject == cullingCamera.gameObject) continue;

            // Skip player
            if (rend.gameObject.CompareTag("Player")) continue;
            if (rend.transform.root.CompareTag("Player")) continue;

            RegisterRenderer(rend);
            registered++;
        }

        Debug.Log($"[OcclusionCullingManager] Scanned scene: registered {registered} renderers for culling.");
    }

    /// <summary>
    /// Restores all renderers to their original state and clears registration.
    /// </summary>
    public void RestoreAllAndClear()
    {
        foreach (var entry in _entries)
        {
            if (entry.renderer != null)
            {
                entry.renderer.enabled = true;
                RestoreShadowMode(entry);
            }
        }

        _entries.Clear();
        _registeredRenderers.Clear();
        _batchCursor = 0;
    }

    /// <summary>
    /// Checks whether a specific Renderer is registered with this system.
    /// </summary>
    public bool IsRegistered(Renderer rend)
    {
        return _registeredRenderers.Contains(rend);
    }

    /// <summary>
    /// Checks whether a specific Renderer is currently culled by this system.
    /// </summary>
    public bool IsCulled(Renderer rend)
    {
        if (rend == null) return false;

        foreach (var entry in _entries)
        {
            if (entry.renderer == rend)
            {
                return entry.wasCulled;
            }
        }
        return false;
    }

    // ─── Gizmos ──────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        Vector3 center = transform.position;

        // Use camera position if available
        if (Application.isPlaying && cullingCamera != null)
        {
            center = cullingCamera.transform.position;
        }
        else if (Camera.main != null)
        {
            center = Camera.main.transform.position;
        }

        // Near tier — green
        Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.15f);
        Gizmos.DrawWireSphere(center, nearDistance);

        // Mid tier — yellow
        Gizmos.color = new Color(0.95f, 0.85f, 0.1f, 0.12f);
        Gizmos.DrawWireSphere(center, midDistance);

        // Far tier — red
        Gizmos.color = new Color(0.95f, 0.2f, 0.15f, 0.1f);
        Gizmos.DrawWireSphere(center, farDistance);

        // Shadow distance — cyan
        Gizmos.color = new Color(0.1f, 0.8f, 0.9f, 0.1f);
        Gizmos.DrawWireSphere(center, shadowDistance);
    }

    private void OnDestroy()
    {
        RestoreAllAndClear();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
