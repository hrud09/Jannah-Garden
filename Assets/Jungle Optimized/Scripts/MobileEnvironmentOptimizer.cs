using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime-only performance layer for the heavy jungle / foliage environment on phones.
///
/// Nothing here edits the scene or any asset: every change is applied in Start and undone in
/// OnDisable, and by default it only kicks in on mobile (or when the "Mobile" quality level is
/// active), so the PC / editor look is untouched. Disable the component to compare.
///
/// What it does:
///  - Distance-culls every decorative renderer under <see cref="cullRoots"/> with a native
///    <see cref="CullingGroup"/>, in three size classes (small props, bushes/rocks, trees).
///    Colliders and scripts are left alone, so gameplay is unaffected.
///  - Turns off shadow casting for the small/medium classes (trees keep theirs).
///  - Pulls the camera far plane and linear fog in so the culling edge is hidden by fog.
///  - Keeps only the nearest few lantern point lights on.
///  - Pauses looping ambient particles (falling leaves, fireflies) when far away.
///  - Cuts the Brute Force geometry-shader shell grass down (or off) - geometry shaders are
///    extremely slow on mobile GPUs.
///  - Renders the minimap camera every Nth frame while collapsed, without the opaque/depth copies.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // after BF_Terrain.Start has pushed its textures into the grass material
public class MobileEnvironmentOptimizer : MonoBehaviour
{
    public enum ApplyMode { MobileOnly, Always, Never }

    [Header("Activation")]
    [Tooltip("MobileOnly = Android/iOS devices, or whenever the active quality level is named 'Mobile'.\n" +
             "Always = everywhere, including the editor (use this to preview the mobile result).")]
    public ApplyMode applyMode = ApplyMode.MobileOnly;

    [Tooltip("Camera the distances are measured from. Falls back to Camera.main.")]
    public Camera mainCamera;

    [Header("Distance culling")]
    [Tooltip("Hierarchies containing purely decorative renderers (jungle, scattered foliage). " +
             "Lantern lights and looping particles found under these roots are managed too.")]
    public Transform[] cullRoots;

    [Tooltip("Objects whose bounding radius is at or below this are 'small' (flowers, plants, mushrooms, pebbles).")]
    public float smallObjectRadius = 1.2f;
    public float smallObjectDistance = 25f;

    [Tooltip("Objects whose bounding radius is at or below this are 'medium' (bushes, rocks, lanterns, logs).")]
    public float mediumObjectRadius = 4f;
    public float mediumObjectDistance = 45f;

    [Tooltip("Everything bigger (trees). Keep this a little below the fog end so trees fade out instead of popping.")]
    public float largeObjectDistance = 110f;

    [Tooltip("Small and medium objects stop casting shadows. Trees keep theirs.")]
    public bool disableSmallAndMediumShadows = true;

    [Tooltip("How often (seconds) bounding spheres of movable PlaceableItems are re-synced to their transforms.")]
    public float movingObjectRefreshInterval = 0.5f;

    [Header("Camera & fog")]
    public bool overrideCameraAndFog = true;
    public float cameraFarClip = 125f;
    [Tooltip("Only applied when the scene uses Linear fog. Values are only ever pulled closer, never pushed out.")]
    public float fogStart = 35f;
    public float fogEnd = 120f;

    [Header("Lantern / point lights")]
    public int maxActiveLights = 4;
    public float lightDistance = 35f;

    [Header("Ambient particles")]
    public float particleDistance = 40f;

    [Header("Shell grass (Brute Force, geometry shader)")]
    public Terrain shellGrassTerrain;
    [Tooltip("Turn the shell-grass terrain off completely (cheapest). Otherwise it is kept but reduced.")]
    public bool disableShellGrass = false;
    [Range(1, 17)] public int shellGrassStacks = 6;
    public float shellGrassFadeStart = 15f;
    public float shellGrassFadeEnd = 35f;

    [Header("Minimap")]
    public Camera minimapCamera;
    public MinimapBehaviour minimap;
    [Tooltip("While collapsed, the minimap camera renders once every N frames (the render texture keeps the last image).")]
    [Range(1, 10)] public int minimapRenderInterval = 2;
    public float minimapFarClip = 80f;

    [Header("Misc")]
    [Tooltip("Objects that are deactivated while the optimizer is active (e.g. leftover demo objects).")]
    public GameObject[] disableObjects;

    public bool IsActive { get; private set; }
    public int CulledUnitCount => units.Count;

    // ---------------------------------------------------------------- distance culling state

    private const int SmallBand = 0, MediumBand = 1, LargeBand = 2;

    private class Unit
    {
        public Renderer[] renderers;
        public Transform anchor;
        public Vector3 localCenter;
        public float radius;
        public int maxBand;
        public bool visible = true;
        public bool moving;
    }

    private readonly List<Unit> units = new List<Unit>();
    private readonly List<int> movingUnits = new List<int>();
    private readonly List<KeyValuePair<Renderer, ShadowCastingMode>> changedShadows = new List<KeyValuePair<Renderer, ShadowCastingMode>>();
    private BoundingSphere[] spheres;
    private CullingGroup group;
    private float nextMovingRefresh;

    // ---------------------------------------------------------------- lights / particles

    private Light[] lights = new Light[0];
    private float[] lightKeys;
    private int[] lightOrder;
    private bool[] lightOriginallyEnabled;

    private class Ambient
    {
        public ParticleSystem system;
        public Renderer[] renderers;
        public ParticleSystemCullingMode originalCulling;
        public bool near = true;
    }
    private readonly List<Ambient> ambients = new List<Ambient>();
    private float nextSlowTick;

    // ---------------------------------------------------------------- restore state

    private float originalFarClip;
    private bool cameraOverridden;
    private float originalFogStart, originalFogEnd;
    private bool fogOverridden;

    private Material originalGrassMaterial, mobileGrassMaterial;
    private bool originalGrassDrawHeightmap;
    private ShadowCastingMode originalGrassShadows;

    private float originalMinimapFar;
    private bool originalMinimapMsaa, originalMinimapHdr, originalMinimapEnabled;
    private CameraOverrideOption originalMinimapColor, originalMinimapDepth;
    private bool minimapOverridden;

    private readonly List<GameObject> deactivated = new List<GameObject>();

    // =================================================================================

    private bool ShouldApply()
    {
        switch (applyMode)
        {
            case ApplyMode.Always: return true;
            case ApplyMode.Never: return false;
            default:
                if (Application.isMobilePlatform) return true;
                string[] names = QualitySettings.names;
                int level = QualitySettings.GetQualityLevel();
                return level >= 0 && level < names.Length && names[level] == "Mobile";
        }
    }

    private bool started;

    private void Start()
    {
        started = true;
        if (!ShouldApply()) { enabled = false; return; }
        if (mainCamera == null) mainCamera = Camera.main;
        Apply();
    }

    // Lets the component be toggled off/on in the Inspector during play to A/B the result.
    private void OnEnable()
    {
        if (started && !IsActive && ShouldApply()) Apply();
    }

    private void OnDisable()
    {
        if (IsActive) Revert();
    }

    private void OnValidate()
    {
        mediumObjectRadius = Mathf.Max(mediumObjectRadius, smallObjectRadius);
        mediumObjectDistance = Mathf.Max(mediumObjectDistance, smallObjectDistance);
        largeObjectDistance = Mathf.Max(largeObjectDistance, mediumObjectDistance);
        fogEnd = Mathf.Max(fogEnd, fogStart + 1f);
        shellGrassFadeEnd = Mathf.Max(shellGrassFadeEnd, shellGrassFadeStart + 1f);
    }

    private void Apply()
    {
        IsActive = true;

        foreach (GameObject go in disableObjects)
        {
            if (go != null && go.activeSelf) { go.SetActive(false); deactivated.Add(go); }
        }

        ApplyCameraAndFog();
        ApplyShellGrass();
        ApplyMinimap();
        CollectLightsAndParticles();
        BuildCullingGroup();

        nextSlowTick = 0f;
        nextMovingRefresh = Time.unscaledTime + movingObjectRefreshInterval;
    }

    private void Revert()
    {
        IsActive = false;

        if (group != null) { group.onStateChanged = null; group.Dispose(); group = null; }
        foreach (Unit u in units)
        {
            if (u.visible) continue;
            foreach (Renderer r in u.renderers) if (r != null) r.enabled = true;
        }
        units.Clear();
        movingUnits.Clear();

        foreach (var kv in changedShadows) if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
        changedShadows.Clear();

        for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].enabled = lightOriginallyEnabled[i];
        lights = new Light[0];

        foreach (Ambient a in ambients)
        {
            if (a.system == null) continue;
            var main = a.system.main;
            main.cullingMode = a.originalCulling;
            if (!a.near) { SetRenderers(a.renderers, true); a.system.Play(true); }
        }
        ambients.Clear();

        if (cameraOverridden && mainCamera != null) mainCamera.farClipPlane = originalFarClip;
        cameraOverridden = false;
        if (fogOverridden)
        {
            RenderSettings.fogStartDistance = originalFogStart;
            RenderSettings.fogEndDistance = originalFogEnd;
            fogOverridden = false;
        }

        if (shellGrassTerrain != null)
        {
            shellGrassTerrain.drawHeightmap = originalGrassDrawHeightmap;
            shellGrassTerrain.shadowCastingMode = originalGrassShadows;
            if (mobileGrassMaterial != null)
            {
                shellGrassTerrain.materialTemplate = originalGrassMaterial;
                Destroy(mobileGrassMaterial);
                mobileGrassMaterial = null;
            }
        }

        if (minimapOverridden && minimapCamera != null)
        {
            minimapCamera.farClipPlane = originalMinimapFar;
            minimapCamera.allowMSAA = originalMinimapMsaa;
            minimapCamera.allowHDR = originalMinimapHdr;
            minimapCamera.enabled = originalMinimapEnabled;
            var data = minimapCamera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) { data.requiresColorOption = originalMinimapColor; data.requiresDepthOption = originalMinimapDepth; }
        }
        minimapOverridden = false;

        foreach (GameObject go in deactivated) if (go != null) go.SetActive(true);
        deactivated.Clear();
    }

    // ---------------------------------------------------------------- camera / fog

    private void ApplyCameraAndFog()
    {
        if (!overrideCameraAndFog) return;

        if (mainCamera != null)
        {
            originalFarClip = mainCamera.farClipPlane;
            mainCamera.farClipPlane = Mathf.Min(originalFarClip, cameraFarClip);
            cameraOverridden = true;
        }

        if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear)
        {
            originalFogStart = RenderSettings.fogStartDistance;
            originalFogEnd = RenderSettings.fogEndDistance;
            RenderSettings.fogStartDistance = Mathf.Min(originalFogStart, fogStart);
            RenderSettings.fogEndDistance = Mathf.Min(originalFogEnd, fogEnd);
            fogOverridden = true;
        }
    }

    // ---------------------------------------------------------------- shell grass

    private void ApplyShellGrass()
    {
        if (shellGrassTerrain == null) return;

        originalGrassDrawHeightmap = shellGrassTerrain.drawHeightmap;
        originalGrassShadows = shellGrassTerrain.shadowCastingMode;

        if (disableShellGrass)
        {
            shellGrassTerrain.drawHeightmap = false;
            return;
        }

        // The extruded shells casting into the shadow map is the most expensive part - drop it.
        shellGrassTerrain.shadowCastingMode = ShadowCastingMode.Off;

        originalGrassMaterial = shellGrassTerrain.materialTemplate;
        if (originalGrassMaterial == null) return;

        // Work on a copy so the material asset on disk is never modified at runtime.
        mobileGrassMaterial = new Material(originalGrassMaterial) { name = originalGrassMaterial.name + " (Mobile)" };
        SetMin(mobileGrassMaterial, "_NumberOfStacks", shellGrassStacks);
        SetMin(mobileGrassMaterial, "_FadeDistanceStart", shellGrassFadeStart);
        SetMin(mobileGrassMaterial, "_FadeDistanceEnd", shellGrassFadeEnd);
        shellGrassTerrain.materialTemplate = mobileGrassMaterial;
    }

    private static void SetMin(Material m, string property, float value)
    {
        if (m.HasProperty(property)) m.SetFloat(property, Mathf.Min(m.GetFloat(property), value));
    }

    // ---------------------------------------------------------------- minimap

    private void ApplyMinimap()
    {
        if (minimapCamera == null) return;

        originalMinimapFar = minimapCamera.farClipPlane;
        originalMinimapMsaa = minimapCamera.allowMSAA;
        originalMinimapHdr = minimapCamera.allowHDR;
        originalMinimapEnabled = minimapCamera.enabled;

        minimapCamera.farClipPlane = Mathf.Min(originalMinimapFar, minimapFarClip);
        minimapCamera.allowMSAA = false;
        minimapCamera.allowHDR = false;

        var data = minimapCamera.GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
        {
            originalMinimapColor = data.requiresColorOption;
            originalMinimapDepth = data.requiresDepthOption;
            // The minimap never needs the opaque-colour or depth copies the main camera may request.
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }
        minimapOverridden = true;
    }

    private void UpdateMinimap()
    {
        if (!minimapOverridden || minimapCamera == null || !originalMinimapEnabled) return;

        bool expanded = minimap != null && minimap.IsExpanded;
        bool render = expanded || minimapRenderInterval <= 1 || Time.frameCount % minimapRenderInterval == 0;
        if (minimapCamera.enabled != render) minimapCamera.enabled = render;
    }

    // ---------------------------------------------------------------- lights & particles

    private void CollectLightsAndParticles()
    {
        var foundLights = new List<Light>();
        ambients.Clear();

        foreach (Transform root in cullRoots)
        {
            if (root == null) continue;

            foreach (Light l in root.GetComponentsInChildren<Light>(false))
            {
                if (l.type == LightType.Point || l.type == LightType.Spot) foundLights.Add(l);
            }

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(false))
            {
                // Only top-level looping systems: one-shot ones (e.g. bush leaf bursts) are
                // triggered by gameplay and must stay untouched; sub-systems follow their parent.
                if (!ps.main.loop) continue;
                Transform parent = ps.transform.parent;
                if (parent != null && parent.GetComponentInParent<ParticleSystem>() != null) continue;

                var a = new Ambient
                {
                    system = ps,
                    renderers = ps.GetComponentsInChildren<ParticleSystemRenderer>(false),
                    originalCulling = ps.main.cullingMode,
                };
                var main = ps.main;
                // Stop simulating when off-screen instead of 'Always Simulate'.
                main.cullingMode = ParticleSystemCullingMode.Pause;
                ambients.Add(a);
            }
        }

        lights = foundLights.ToArray();
        lightKeys = new float[lights.Length];
        lightOrder = new int[lights.Length];
        lightOriginallyEnabled = new bool[lights.Length];
        for (int i = 0; i < lights.Length; i++) lightOriginallyEnabled[i] = lights[i].enabled;
    }

    private void UpdateLightsAndParticles(Vector3 eye)
    {
        // Nearest-N lanterns within range stay lit, everything else is switched off.
        if (lights.Length > 0)
        {
            float maxSqr = lightDistance * lightDistance;
            for (int i = 0; i < lights.Length; i++)
            {
                lightOrder[i] = i;
                lightKeys[i] = lights[i] != null && lightOriginallyEnabled[i]
                    ? (lights[i].transform.position - eye).sqrMagnitude
                    : float.MaxValue;
            }
            System.Array.Sort(lightKeys, lightOrder);
            for (int k = 0; k < lightOrder.Length; k++)
            {
                Light l = lights[lightOrder[k]];
                if (l == null) continue;
                bool on = k < maxActiveLights && lightKeys[k] <= maxSqr;
                if (l.enabled != on) l.enabled = on;
            }
        }

        float particleSqr = particleDistance * particleDistance;
        foreach (Ambient a in ambients)
        {
            if (a.system == null) continue;
            bool near = (a.system.transform.position - eye).sqrMagnitude <= particleSqr;
            if (near == a.near) continue;
            a.near = near;
            if (near) { SetRenderers(a.renderers, true); a.system.Play(true); }
            else { a.system.Pause(true); SetRenderers(a.renderers, false); }
        }
    }

    private static void SetRenderers(Renderer[] renderers, bool on)
    {
        if (renderers == null) return;
        foreach (Renderer r in renderers) if (r != null) r.enabled = on;
    }

    // ---------------------------------------------------------------- distance culling

    private void BuildCullingGroup()
    {
        units.Clear();
        movingUnits.Clear();
        changedShadows.Clear();

        var claimed = new HashSet<Renderer>();
        var buffer = new List<Renderer>();

        foreach (Transform root in cullRoots)
        {
            if (root == null) continue;

            // An LOD group is culled as one unit so its levels never disagree.
            foreach (LODGroup lg in root.GetComponentsInChildren<LODGroup>(false))
            {
                buffer.Clear();
                foreach (LOD lod in lg.GetLODs())
                {
                    if (lod.renderers == null) continue;
                    foreach (Renderer r in lod.renderers)
                    {
                        if (IsCullable(r) && claimed.Add(r)) buffer.Add(r);
                    }
                }
                AddUnit(lg.transform, buffer);
            }
        }

        foreach (Transform root in cullRoots)
        {
            if (root == null) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!IsCullable(r) || !claimed.Add(r)) continue;
                buffer.Clear();
                buffer.Add(r);
                AddUnit(r.transform, buffer);
            }
        }

        if (units.Count == 0 || mainCamera == null) return;

        spheres = new BoundingSphere[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            Unit u = units[i];
            spheres[i] = new BoundingSphere(u.anchor.TransformPoint(u.localCenter), u.radius);
            if (u.moving) movingUnits.Add(i);
        }

        group = new CullingGroup { targetCamera = mainCamera };
        group.SetBoundingSpheres(spheres);
        group.SetBoundingSphereCount(units.Count);
        group.SetBoundingDistances(new[] { smallObjectDistance, mediumObjectDistance, Mathf.Min(largeObjectDistance, cameraFarClip) });
        group.SetDistanceReferencePoint(mainCamera.transform);
        group.onStateChanged = OnCullingStateChanged;

        // The group only reports *changes*, so once it has run a culling pass, sync every unit once.
        StartCoroutine(InitialSync());
    }

    private static bool IsCullable(Renderer r)
    {
        return r != null && r.enabled && !(r is ParticleSystemRenderer) && r.gameObject.activeInHierarchy;
    }

    private void AddUnit(Transform anchor, List<Renderer> renderers)
    {
        if (renderers.Count == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++) b.Encapsulate(renderers[i].bounds);
        float radius = Mathf.Max(b.extents.magnitude, 0.05f);

        int band = radius <= smallObjectRadius ? SmallBand : radius <= mediumObjectRadius ? MediumBand : LargeBand;

        var u = new Unit
        {
            renderers = renderers.ToArray(),
            anchor = anchor,
            localCenter = anchor.InverseTransformPoint(b.center),
            radius = radius,
            maxBand = band,
            moving = anchor.GetComponentInParent<PlaceableItem>() != null,
        };
        units.Add(u);

        if (disableSmallAndMediumShadows && band != LargeBand)
        {
            foreach (Renderer r in u.renderers)
            {
                ShadowCastingMode mode = r.shadowCastingMode;
                if (mode == ShadowCastingMode.Off || mode == ShadowCastingMode.ShadowsOnly) continue;
                changedShadows.Add(new KeyValuePair<Renderer, ShadowCastingMode>(r, mode));
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    private IEnumerator InitialSync()
    {
        yield return null;
        yield return null;
        if (group == null) yield break;
        for (int i = 0; i < units.Count; i++) SetUnitVisible(units[i], group.GetDistance(i) <= units[i].maxBand);
    }

    private void OnCullingStateChanged(CullingGroupEvent evt)
    {
        if (evt.index >= units.Count) return;
        Unit u = units[evt.index];
        SetUnitVisible(u, evt.currentDistance <= u.maxBand);
    }

    private static void SetUnitVisible(Unit u, bool visible)
    {
        if (u.visible == visible) return;
        u.visible = visible;
        foreach (Renderer r in u.renderers) if (r != null) r.enabled = visible;
    }

    // ---------------------------------------------------------------- per frame

    private void LateUpdate()
    {
        if (!IsActive) return;

        UpdateMinimap();

        float now = Time.unscaledTime;

        if (movingUnits.Count > 0 && now >= nextMovingRefresh)
        {
            nextMovingRefresh = now + movingObjectRefreshInterval;
            foreach (int i in movingUnits)
            {
                Unit u = units[i];
                if (u.anchor != null) spheres[i].position = u.anchor.TransformPoint(u.localCenter);
            }
        }

        if (now >= nextSlowTick && mainCamera != null)
        {
            nextSlowTick = now + 0.25f;
            UpdateLightsAndParticles(mainCamera.transform.position);
        }
    }
}
