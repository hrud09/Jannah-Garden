using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Why a spot can't be built on. <see cref="PlacementValidity.Valid"/> is the only placeable value;
/// everything else is a bit flag so the UI can explain the *first* real problem rather than a generic
/// "you can't put that there".
/// </summary>
[System.Flags]
public enum PlacementValidity
{
    Valid = 0,
    OutOfRadius = 1 << 0,
    OffTerrain = 1 << 1,
    Occupied = 1 << 2,
    TooSteep = 1 << 3
}

/// <summary>
/// The lattice every placed item snaps to, plus the record of which cells are already spoken for.
///
/// Pure spatial bookkeeping — it never touches prefabs, saves or UI. <see cref="ItemPlacementManager"/>
/// drives it; <see cref="PlacementGridView"/> only reads from it.
///
/// <para><b>Why the origin comes from the Terrain transform:</b> placements round-trip through Firebase
/// to the player's other devices as raw world positions. If the lattice were anchored to anything
/// computed at runtime (the player, the camera, the first item placed) two devices could disagree on
/// where the cell boundaries fall, and a garden that was flush on a phone would be a cell out on a
/// tablet. The Terrain's transform is scene data: identical everywhere, forever.</para>
/// </summary>
public class GardenGrid : MonoBehaviour
{
    public static GardenGrid Instance { get; private set; }

    [Header("Lattice")]
    [Tooltip("Edge length of one cell in metres. Smaller means finer control and tighter packing; the " +
             "cost is only in how many cells each item claims, never in memory for the terrain itself " +
             "(occupancy is sparse). 0.25 lets decorations sit essentially flush while still snapping.")]
    [Range(0.05f, 2f)]
    public float cellSize = 0.25f;

    [Tooltip("The terrain the lattice is anchored to and heights are sampled from. Left empty, the one " +
             "on ItemPlacementManager.terrainCollider is used.")]
    public Terrain terrain;

    [Header("Ground Rules")]
    [Tooltip("Steepest ground an item may stand on, in degrees, measured at the corners of its " +
             "footprint. Stops buildings from being pasted flat onto a cliff face.")]
    [Range(0f, 90f)]
    public float maxSlopeDegrees = 30f;

    [Tooltip("How far into the next cell the aim point must travel before the ghost actually moves " +
             "there, as a fraction of a cell. Without this the ghost chatters between two cells " +
             "whenever the crosshair lands near a boundary — at 0.25 m cells that is most of the time.")]
    [Range(0.5f, 0.95f)]
    public float snapHysteresis = 0.62f;

    /// <summary>
    /// How many items currently cover each cell. A count rather than a single owner id because legacy
    /// gardens (placed before the grid existed) contain genuine overlaps: with one owner per cell,
    /// releasing the second item would wrongly free cells the first one still stands on.
    /// </summary>
    private readonly Dictionary<Vector2Int, int> _cellRefs = new Dictionary<Vector2Int, int>();

    /// <summary>Which cells each placed item claimed, so <see cref="Release"/> can give back exactly those.</summary>
    private readonly Dictionary<string, RectInt> _areas = new Dictionary<string, RectInt>();

    private Vector3 _origin;
    private Vector2 _terrainSize;

    /// <summary>True once a terrain has been resolved and the lattice has a fixed anchor.</summary>
    public bool IsReady { get; private set; }

    public float CellSize => cellSize;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Anchors the lattice. Safe to call more than once; calling it with a different terrain clears
    /// occupancy, because cell coordinates recorded against the old anchor would be meaningless.
    /// </summary>
    public void Bind(Terrain boundTerrain)
    {
        if (boundTerrain == null)
        {
            Debug.LogWarning("[GardenGrid] Bind called with no terrain — grid placement stays disabled.");
            IsReady = false;
            return;
        }

        if (terrain == boundTerrain && IsReady) return;

        if (IsReady)
        {
            _cellRefs.Clear();
            _areas.Clear();
        }

        terrain = boundTerrain;
        _origin = terrain.transform.position;
        TerrainData data = terrain.terrainData;
        _terrainSize = data != null ? new Vector2(data.size.x, data.size.z) : new Vector2(float.MaxValue, float.MaxValue);
        IsReady = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  LATTICE MATH
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The cell containing <paramref name="world"/>.</summary>
    public Vector2Int WorldToCell(Vector3 world)
    {
        return new Vector2Int(
            Mathf.FloorToInt((world.x - _origin.x) / cellSize),
            Mathf.FloorToInt((world.z - _origin.z) / cellSize));
    }

    /// <summary>
    /// Where a footprint anchored at <paramref name="minCell"/> puts the item's pivot, in XZ.
    ///
    /// The pivot sits at the centre of the claimed block, which is what makes odd footprints land on
    /// cell centres and even ones on cell boundaries with no special-casing — and is why two items of
    /// the same footprint always tile flush against each other instead of leaving a sliver of a gap.
    /// Y is left at zero; the caller resamples ground height (see <see cref="SampleHeight"/>).
    /// </summary>
    public Vector3 CellAreaCenter(Vector2Int minCell, Vector2Int footprint)
    {
        return new Vector3(
            _origin.x + (minCell.x + footprint.x * 0.5f) * cellSize,
            0f,
            _origin.z + (minCell.y + footprint.y * 0.5f) * cellSize);
    }

    /// <summary>The anchor cell that centres <paramref name="footprint"/> as close as possible to <paramref name="world"/>.</summary>
    public Vector2Int NearestAnchor(Vector3 world, Vector2Int footprint)
    {
        return new Vector2Int(
            Mathf.RoundToInt((world.x - _origin.x) / cellSize - footprint.x * 0.5f),
            Mathf.RoundToInt((world.z - _origin.z) / cellSize - footprint.y * 0.5f));
    }

    /// <summary>
    /// The block of cells a footprint of <paramref name="footprint"/> centred on <paramref name="world"/>
    /// covers, <em>without</em> snapping. Used for items already standing at an arbitrary position —
    /// items loaded from a save written before the grid existed.
    /// </summary>
    public RectInt AreaCovering(Vector3 world, Vector2Int footprint)
    {
        Vector3 min = new Vector3(
            world.x - footprint.x * cellSize * 0.5f,
            0f,
            world.z - footprint.y * cellSize * 0.5f);

        return new RectInt(WorldToCell(min), footprint);
    }

    /// <summary>
    /// Snaps <paramref name="world"/> to the lattice, holding the previous anchor until the aim point
    /// has moved <see cref="snapHysteresis"/> of a cell past it.
    ///
    /// <paramref name="anchor"/> and <paramref name="hasAnchor"/> are the caller's per-placement state:
    /// reset <paramref name="hasAnchor"/> to false whenever the footprint changes (a rotation) or a new
    /// placement begins, so the ghost re-centres immediately instead of dragging the old anchor along.
    /// </summary>
    public Vector3 Snap(Vector3 world, Vector2Int footprint, ref Vector2Int anchor, ref bool hasAnchor)
    {
        float fx = (world.x - _origin.x) / cellSize - footprint.x * 0.5f;
        float fz = (world.z - _origin.z) / cellSize - footprint.y * 0.5f;

        if (!hasAnchor)
        {
            anchor = new Vector2Int(Mathf.RoundToInt(fx), Mathf.RoundToInt(fz));
            hasAnchor = true;
        }
        else
        {
            if (Mathf.Abs(fx - anchor.x) > snapHysteresis) anchor.x = Mathf.RoundToInt(fx);
            if (Mathf.Abs(fz - anchor.y) > snapHysteresis) anchor.y = Mathf.RoundToInt(fz);
        }

        return CellAreaCenter(anchor, footprint);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GROUND
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>World-space ground height under <paramref name="world"/>, or its own Y if unbound.</summary>
    public float SampleHeight(Vector3 world)
    {
        if (terrain == null) return world.y;
        return terrain.SampleHeight(world) + terrain.transform.position.y;
    }

    /// <summary>Puts <paramref name="world"/> on the ground, keeping its XZ.</summary>
    public Vector3 DropToGround(Vector3 world)
    {
        return new Vector3(world.x, SampleHeight(world), world.z);
    }

    /// <summary>Steepest slope, in degrees, across the four corners of <paramref name="area"/>.</summary>
    public float MaxSlopeOver(RectInt area)
    {
        TerrainData data = terrain != null ? terrain.terrainData : null;
        if (data == null) return 0f;

        float worst = 0f;

        for (int i = 0; i < 4; i++)
        {
            int cx = (i & 1) == 0 ? area.xMin : area.xMax;
            int cy = (i & 2) == 0 ? area.yMin : area.yMax;

            float wx = _origin.x + cx * cellSize;
            float wz = _origin.z + cy * cellSize;

            float u = Mathf.Clamp01((wx - _origin.x) / _terrainSize.x);
            float v = Mathf.Clamp01((wz - _origin.z) / _terrainSize.y);

            float slope = Vector3.Angle(data.GetInterpolatedNormal(u, v), Vector3.up);
            if (slope > worst) worst = slope;
        }

        return worst;
    }

    /// <summary>True when every cell of <paramref name="area"/> lies inside the terrain's own extent.</summary>
    public bool IsInsideTerrain(RectInt area)
    {
        if (terrain == null || terrain.terrainData == null) return true;

        return area.xMin >= 0
            && area.yMin >= 0
            && area.xMax * cellSize <= _terrainSize.x
            && area.yMax * cellSize <= _terrainSize.y;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  OCCUPANCY
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>True when nothing already stands on any cell of <paramref name="area"/>.</summary>
    public bool IsAreaFree(RectInt area)
    {
        for (int x = area.xMin; x < area.xMax; x++)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            {
                if (_cellRefs.ContainsKey(new Vector2Int(x, y))) return false;
            }
        }

        return true;
    }

    /// <summary>True when any cell of <paramref name="area"/> is already claimed.</summary>
    public bool IsCellOccupied(Vector2Int cell) => _cellRefs.ContainsKey(cell);

    /// <summary>
    /// Marks <paramref name="area"/> as taken by <paramref name="ownerId"/>.
    ///
    /// Never refuses: a garden restored from a save written before the grid existed can contain items
    /// that genuinely overlap, and dropping one of them because the lattice disagrees would destroy
    /// something the player paid for. Conflicts just raise the cell's reference count.
    /// </summary>
    public void Occupy(RectInt area, string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId)) return;

        // Re-occupying under the same id (a relocate put back where it started) must not double-count.
        if (_areas.ContainsKey(ownerId)) Release(ownerId);

        _areas[ownerId] = area;

        for (int x = area.xMin; x < area.xMax; x++)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                _cellRefs.TryGetValue(cell, out int refs);
                _cellRefs[cell] = refs + 1;
            }
        }
    }

    /// <summary>Gives back every cell <paramref name="ownerId"/> claimed. No-op for an unknown id.</summary>
    public void Release(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId) || !_areas.TryGetValue(ownerId, out RectInt area)) return;

        _areas.Remove(ownerId);

        for (int x = area.xMin; x < area.xMax; x++)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                if (!_cellRefs.TryGetValue(cell, out int refs)) continue;

                if (refs <= 1) _cellRefs.Remove(cell);
                else _cellRefs[cell] = refs - 1;
            }
        }
    }

    /// <summary>Forgets every claim. Used when the garden is torn down for a rebuild.</summary>
    public void ClearAll()
    {
        _cellRefs.Clear();
        _areas.Clear();
    }

    /// <summary>The cells <paramref name="ownerId"/> holds, if it holds any.</summary>
    public bool TryGetArea(string ownerId, out RectInt area) => _areas.TryGetValue(ownerId, out area);

    /// <summary>
    /// Every cell block currently claimed, keyed by owner id. Read-only presentation data for
    /// <see cref="PlacementGridView"/> — callers must not mutate the returned areas' bookkeeping
    /// through anything but <see cref="Occupy"/>/<see cref="Release"/>.
    /// </summary>
    public IReadOnlyDictionary<string, RectInt> AllAreas => _areas;

    // ═══════════════════════════════════════════════════════════════════════════
    //  VALIDITY
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Everything wrong with standing an item on <paramref name="area"/>, as one flag set.
    /// <paramref name="radiusCenter"/>/<paramref name="radius"/> describe the reachable bubble around
    /// the player; pass a negative radius to skip that test.
    /// </summary>
    public PlacementValidity Evaluate(RectInt area, Vector3 radiusCenter, float radius, bool checkOccupancy)
    {
        var result = PlacementValidity.Valid;

        if (checkOccupancy && !IsAreaFree(area)) result |= PlacementValidity.Occupied;
        if (!IsInsideTerrain(area)) result |= PlacementValidity.OffTerrain;
        if (MaxSlopeOver(area) > maxSlopeDegrees) result |= PlacementValidity.TooSteep;

        if (radius >= 0f)
        {
            Vector3 center = CellAreaCenter(area.min, area.size);
            float dx = center.x - radiusCenter.x;
            float dz = center.z - radiusCenter.z;

            // The whole block has to be reachable, not just its pivot — otherwise a large item could
            // be anchored just inside the bubble with most of its body hanging out of it. Measuring
            // the pivot against a radius shrunk by the block's half-diagonal is the same test, done
            // without walking the corners.
            //
            // The caller is expected to have already clamped the aim point to that shrunk radius, so
            // in practice this only ever catches the fraction of a cell that snapping can add on top.
            // A one-cell allowance absorbs exactly that, and nothing more.
            float half = new Vector2(area.width, area.height).magnitude * cellSize * 0.5f;
            float reach = Mathf.Max(0f, radius - half) + cellSize;

            if (Mathf.Sqrt(dx * dx + dz * dz) > reach) result |= PlacementValidity.OutOfRadius;
        }

        return result;
    }
}
