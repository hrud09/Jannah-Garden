using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared geography for Auto Travel and the Jungle Orb Run: the painted roads (the same paint
/// <see cref="GardenPathfinder"/> and the autopilot follow), which side of the map is jungle, and
/// road-following routes between points.
/// </summary>
public static class RideRoutes
{
    // Same settings as PlayerAutoWalker, so "road" and "water" mean the same thing everywhere.
    private const float WaterLine = 4.39f;
    private const float MaxSlope = 40f;
    private const float OffRoadCost = 8f;

    private static GardenPathfinder _pathfinder;
    private static Terrain _terrain;
    private static Rect _lake;
    private static bool _jungleResolved;
    private static Rect _jungle;
    private static Rect _playArea;

    public static Terrain Terrain
    {
        get
        {
            if (_terrain == null)
            {
                GameObject named = GameObject.Find("LandTerrain");
                _terrain = named != null ? named.GetComponent<Terrain>() : null;
                if (_terrain == null) _terrain = Terrain.activeTerrain;
            }
            return _terrain;
        }
    }

    public static GardenPathfinder Pathfinder
    {
        get
        {
            if (_pathfinder == null && Terrain != null)
            {
                _lake = ResolveLake();
                _pathfinder = new GardenPathfinder(Terrain, _lake, WaterLine, MaxSlope, OffRoadCost);
            }
            return _pathfinder;
        }
    }

    public static float GroundHeight(Vector3 p)
    {
        Terrain t = Terrain;
        return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
    }

    /// <summary>
    /// True inside the jungle: the footprint of Environment/Jungle's trees, below the garden. The jungle
    /// wraps the south of the map, so its box's northern edge is where the garden begins.
    /// </summary>
    public static bool InJungle(Vector3 p)
    {
        ResolveZones();
        return _jungle.width > 0f && _jungle.Contains(new Vector2(p.x, p.z));
    }

    /// <summary>Inside the boundary walls, with <paramref name="margin"/> metres to spare.</summary>
    public static bool InPlayArea(Vector3 p, float margin)
    {
        ResolveZones();
        if (_playArea.width <= 0f) return true;
        return p.x > _playArea.xMin + margin && p.x < _playArea.xMax - margin &&
               p.z > _playArea.yMin + margin && p.z < _playArea.yMax - margin;
    }

    /// <summary>
    /// Points on painted road, every <paramref name="spacing"/> metres, kept inside the walls and off the
    /// lake. <paramref name="jungle"/> picks the jungle's roads or the garden's.
    /// </summary>
    public static List<Vector3> RoadPoints(bool jungle, float spacing)
    {
        var points = new List<Vector3>();
        GardenPathfinder pf = Pathfinder;
        Terrain t = Terrain;
        if (pf == null || t == null) return points;

        Vector3 o = t.transform.position, size = t.terrainData.size;
        for (float x = o.x + spacing * 0.5f; x < o.x + size.x; x += spacing)
        {
            for (float z = o.z + spacing * 0.5f; z < o.z + size.z; z += spacing)
            {
                var p = new Vector3(x, 0f, z);
                if (!pf.IsRoad(p)) continue;
                if (InJungle(p) != jungle) continue;
                if (!InPlayArea(p, 6f)) continue;
                p.y = GroundHeight(p);
                // The lake's box also covers dry shore; only ground under the water line is lake.
                if (_lake.width > 0f && _lake.Contains(new Vector2(x, z)) && p.y <= WaterLine) continue;
                points.Add(p);
            }
        }
        return points;
    }

    /// <summary>
    /// Up to <paramref name="count"/> of <paramref name="candidates"/>, spread as far apart as possible
    /// (each new pick is the one furthest from all picked so far), starting nearest <paramref name="from"/>.
    /// </summary>
    public static List<Vector3> Spread(List<Vector3> candidates, int count, Vector3 from)
    {
        var picked = new List<Vector3>();
        if (candidates.Count == 0) return picked;

        int first = 0;
        float best = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            float d = FlatSqr(candidates[i], from);
            if (d < best) { best = d; first = i; }
        }
        picked.Add(candidates[first]);

        var nearest = new float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++) nearest[i] = FlatSqr(candidates[i], candidates[first]);

        while (picked.Count < count)
        {
            int far = -1;
            float farD = 0.5f;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (nearest[i] > farD) { farD = nearest[i]; far = i; }
            }
            if (far < 0) break;
            picked.Add(candidates[far]);
            for (int i = 0; i < candidates.Count; i++)
                nearest[i] = Mathf.Min(nearest[i], FlatSqr(candidates[i], candidates[far]));
        }
        return picked;
    }

    /// <summary>Reorders <paramref name="points"/> into a short round trip: nearest first, then nearest-next.</summary>
    public static List<Vector3> OrderAsLoop(List<Vector3> points, Vector3 from)
    {
        var left = new List<Vector3>(points);
        var ordered = new List<Vector3>();
        Vector3 at = from;
        while (left.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < left.Count; i++)
                if (FlatSqr(left[i], at) < FlatSqr(left[best], at)) best = i;
            at = left[best];
            ordered.Add(at);
            left.RemoveAt(best);
        }
        return ordered;
    }

    /// <summary>
    /// A road-preferring route through <paramref name="stops"/> in order, starting at
    /// <paramref name="from"/>, as one polyline. Stops that can't be reached are skipped.
    /// </summary>
    public static List<Vector3> RouteThrough(Vector3 from, List<Vector3> stops)
    {
        var route = new List<Vector3> { from };
        GardenPathfinder pf = Pathfinder;
        if (pf == null) return route;

        Vector3 at = from;
        foreach (Vector3 stop in stops)
        {
            List<Vector3> leg = pf.FindPath(at, stop);
            if (leg == null || leg.Count == 0) continue;
            route.AddRange(leg);
            at = stop;
        }
        return route;
    }

    /// <summary>Is there room for a body of <paramref name="radius"/> at <paramref name="p"/>, above the ground?</summary>
    public static bool IsClear(Vector3 p, float radius, float height)
    {
        Vector3 bottom = p + Vector3.up * (radius + 0.3f);
        Vector3 top = p + Vector3.up * Mathf.Max(radius + 0.3f, height - radius);
        foreach (Collider c in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c is TerrainCollider) continue;
            return false;
        }
        return true;
    }

    private static float FlatSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static void ResolveZones()
    {
        if (_jungleResolved) return;
        _jungleResolved = true;

        GameObject jungle = GameObject.Find("Environment/Jungle");
        if (jungle != null)
        {
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in jungle.GetComponentsInChildren<Renderer>(true))
            {
                // Only the trees: the mood volume and FX can sit anywhere.
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any) _jungle = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
        }

        GameObject[] walls;
        try { walls = GameObject.FindGameObjectsWithTag("Boundary"); }
        catch (UnityException) { walls = new GameObject[0]; }

        if (walls.Length >= 4)
        {
            Vector3 centre = Vector3.zero;
            int n = 0;
            foreach (GameObject w in walls)
            {
                if (!w.TryGetComponent(out Collider c)) continue;
                centre += c.bounds.center;
                n++;
            }
            centre /= Mathf.Max(1, n);

            float minX = float.MinValue, maxX = float.MaxValue, minZ = float.MinValue, maxZ = float.MaxValue;
            foreach (GameObject w in walls)
            {
                if (!w.TryGetComponent(out Collider c)) continue;
                Bounds b = c.bounds;
                if (b.size.x < b.size.z)
                {
                    if (b.center.x < centre.x) minX = Mathf.Max(minX, b.max.x);
                    else maxX = Mathf.Min(maxX, b.min.x);
                }
                else
                {
                    if (b.center.z < centre.z) minZ = Mathf.Max(minZ, b.max.z);
                    else maxZ = Mathf.Min(maxZ, b.min.z);
                }
            }
            if (minX > float.MinValue && maxX < float.MaxValue && minZ > float.MinValue && maxZ < float.MaxValue)
            {
                _playArea = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            }
        }
    }

    private static Rect ResolveLake()
    {
        GameObject env = GameObject.Find("Environment");
        Transform water = env != null ? env.transform.Find("Water") : null;
        if (water == null) return Rect.zero;

        foreach (Renderer r in water.GetComponentsInChildren<Renderer>(true))
        {
            if (r.bounds.size.x < 40f || r.bounds.size.y > 1f) continue;
            Bounds b = r.bounds;
            return new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
        }
        return Rect.zero;
    }
}
