using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds walking routes across the garden that keep to its painted paths.
///
/// <para><b>Why the terrain paint and not a NavMesh:</b> the garden's roads are not objects — they are
/// the dirt layer painted onto the terrain, the same paint <c>GardenTerrainRegions</c> reads to cut the
/// garden into areas. Reading that paint here means "the road" is the same thing to the area bake, the
/// minimap and the autopilot, and repainting a path reroutes the autopilot with no re-bake at all.</para>
///
/// <para><b>Roads are preferred, not required.</b> Every walkable cell can be crossed, but grass and
/// sand cost <see cref="OffRoadCost"/> times as much as road. That one rule covers stepping off the
/// grass onto the nearest path, crossing the odd gap in the paint, and walking the last stretch into an
/// area — without a separate "find the nearest road" phase that would need its own edge cases.</para>
///
/// <para>Water (the lake footprint, below the shore line) and slopes too steep for the character
/// controller are never crossed.</para>
///
/// <para>Built once from the terrain on first use; nothing at runtime writes to the terrain, so the
/// grid never goes stale within a session.</para>
/// </summary>
public sealed class GardenPathfinder
{
    // Same paint rules as GardenTerrainRegions (Editor) so road means the same thing to both.
    private const int LayerDirt = 1;
    private const float RoadThreshold = 0.35f;

    private const byte CellRoad = 0;
    private const byte CellOffRoad = 1;
    private const byte CellBlocked = 2;

    private const float Sqrt2 = 1.41421356f;

    public readonly int Res;
    public readonly float CellSize;
    public readonly Vector3 Origin;
    public readonly float OffRoadCost;

    private readonly Terrain _terrain;
    private readonly byte[] _kind;

    // Search scratch, reused between searches so a button press does not allocate a quarter-million
    // element arrays each time.
    private readonly float[] _cost;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private int _searchId;
    private readonly MinHeap _open = new MinHeap(1024);

    /// <summary>
    /// Builds the walk grid from <paramref name="terrain"/>'s dirt paint.
    /// </summary>
    /// <param name="lakeFootprint">World XZ rect of the lake; cells inside it below
    /// <paramref name="waterLine"/> are water. <see cref="Rect.zero"/> for no lake.</param>
    /// <param name="waterLine">World height at and below which ground inside the lake is water.</param>
    /// <param name="maxSlope">Steepest ground, in degrees, the player can walk up.</param>
    /// <param name="offRoadCost">How many times more a grass or sand cell costs than a road cell.</param>
    public GardenPathfinder(Terrain terrain, Rect lakeFootprint, float waterLine, float maxSlope, float offRoadCost)
    {
        _terrain = terrain;
        TerrainData td = terrain.terrainData;

        Res = td.alphamapResolution;
        CellSize = td.size.x / Res;
        Origin = terrain.transform.position;
        OffRoadCost = Mathf.Max(1f, offRoadCost);

        int n = Res * Res;
        _kind = new byte[n];
        _cost = new float[n];
        _parent = new int[n];
        _stamp = new int[n];

        bool hasDirt = td.alphamapLayers > LayerDirt;
        if (!hasDirt)
        {
            Debug.LogWarning("[GardenPathfinder] Terrain has no dirt layer — routes will ignore roads.");
        }

        // Read the alphamaps in strips: the whole map at once is Res² × layers floats, which is a
        // needless spike on a phone for data that is thrown away as soon as it is classified.
        const int Strip = 64;
        for (int y0 = 0; y0 < Res; y0 += Strip)
        {
            int rows = Mathf.Min(Strip, Res - y0);
            float[,,] alpha = hasDirt ? td.GetAlphamaps(0, y0, Res, rows) : null;

            for (int r = 0; r < rows; r++)
            {
                int y = y0 + r;
                float nz = (y + 0.5f) / Res;

                for (int x = 0; x < Res; x++)
                {
                    float nx = (x + 0.5f) / Res;
                    int i = y * Res + x;

                    if (td.GetSteepness(nx, nz) > maxSlope)
                    {
                        _kind[i] = CellBlocked;
                        continue;
                    }

                    if (lakeFootprint.width > 0f)
                    {
                        Vector2 world = new Vector2(Origin.x + nx * td.size.x, Origin.z + nz * td.size.z);
                        if (lakeFootprint.Contains(world) &&
                            td.GetInterpolatedHeight(nx, nz) + Origin.y <= waterLine)
                        {
                            _kind[i] = CellBlocked;
                            continue;
                        }
                    }

                    _kind[i] = hasDirt && alpha[r, x, LayerDirt] > RoadThreshold ? CellRoad : CellOffRoad;
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  QUERIES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The cell under <paramref name="world"/>, or -1 when it is off the terrain.</summary>
    public int CellAt(Vector3 world)
    {
        int x = Mathf.FloorToInt((world.x - Origin.x) / CellSize);
        int y = Mathf.FloorToInt((world.z - Origin.z) / CellSize);
        return x < 0 || y < 0 || x >= Res || y >= Res ? -1 : y * Res + x;
    }

    /// <summary>World position of the centre of <paramref name="cell"/>, at terrain height.</summary>
    public Vector3 CellCentre(int cell)
    {
        Vector3 p = FlatCentre(cell);
        p.y = _terrain.SampleHeight(p) + Origin.y;
        return p;
    }

    private Vector3 FlatCentre(int cell)
    {
        int cy = cell / Res, cx = cell % Res;
        return new Vector3(Origin.x + (cx + 0.5f) * CellSize, 0f, Origin.z + (cy + 0.5f) * CellSize);
    }

    /// <summary>True when <paramref name="world"/> is on painted road.</summary>
    public bool IsRoad(Vector3 world)
    {
        int c = CellAt(world);
        return c >= 0 && _kind[c] == CellRoad;
    }

    /// <summary>
    /// The cheapest route from <paramref name="from"/> to <paramref name="to"/>, as world-space
    /// waypoints ending exactly at <paramref name="to"/>. Null when there is no way there.
    /// </summary>
    public List<Vector3> FindPath(Vector3 from, Vector3 to)
    {
        int goal = CellAt(to);
        if (goal < 0) return null;

        List<Vector3> path = Search(from, c => c == goal, goal);
        if (path != null) path[path.Count - 1] = to;
        return path;
    }

    /// <summary>
    /// The cheapest route from <paramref name="from"/> to whichever of <paramref name="targets"/> is
    /// nearest <em>by that route</em> — not as the crow flies, which across a garden of fenced-off
    /// pockets is often the wrong answer. Returns the index of the target reached through
    /// <paramref name="reachedIndex"/>, or null and -1 when none can be reached.
    /// </summary>
    public List<Vector3> FindPathToNearest(Vector3 from, IList<Vector3> targets, out int reachedIndex)
    {
        reachedIndex = -1;
        if (targets == null || targets.Count == 0) return null;

        var goalToIndex = new Dictionary<int, int>();
        for (int i = 0; i < targets.Count; i++)
        {
            int c = CellAt(targets[i]);
            if (c >= 0 && !goalToIndex.ContainsKey(c)) goalToIndex.Add(c, i);
        }

        if (goalToIndex.Count == 0) return null;

        int reachedCell = -1;
        List<Vector3> path = Search(from, c =>
        {
            if (!goalToIndex.ContainsKey(c)) return false;
            reachedCell = c;
            return true;
        }, -1);

        if (path == null) return null;

        reachedIndex = goalToIndex[reachedCell];
        path[path.Count - 1] = targets[reachedIndex];
        return path;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SEARCH
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A* over the cell grid — or plain Dijkstra when <paramref name="heuristicGoal"/> is -1, which is
    /// what a search for the nearest of several goals needs to stay correct.
    /// </summary>
    private List<Vector3> Search(Vector3 from, Func<int, bool> isGoal, int heuristicGoal)
    {
        int start = CellAt(from);
        if (start < 0) return null;

        _searchId++;
        if (_searchId == int.MaxValue)
        {
            Array.Clear(_stamp, 0, _stamp.Length);
            _searchId = 1;
        }

        _open.Clear();
        Touch(start, 0f, -1);
        _open.Push(start, Heuristic(start, heuristicGoal));

        int found = -1;

        while (_open.Count > 0)
        {
            _open.Pop(out int c, out float f);

            // Lazy deletion: a cell can sit in the heap more than once after its cost improved.
            if (f - Heuristic(c, heuristicGoal) > _cost[c] + 0.0001f) continue;

            if (isGoal(c))
            {
                found = c;
                break;
            }

            int cy = c / Res, cx = c % Res;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = cy + dy;
                if (ny < 0 || ny >= Res) continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = cx + dx;
                    if (nx < 0 || nx >= Res) continue;

                    int ni = ny * Res + nx;
                    // A blocked goal (a target right on the shore) is still allowed as the final step.
                    if (_kind[ni] == CellBlocked && ni != heuristicGoal) continue;

                    bool diagonal = dx != 0 && dy != 0;

                    // No squeezing diagonally between two blocked cells — the capsule would not fit.
                    if (diagonal && (_kind[cy * Res + nx] == CellBlocked || _kind[ny * Res + cx] == CellBlocked))
                        continue;

                    float step = (diagonal ? Sqrt2 : 1f) * CellSize * StepCost(ni);
                    float g = _cost[c] + step;

                    if (_stamp[ni] == _searchId && g >= _cost[ni]) continue;

                    Touch(ni, g, c);
                    _open.Push(ni, g + Heuristic(ni, heuristicGoal));
                }
            }
        }

        if (found < 0) return null;

        var cells = new List<int>();
        for (int c = found; c >= 0; c = _parent[c]) cells.Add(c);
        cells.Reverse();

        return Simplify(cells);
    }

    private float StepCost(int cell) => _kind[cell] == CellRoad ? 1f : OffRoadCost;

    private void Touch(int cell, float cost, int parent)
    {
        _stamp[cell] = _searchId;
        _cost[cell] = cost;
        _parent[cell] = parent;
    }

    /// <summary>Octile distance at road cost — never overestimates, so A* stays optimal.</summary>
    private float Heuristic(int cell, int goal)
    {
        if (goal < 0) return 0f;

        int dx = Mathf.Abs(cell % Res - goal % Res);
        int dy = Mathf.Abs(cell / Res - goal / Res);
        int min = Mathf.Min(dx, dy), max = Mathf.Max(dx, dy);
        return (max - min + min * Sqrt2) * CellSize;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SMOOTHING
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Turns the staircase of grid cells into a few straight legs.
    ///
    /// <para>A leg may cut straight from one cell to a later one only when the straight line crosses no
    /// blocked ground and no more grass than the route it replaces. That keeps the walk on the road
    /// through bends — a plain line-of-sight test would happily cut every corner across the lawn.</para>
    /// </summary>
    private List<Vector3> Simplify(List<int> cells)
    {
        var points = new List<Vector3>();
        if (cells.Count == 0) return points;

        // Prefix count of off-road cells along the route, so any span's count is one subtraction.
        var offRoadBefore = new int[cells.Count + 1];
        for (int i = 0; i < cells.Count; i++)
        {
            offRoadBefore[i + 1] = offRoadBefore[i] + (_kind[cells[i]] == CellRoad ? 0 : 1);
        }

        int anchor = 0;
        points.Add(CellCentre(cells[0]));

        while (anchor < cells.Count - 1)
        {
            int best = anchor + 1;

            for (int j = anchor + 2; j < cells.Count; j++)
            {
                int allowedOffRoad = offRoadBefore[j + 1] - offRoadBefore[anchor];
                if (!LineIsNoWorse(cells[anchor], cells[j], allowedOffRoad)) break;
                best = j;
            }

            points.Add(CellCentre(cells[best]));
            anchor = best;
        }

        return points;
    }

    private bool LineIsNoWorse(int fromCell, int toCell, int allowedOffRoad)
    {
        Vector3 a = FlatCentre(fromCell), b = FlatCentre(toCell);

        float length = Vector3.Distance(a, b);
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / (CellSize * 0.5f)));

        int offRoad = 0;
        int last = -1;

        for (int s = 0; s <= samples; s++)
        {
            int c = CellAt(Vector3.Lerp(a, b, (float)s / samples));
            if (c < 0) return false;
            if (c == last) continue;
            last = c;

            if (_kind[c] == CellBlocked) return false;
            if (_kind[c] != CellRoad && ++offRoad > allowedOffRoad) return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAP
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Binary min-heap of (cell, priority). No decrease-key: stale entries are skipped on pop.</summary>
    private sealed class MinHeap
    {
        private int[] _items;
        private float[] _keys;

        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            _items = new int[capacity];
            _keys = new float[capacity];
        }

        public void Clear() => Count = 0;

        public void Push(int item, float key)
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, Count * 2);
                Array.Resize(ref _keys, Count * 2);
            }

            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_keys[parent] <= key) break;
                _items[i] = _items[parent];
                _keys[i] = _keys[parent];
                i = parent;
            }

            _items[i] = item;
            _keys[i] = key;
        }

        public void Pop(out int item, out float key)
        {
            item = _items[0];
            key = _keys[0];

            int lastItem = _items[--Count];
            float lastKey = _keys[Count];

            int i = 0;
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= Count) break;
                if (child + 1 < Count && _keys[child + 1] < _keys[child]) child++;
                if (_keys[child] >= lastKey) break;
                _items[i] = _items[child];
                _keys[i] = _keys[child];
                i = child;
            }

            if (Count > 0)
            {
                _items[i] = lastItem;
                _keys[i] = lastKey;
            }
        }
    }
}
