using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes the lake look like water from the inside once the main camera dips under its surface (a swimming
/// camel, a low camera, a dive on the pegasus).
///
/// Before this, the lake was only a surface: its shader culls back faces, so from below it vanished and
/// the bed read as a sunny beach under an open sky. While submerged this:
/// <list type="bullet">
/// <item>swaps the main camera's fog for short-range water fog, clears to the water colour instead of the
/// skybox and pulls the far plane in to the fog's end (no clouds or distant terrain through the water);</item>
/// <item>draws the surface's underside — Snell's window looking up, a mirror of the water elsewhere;</item>
/// <item>draws moving caustics over the submerged lakebed;</item>
/// <item>lays slanted light shafts, a tint and a deep vignette over the frame;</item>
/// <item>fills the water with drifting motes;</item>
/// <item>muffles all audio through a low-pass on the listener.</item>
/// </list>
/// Everything is per camera: fog and clear settings are swapped in for the main camera only and put back
/// as soon as it has rendered, and the extra meshes are submitted to that camera alone, so the minimap's
/// top-down camera never sees any of it. Nothing needs a depth texture (mobile has none).
/// </summary>
[DisallowMultipleComponent]
public class UnderwaterEffect : MonoBehaviour
{
    private const string LakeShaderName = "Jannah/Environment/Lake Water";

    [Tooltip("The lake surface renderer. Found automatically (by its shader) if left empty.")]
    public Renderer lakeSurface;

    [Header("Materials")]
    public Material undersideMaterial;
    public Material causticsMaterial;
    public Material overlayMaterial;
    public Material motesMaterial;

    [Header("Water")]
    [Tooltip("Water colour just under the surface.")]
    public Color shallowColor = new Color(0.12f, 0.50f, 0.58f);
    [Tooltip("Water colour at Deep Depth and below.")]
    public Color deepColor = new Color(0.035f, 0.20f, 0.34f);
    [Tooltip("Camera depth (m) at which the water reaches Deep Color and its shortest visibility.")]
    public float deepDepth = 4f;
    [Tooltip("How far you can see (fog end, m) just under the surface (x) and at Deep Depth (y).")]
    public Vector2 visibility = new Vector2(24f, 12f);
    [Tooltip("Scene fog brightness that counts as full daylight. The water darkens with the scene fog, so " +
             "it follows a day/night cycle without knowing about one.")]
    public float referenceFogLuminance = 0.83f;
    [Tooltip("Seconds to fade the effect in on the way down. Coming up is instant: fog lingering above " +
             "the water would be wrong for a frame.")]
    public float fadeInTime = 0.15f;

    [Header("Detail")]
    [Tooltip("Spacing (m) of the mesh the lakebed caustics are drawn on.")]
    public float causticGridSpacing = 1f;
    [Tooltip("How high above the lakebed the caustic mesh floats (m).")]
    public float causticLift = 0.04f;
    [Tooltip("Number of drifting motes around the camera.")]
    public int moteCount = 220;

    [Header("Audio")]
    public bool muffleAudio = true;
    [Tooltip("Low-pass cutoff while fully submerged (Hz).")]
    public float muffledCutoff = 750f;

    /// <summary>True while the main camera is under the lake surface.</summary>
    public bool IsUnderwater { get; private set; }

    /// <summary>0 above water, 1 fully submerged (fades in over <see cref="fadeInTime"/>).</summary>
    public float Weight { get; private set; }

    private static readonly int UnderwaterColorId = Shader.PropertyToID("_UnderwaterColor");
    private static readonly int UnderwaterWeightId = Shader.PropertyToID("_UnderwaterWeight");
    private static readonly int UnderwaterParamsId = Shader.PropertyToID("_UnderwaterParams");
    private static readonly int UnderwaterSurfaceId = Shader.PropertyToID("_UnderwaterSurfaceY");

    private Camera _cam;
    private Terrain _terrain;
    private float _surfaceY;
    private Rect _footprint;
    private bool _lakeResolved;
    private Mesh _lakeMesh;
    private Material _undersideInstance;

    private Mesh _causticMesh;
    private Mesh _overlayMesh;
    private Mesh _motesMesh;

    private Color _waterColor;
    private float _fogEnd;
    private float _depth;

    private AudioListener _listener;
    private AudioLowPassFilter _lowPass;

    // Scene settings saved while the main camera renders with the water's.
    private bool _overridden;
    private bool _savedFog;
    private FogMode _savedFogMode;
    private Color _savedFogColor;
    private float _savedFogStart, _savedFogEnd;
    private CameraClearFlags _savedClear;
    private Color _savedBackground;
    private float _savedFar;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        Weight = 0f;
        IsUnderwater = false;
        Shader.SetGlobalFloat(UnderwaterWeightId, 0f);
        if (_lowPass != null) _lowPass.enabled = false;
    }

    private void OnDestroy()
    {
        if (_causticMesh != null) Destroy(_causticMesh);
        if (_overlayMesh != null) Destroy(_overlayMesh);
        if (_motesMesh != null) Destroy(_motesMesh);
        if (_undersideInstance != null) Destroy(_undersideInstance);
    }

    private void LateUpdate()
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null || !ResolveLake()) return;

        Vector3 camPos = _cam.transform.position;
        IsUnderwater = IsPointUnderwater(camPos);

        if (!IsUnderwater) Weight = 0f;
        else Weight = fadeInTime > 0f ? Mathf.MoveTowards(Weight, 1f, Time.deltaTime / fadeInTime) : 1f;

        UpdateAudio();
        Shader.SetGlobalFloat(UnderwaterWeightId, Weight);
        if (Weight <= 0f) return;

        // ---- Water colour & visibility for this frame ----------------------------------------
        _depth = Mathf.Max(0f, _surfaceY - camPos.y);
        float deep01 = Mathf.Clamp01(_depth / Mathf.Max(0.01f, deepDepth));
        float daylight = Mathf.Clamp(Luminance(RenderSettings.fogColor) / Mathf.Max(0.01f, referenceFogLuminance), 0.2f, 1.1f);
        _waterColor = Color.Lerp(shallowColor, deepColor, deep01) * daylight;
        _waterColor.a = 1f;
        _fogEnd = Mathf.Lerp(visibility.x, visibility.y, deep01);

        float lookUp = Vector3.Dot(_cam.transform.forward, Vector3.up);
        Shader.SetGlobalColor(UnderwaterColorId, _waterColor);
        Shader.SetGlobalVector(UnderwaterParamsId, new Vector4(lookUp, deep01, Mathf.Clamp01(daylight), 0f));
        Shader.SetGlobalFloat(UnderwaterSurfaceId, _surfaceY);

        // ---- Extra geometry, submitted to the main camera only -------------------------------
        int layer = _cam.gameObject.layer;

        if (_undersideInstance != null && _lakeMesh != null)
            Graphics.DrawMesh(_lakeMesh, lakeSurface.localToWorldMatrix, _undersideInstance, layer, _cam, 0, null, false, false);

        if (causticsMaterial != null)
        {
            if (_causticMesh == null) _causticMesh = BuildCausticMesh();
            if (_causticMesh != null)
                Graphics.DrawMesh(_causticMesh, Matrix4x4.identity, causticsMaterial, layer, _cam, 0, null, false, false);
        }

        if (motesMaterial != null)
        {
            if (_motesMesh == null) _motesMesh = BuildMotesMesh(moteCount);
            Graphics.DrawMesh(_motesMesh, Matrix4x4.identity, motesMaterial, layer, _cam, 0, null, false, false);
        }

        if (overlayMaterial != null)
        {
            if (_overlayMesh == null) _overlayMesh = BuildQuad();
            Graphics.DrawMesh(_overlayMesh, OverlayMatrix(_cam), overlayMaterial, layer, _cam, 0, null, false, false);
        }
    }

    /// <summary>Under the surface, over the lake, and over ground that is itself below the water line.</summary>
    public bool IsPointUnderwater(Vector3 p)
    {
        if (!ResolveLake()) return false;
        if (p.y >= _surfaceY - 0.02f) return false;
        if (!_footprint.Contains(new Vector2(p.x, p.z))) return false;
        return GroundHeight(p) < _surfaceY;
    }

    // ---------------------------------------------------------------------------------------------
    // Per-camera fog / clear swap
    // ---------------------------------------------------------------------------------------------

    private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _cam || Weight <= 0f) return;

        _savedFog = RenderSettings.fog;
        _savedFogMode = RenderSettings.fogMode;
        _savedFogColor = RenderSettings.fogColor;
        _savedFogStart = RenderSettings.fogStartDistance;
        _savedFogEnd = RenderSettings.fogEndDistance;
        _savedClear = camera.clearFlags;
        _savedBackground = camera.backgroundColor;
        _savedFar = camera.farClipPlane;
        _overridden = true;

        float w = Weight;
        Color fogColor = Color.Lerp(_savedFogColor, _waterColor, w);

        // Linear stays linear: the build only keeps the fog variants the scenes use, so switching mode
        // here could land on a stripped variant on device.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = Mathf.Lerp(_savedFog ? _savedFogStart : _fogEnd, 0f, w);
        RenderSettings.fogEndDistance = Mathf.Lerp(_savedFog ? _savedFogEnd : _fogEnd * 4f, _fogEnd, w);

        if (w >= 0.999f)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = fogColor;
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 1f, _fogEnd + 2f);
        }
    }

    private void OnEndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!_overridden || camera != _cam) return;
        _overridden = false;

        RenderSettings.fog = _savedFog;
        RenderSettings.fogMode = _savedFogMode;
        RenderSettings.fogColor = _savedFogColor;
        RenderSettings.fogStartDistance = _savedFogStart;
        RenderSettings.fogEndDistance = _savedFogEnd;
        camera.clearFlags = _savedClear;
        camera.backgroundColor = _savedBackground;
        camera.farClipPlane = _savedFar;
    }

    // ---------------------------------------------------------------------------------------------
    // Audio
    // ---------------------------------------------------------------------------------------------

    private void UpdateAudio()
    {
        if (!muffleAudio) return;

        if (_listener == null || !_listener.isActiveAndEnabled)
        {
            _listener = FindFirstObjectByType<AudioListener>();
            _lowPass = null;
            if (_listener == null) return;
        }

        if (_lowPass == null)
        {
            if (Weight <= 0f) return; // Don't add a filter to the listener until it is ever needed.
            _lowPass = _listener.GetComponent<AudioLowPassFilter>();
            if (_lowPass == null) _lowPass = _listener.gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.lowpassResonanceQ = 1f;
        }

        _lowPass.enabled = Weight > 0f;
        // Log-space blend so the muffle comes in evenly to the ear.
        _lowPass.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(Mathf.Max(100f, muffledCutoff)), Weight));
    }

    // ---------------------------------------------------------------------------------------------
    // Lake
    // ---------------------------------------------------------------------------------------------

    private bool ResolveLake()
    {
        if (_lakeResolved && lakeSurface != null) return true;
        _lakeResolved = false;

        if (lakeSurface == null)
        {
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                Material m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == LakeShaderName)
                {
                    lakeSurface = r;
                    break;
                }
            }
        }
        if (lakeSurface == null) return false;

        Bounds b = lakeSurface.bounds;
        _surfaceY = b.center.y;
        _footprint = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        _terrain = Terrain.activeTerrain;

        var filter = lakeSurface.GetComponent<MeshFilter>();
        _lakeMesh = filter != null ? filter.sharedMesh : null;

        if (undersideMaterial != null && _undersideInstance == null)
        {
            // Borrow the lake's own ripple textures, flow and baked depth so the two sides agree.
            _undersideInstance = new Material(undersideMaterial) { name = undersideMaterial.name + " (Lake)" };
            Material lake = lakeSurface.sharedMaterial;
            CopyTexture(lake, _undersideInstance, "_NormalA");
            CopyTexture(lake, _undersideInstance, "_NormalB");
            CopyTexture(lake, _undersideInstance, "_DepthMap");
            CopyVector(lake, _undersideInstance, "_NormalTiling");
            CopyVector(lake, _undersideInstance, "_FlowA");
            CopyVector(lake, _undersideInstance, "_FlowB");
            CopyVector(lake, _undersideInstance, "_DepthRect");
            if (lake != null && lake.HasProperty("_DepthMax")) _undersideInstance.SetFloat("_DepthMax", lake.GetFloat("_DepthMax"));
        }

        _lakeResolved = true;
        return true;
    }

    private static void CopyTexture(Material from, Material to, string name)
    {
        if (from != null && from.HasProperty(name) && to.HasProperty(name)) to.SetTexture(name, from.GetTexture(name));
    }

    private static void CopyVector(Material from, Material to, string name)
    {
        if (from != null && from.HasProperty(name) && to.HasProperty(name)) to.SetVector(name, from.GetVector(name));
    }

    private float GroundHeight(Vector3 p)
    {
        if (_terrain == null) _terrain = Terrain.activeTerrain;
        return _terrain != null ? _terrain.SampleHeight(p) + _terrain.transform.position.y : float.NegativeInfinity;
    }

    private static float Luminance(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;

    // ---------------------------------------------------------------------------------------------
    // Meshes
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A grid over the submerged part of the lakebed, lifted just off the terrain, with the water depth at
    /// each vertex in uv.x. Built once, the first time the camera goes under.
    /// </summary>
    private Mesh BuildCausticMesh()
    {
        if (_terrain == null) _terrain = Terrain.activeTerrain;
        if (_terrain == null) return null;

        float step = Mathf.Max(0.25f, causticGridSpacing);
        int nx = Mathf.CeilToInt(_footprint.width / step) + 1;
        int nz = Mathf.CeilToInt(_footprint.height / step) + 1;

        var heights = new float[nx * nz];
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            var p = new Vector3(_footprint.xMin + x * step, 0f, _footprint.yMin + z * step);
            heights[z * nx + x] = GroundHeight(p);
        }

        var verts = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        var index = new int[nx * nz];
        for (int i = 0; i < index.Length; i++) index[i] = -1;

        int Vertex(int x, int z)
        {
            int k = z * nx + x;
            if (index[k] >= 0) return index[k];
            float h = heights[k];
            index[k] = verts.Count;
            verts.Add(new Vector3(_footprint.xMin + x * step, h + causticLift, _footprint.yMin + z * step));
            uvs.Add(new Vector2(Mathf.Max(0f, _surfaceY - h), 0f));
            return index[k];
        }

        for (int z = 0; z < nz - 1; z++)
        for (int x = 0; x < nx - 1; x++)
        {
            bool wet = heights[z * nx + x] < _surfaceY || heights[z * nx + x + 1] < _surfaceY
                    || heights[(z + 1) * nx + x] < _surfaceY || heights[(z + 1) * nx + x + 1] < _surfaceY;
            if (!wet) continue;

            int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x, z + 1), d = Vertex(x + 1, z + 1);
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        if (tris.Count == 0) return null;

        var mesh = new Mesh { name = "Underwater Caustics" };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Quads whose four corners share a random seed position; the shader wraps them around the camera.</summary>
    private static Mesh BuildMotesMesh(int count)
    {
        count = Mathf.Clamp(count, 1, 4000);
        var verts = new Vector3[count * 4];
        var uvs = new Vector4[count * 4];
        var tris = new int[count * 6];
        var rng = new System.Random(1927);

        for (int i = 0; i < count; i++)
        {
            var seed = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            // Mostly fine silt, a few larger bubbles.
            float size = rng.NextDouble() < 0.15 ? 0.025f + (float)rng.NextDouble() * 0.03f : 0.008f + (float)rng.NextDouble() * 0.012f;
            float phase = (float)rng.NextDouble();
            for (int c = 0; c < 4; c++)
            {
                verts[i * 4 + c] = seed;
                float cx = (c == 0 || c == 2) ? -1f : 1f;
                float cy = c < 2 ? -1f : 1f;
                uvs[i * 4 + c] = new Vector4(cx, cy, size, phase);
            }
            int v = i * 4, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = "Underwater Motes" };
        mesh.vertices = verts;
        mesh.SetUVs(0, uvs);
        mesh.triangles = tris;
        // Positions are rewritten in the shader around the camera: never let the CPU cull it.
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return mesh;
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "Underwater Overlay" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return mesh;
    }

    /// <summary>A quad filling the view just past the near plane.</summary>
    private static Matrix4x4 OverlayMatrix(Camera cam)
    {
        float d = cam.nearClipPlane * 1.5f;
        float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        float w = h * cam.aspect;
        return cam.transform.localToWorldMatrix * Matrix4x4.TRS(new Vector3(0f, 0f, d), Quaternion.identity, new Vector3(w, h, 1f));
    }
}
