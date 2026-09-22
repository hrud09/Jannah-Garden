using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the placement lattice and the footprint highlight on the ground.
///
/// <para><b>Why a displaced mesh and not a decal or a flat quad:</b> the garden is real terrain, so a
/// single flat quad would cut through every slope it crosses. A URP Decal Projector would drape
/// correctly but needs the Decal renderer feature enabled and costs a screen-space pass on every
/// frame it is alive — a poor trade on the phones this ships to. A mesh whose vertices are lifted to
/// terrain height is two draw calls, no renderer features, and only recomputes its heights when the
/// player actually walks somewhere.</para>
///
/// <para>Both meshes are built directly in world space with the transform left at identity, so there is
/// no local/world bookkeeping to get subtly wrong when the terrain is not at the origin.</para>
///
/// Everything here is presentation. It reads <see cref="GardenGrid"/> and never writes to it.
/// </summary>
public class PlacementGridView : MonoBehaviour
{
    [Header("Grid Sheet")]
    [Tooltip("Vertices per side of the terrain-following grid mesh. Higher hugs bumpy ground more " +
             "closely at the cost of a slower (but rare) rebuild.")]
    [Range(8, 128)]
    public int gridSubdivisions = 48;

    [Tooltip("Extra metres of grid drawn beyond the placement radius, so the sheet's own edge is " +
             "never visible inside the area that has already faded out.")]
    public float gridMargin = 3f;

    [Tooltip("How far the player must move before the sheet's heights are re-sampled from the terrain.")]
    public float rebuildMoveThreshold = 1f;

    [Tooltip("Metres the overlay floats above the ground. Just enough to clear z-fighting on top of " +
             "the shader's own polygon offset.")]
    public float groundOffset = 0.02f;

    [Header("Footprint Highlight")]
    [Range(1, 32)]
    public int footprintSubdivisions = 6;

    // Fill left fully transparent on purpose: a solid tint over textured ground blends unevenly with
    // whatever is underneath (sand, grass, dirt) and reads as blotchy patches rather than a placement
    // outline. The border and interior cell lines alone are enough to show the footprint and its validity.
    public Color validFill = new Color(0.35f, 1f, 0.45f, 0f);
    public Color validBorder = new Color(0.5f, 1f, 0.6f, 0.85f);
    public Color invalidFill = new Color(1f, 0.32f, 0.3f, 0f);
    public Color invalidBorder = new Color(1f, 0.45f, 0.42f, 0.9f);

    [Header("Occupied Areas")]
    [Tooltip("Extra metres beyond the placement radius within which already-placed items' footprints " +
             "are outlined, so the player can see taken ground before aiming into it.")]
    public float occupiedAreaMargin = 1.5f;

    [Header("Fade")]
    [Tooltip("Seconds for the overlay to fade in when a placement starts and out when it ends.")]
    [Range(0.01f, 1f)]
    public float fadeDuration = 0.18f;

    private GardenGrid _grid;

    private MeshRenderer _gridRenderer;
    private Mesh _gridMesh;
    private Material _gridMaterial;

    private MeshRenderer _footprintRenderer;
    private Mesh _footprintMesh;
    private Material _footprintMaterial;

    private MeshRenderer _occupiedRenderer;
    private Mesh _occupiedMesh;
    private Material _occupiedMaterial;
    private readonly List<RectInt> _occupiedAreaBuffer = new List<RectInt>();

    private Vector3[] _gridVertices;
    private Vector3 _lastGridCenter = new Vector3(float.MaxValue, 0f, float.MaxValue);
    private float _lastGridRadius = -1f;

    private RectInt _lastFootprintArea;
    private bool _hasFootprint;
    private bool _lastFootprintValid = true;

    private float _alpha;
    private float _targetAlpha;

    private static readonly int GridOriginId = Shader.PropertyToID("_GridOrigin");
    private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int GlobalAlphaId = Shader.PropertyToID("_GlobalAlpha");
    private static readonly int SizeMetersId = Shader.PropertyToID("_SizeMeters");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");

    /// <summary>Wires the view to a lattice and builds its meshes. Safe to call more than once.</summary>
    public void Bind(GardenGrid grid)
    {
        _grid = grid;
        EnsureRenderers();
    }

    private void Awake()
    {
        EnsureRenderers();
        SetAlphaImmediate(0f);
    }

    private void EnsureRenderers()
    {
        if (_gridRenderer == null)
        {
            _gridMaterial = LoadMaterial("Placement/PlacementGrid");
            _gridMesh = new Mesh { name = "PlacementGridSheet" };
            _gridMesh.MarkDynamic();
            _gridRenderer = CreateChild("GridSheet", _gridMesh, _gridMaterial);
        }

        if (_footprintRenderer == null)
        {
            _footprintMaterial = LoadMaterial("Placement/PlacementFootprint");
            _footprintMesh = new Mesh { name = "PlacementFootprint" };
            _footprintMesh.MarkDynamic();
            _footprintRenderer = CreateChild("FootprintHighlight", _footprintMesh, _footprintMaterial);
        }

        if (_occupiedRenderer == null)
        {
            _occupiedMaterial = LoadMaterial("Placement/PlacementOccupiedArea");
            _occupiedMesh = new Mesh { name = "PlacementOccupiedAreas" };
            _occupiedMesh.MarkDynamic();
            _occupiedRenderer = CreateChild("OccupiedAreasHighlight", _occupiedMesh, _occupiedMaterial);
        }
    }

    /// <summary>
    /// Materials live under Resources rather than being resolved with <c>Shader.Find</c>: a shader that
    /// no scene references is stripped from the player build, and <c>Shader.Find</c> would then return
    /// null on device while working perfectly in the editor.
    /// </summary>
    private static Material LoadMaterial(string resourcePath)
    {
        Material source = Resources.Load<Material>(resourcePath);

        if (source == null)
        {
            Debug.LogError($"[PlacementGridView] Missing material at Resources/{resourcePath} — the overlay will not draw.");
            return null;
        }

        // Instanced so per-placement properties (fade, radius, tint) don't write back to the asset.
        return new Material(source);
    }

    private MeshRenderer CreateChild(string childName, Mesh mesh, Material material)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.enabled = false;

        return renderer;
    }

    private void OnDestroy()
    {
        if (_gridMesh != null) Destroy(_gridMesh);
        if (_footprintMesh != null) Destroy(_footprintMesh);
        if (_occupiedMesh != null) Destroy(_occupiedMesh);
        if (_gridMaterial != null) Destroy(_gridMaterial);
        if (_footprintMaterial != null) Destroy(_footprintMaterial);
        if (_occupiedMaterial != null) Destroy(_occupiedMaterial);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DRIVING
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Fades the lattice in around <paramref name="center"/>.</summary>
    public void Show(Vector3 center, float radius)
    {
        if (_grid == null || !_grid.IsReady) return;

        EnsureRenderers();
        _targetAlpha = 1f;
        RefreshGridSheet(center, radius);

        // Occupancy can have changed since the last time this view was hidden (an item was just placed
        // while standing still, so the move-threshold in RefreshGridSheet would otherwise skip the
        // rebuild) — always resync the outlines when a placement starts.
        BuildOccupiedAreasMesh(center, radius);
    }

    /// <summary>Fades everything out. The meshes stay built, ready for the next placement.</summary>
    public void Hide()
    {
        _targetAlpha = 0f;
        _hasFootprint = false;
        if (_footprintRenderer != null) _footprintRenderer.enabled = false;
        if (_occupiedRenderer != null) _occupiedRenderer.enabled = false;
    }

    /// <summary>Keeps the sheet centred on the player, rebuilding its heights only when worth it.</summary>
    public void SetCenter(Vector3 center, float radius)
    {
        if (_targetAlpha <= 0f) return;
        RefreshGridSheet(center, radius);
    }

    /// <summary>
    /// Moves the highlight onto <paramref name="area"/> and colours it by whether the item may be put
    /// there. Rebuilds the mesh only when the claimed cells actually change — at 0.25 m with hysteresis
    /// that is a handful of times per placement, not once per frame.
    /// </summary>
    public void SetFootprint(RectInt area, bool valid)
    {
        if (_grid == null || !_grid.IsReady || _footprintRenderer == null) return;

        bool areaChanged = !_hasFootprint || !area.Equals(_lastFootprintArea);

        if (areaChanged)
        {
            BuildFootprintMesh(area);
            _lastFootprintArea = area;
            _hasFootprint = true;
        }

        if (areaChanged || valid != _lastFootprintValid)
        {
            _lastFootprintValid = valid;

            if (_footprintMaterial != null)
            {
                _footprintMaterial.SetColor(ColorId, valid ? validFill : invalidFill);
                _footprintMaterial.SetColor(BorderColorId, valid ? validBorder : invalidBorder);
                _footprintMaterial.SetVector(SizeMetersId, new Vector4(
                    area.width * _grid.CellSize, area.height * _grid.CellSize, 0f, 0f));
                _footprintMaterial.SetFloat(CellSizeId, _grid.CellSize);
                _footprintMaterial.SetVector(GridOriginId, GridOriginVector());
            }
        }

        _footprintRenderer.enabled = _alpha > 0.001f;
    }

    private void Update()
    {
        if (Mathf.Approximately(_alpha, _targetAlpha)) return;

        float step = Time.unscaledDeltaTime / Mathf.Max(fadeDuration, 0.01f);
        SetAlphaImmediate(Mathf.MoveTowards(_alpha, _targetAlpha, step));
    }

    private void SetAlphaImmediate(float alpha)
    {
        _alpha = alpha;
        bool visible = _alpha > 0.001f;

        if (_gridMaterial != null) _gridMaterial.SetFloat(GlobalAlphaId, _alpha);
        if (_footprintMaterial != null) _footprintMaterial.SetFloat(GlobalAlphaId, _alpha);
        if (_occupiedMaterial != null) _occupiedMaterial.SetFloat(GlobalAlphaId, _alpha);

        if (_gridRenderer != null) _gridRenderer.enabled = visible;
        if (_footprintRenderer != null) _footprintRenderer.enabled = visible && _hasFootprint;
        if (_occupiedRenderer != null) _occupiedRenderer.enabled = visible && _occupiedAreaBuffer.Count > 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MESH BUILDING
    // ═══════════════════════════════════════════════════════════════════════════

    private Vector4 GridOriginVector()
    {
        // The shader generates its lines from world XZ, so it needs the same anchor the C# lattice uses
        // or the drawn lines would sit half a cell off the cells they claim to represent.
        Vector3 anchor = _grid.CellAreaCenter(Vector2Int.zero, Vector2Int.zero);
        return new Vector4(anchor.x, 0f, anchor.z, 0f);
    }

    private void RefreshGridSheet(Vector3 center, float radius)
    {
        if (_gridMaterial != null)
        {
            _gridMaterial.SetVector(CenterId, new Vector4(center.x, 0f, center.z, 0f));
            _gridMaterial.SetFloat(RadiusId, radius);
            _gridMaterial.SetFloat(CellSizeId, _grid.CellSize);
            _gridMaterial.SetVector(GridOriginId, GridOriginVector());
        }

        bool moved = (new Vector2(center.x - _lastGridCenter.x, center.z - _lastGridCenter.z)).sqrMagnitude
                     > rebuildMoveThreshold * rebuildMoveThreshold;

        if (!moved && Mathf.Approximately(radius, _lastGridRadius) && _gridVertices != null)
        {
            return;
        }

        BuildGridMesh(center, radius);
        BuildOccupiedAreasMesh(center, radius);
        _lastGridCenter = center;
        _lastGridRadius = radius;
    }

    private void BuildGridMesh(Vector3 center, float radius)
    {
        int n = Mathf.Max(2, gridSubdivisions);
        int side = n + 1;
        float extent = radius + gridMargin;
        float step = extent * 2f / n;

        bool rebuildTopology = _gridVertices == null || _gridVertices.Length != side * side;
        if (rebuildTopology) _gridVertices = new Vector3[side * side];

        for (int y = 0; y < side; y++)
        {
            float wz = center.z - extent + y * step;

            for (int x = 0; x < side; x++)
            {
                float wx = center.x - extent + x * step;
                _gridVertices[y * side + x] = new Vector3(wx, _grid.SampleHeight(new Vector3(wx, 0f, wz)) + groundOffset, wz);
            }
        }

        if (rebuildTopology)
        {
            _gridMesh.Clear();
            _gridMesh.indexFormat = side * side > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _gridMesh.vertices = _gridVertices;
            _gridMesh.triangles = BuildQuadIndices(n);
        }
        else
        {
            _gridMesh.vertices = _gridVertices;
        }

        _gridMesh.RecalculateBounds();
    }

    private void BuildFootprintMesh(RectInt area)
    {
        int n = Mathf.Max(1, footprintSubdivisions);
        int side = n + 1;

        var vertices = new Vector3[side * side];
        var uvs = new Vector2[side * side];

        Vector3 min = _grid.CellAreaCenter(area.min, Vector2Int.zero);
        float width = area.width * _grid.CellSize;
        float depth = area.height * _grid.CellSize;

        for (int y = 0; y < side; y++)
        {
            float v = (float)y / n;
            float wz = min.z + v * depth;

            for (int x = 0; x < side; x++)
            {
                float u = (float)x / n;
                float wx = min.x + u * width;

                int i = y * side + x;
                // Lifted slightly further than the sheet so the highlight always reads on top of it.
                vertices[i] = new Vector3(wx, _grid.SampleHeight(new Vector3(wx, 0f, wz)) + groundOffset * 1.5f, wz);
                uvs[i] = new Vector2(u, v);
            }
        }

        _footprintMesh.Clear();
        _footprintMesh.vertices = vertices;
        _footprintMesh.uv = uvs;
        _footprintMesh.triangles = BuildQuadIndices(n);
        _footprintMesh.RecalculateBounds();
    }

    /// <summary>
    /// Builds one combined mesh outlining every already-claimed footprint within
    /// <paramref name="radius"/> + <see cref="occupiedAreaMargin"/> of <paramref name="center"/>, so the
    /// player can see taken ground before aiming a new item into it. A single quad per area, each
    /// carrying its own size in metres on uv1 (see <c>PlacementOccupiedArea.shader</c>), so differently
    /// sized footprints can share one draw call instead of one material each.
    /// </summary>
    private void BuildOccupiedAreasMesh(Vector3 center, float radius)
    {
        if (_grid == null || !_grid.IsReady || _occupiedMesh == null) return;

        _occupiedAreaBuffer.Clear();

        float reach = Mathf.Max(0f, radius) + occupiedAreaMargin;
        float reachSqr = reach * reach;

        foreach (var kvp in _grid.AllAreas)
        {
            RectInt area = kvp.Value;
            Vector3 areaCenter = _grid.CellAreaCenter(area.min, area.size);
            float dx = areaCenter.x - center.x;
            float dz = areaCenter.z - center.z;
            if (dx * dx + dz * dz <= reachSqr) _occupiedAreaBuffer.Add(area);
        }

        int quadCount = _occupiedAreaBuffer.Count;
        var vertices = new Vector3[quadCount * 4];
        var uvs = new Vector2[quadCount * 4];
        var sizes = new Vector2[quadCount * 4];
        var triangles = new int[quadCount * 6];

        for (int i = 0; i < quadCount; i++)
        {
            RectInt area = _occupiedAreaBuffer[i];
            Vector3 min = _grid.CellAreaCenter(area.min, Vector2Int.zero);
            float width = area.width * _grid.CellSize;
            float depth = area.height * _grid.CellSize;
            var size = new Vector2(width, depth);

            int v = i * 4;

            vertices[v + 0] = LiftedCorner(min.x, min.z);
            vertices[v + 1] = LiftedCorner(min.x + width, min.z);
            vertices[v + 2] = LiftedCorner(min.x, min.z + depth);
            vertices[v + 3] = LiftedCorner(min.x + width, min.z + depth);

            uvs[v + 0] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(1f, 0f);
            uvs[v + 2] = new Vector2(0f, 1f);
            uvs[v + 3] = new Vector2(1f, 1f);

            sizes[v + 0] = size;
            sizes[v + 1] = size;
            sizes[v + 2] = size;
            sizes[v + 3] = size;

            int t = i * 6;
            // Same winding as BuildQuadIndices(1): (0,2,1),(1,2,3).
            triangles[t + 0] = v + 0;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        _occupiedMesh.Clear();
        _occupiedMesh.indexFormat = vertices.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _occupiedMesh.vertices = vertices;
        _occupiedMesh.uv = uvs;
        _occupiedMesh.uv2 = sizes;
        _occupiedMesh.triangles = triangles;
        _occupiedMesh.RecalculateBounds();

        if (_occupiedRenderer != null) _occupiedRenderer.enabled = quadCount > 0 && _alpha > 0.001f;
    }

    /// <summary>A footprint-outline corner, sampled onto the ground a touch above the lattice sheet so
    /// it never z-fights with it but still sits below the current ghost's own footprint highlight.</summary>
    private Vector3 LiftedCorner(float x, float z)
    {
        return new Vector3(x, _grid.SampleHeight(new Vector3(x, 0f, z)) + groundOffset * 1.2f, z);
    }

    /// <summary>Two triangles per cell of an <c>n</c>x<c>n</c> grid, wound to face up.</summary>
    private static int[] BuildQuadIndices(int n)
    {
        int side = n + 1;
        var indices = new int[n * n * 6];
        int t = 0;

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * side + x;

                indices[t++] = i;
                indices[t++] = i + side;
                indices[t++] = i + 1;

                indices[t++] = i + 1;
                indices[t++] = i + side;
                indices[t++] = i + side + 1;
            }
        }

        return indices;
    }
}
