using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The garden's painted roads, read back as geometry.
///
/// <para>The paths are not objects — they are paint on the terrain's dirt layer. Flood-filling the
/// terrain away from that paint (and away from the sand flats, which are their own kind of edge)
/// turns the path network into a set of closed pockets: the numbered areas of the garden.</para>
///
/// <para>This is the single source of truth for where one area ends and the next begins. Both the
/// foliage scatter (<see cref="GardenRegionScatter"/>, which themes each pocket) and the placement
/// area bake (<see cref="GardenAreaBaker"/>, which decides where the player may build) run off the
/// same fill, so a repainted road moves both at once instead of letting them drift apart.</para>
///
/// <para>Editor-only: it reads the terrain's alphamaps, which are authoring data.</para>
/// </summary>
internal sealed class GardenTerrainRegions
{
    /// <summary>Terrain layer indices, from the terrain's own layer list.</summary>
    public const int LayerDirt = 1;   // the paths
    public const int LayerSand = 2;   // the southern flats

    /// <summary>Above this dirt weight a cell counts as road, and belongs to no pocket.</summary>
    public const float RoadThreshold = 0.35f;
    public const float SandThreshold = 0.5f;

    public readonly int Res;
    public readonly float CellSize;
    public readonly Vector3 Origin;
    public readonly Vector3 Size;

    /// <summary>-1 for road/sand/excluded, otherwise a 1-based pocket label.</summary>
    public readonly int[] Label;
    /// <summary>Metres from each cell to the nearest road or sand cell.</summary>
    public readonly float[] RoadDistance;
    /// <summary>Metres from each cell to the terrain edge.</summary>
    public readonly float[] EdgeDistance;

    public readonly Dictionary<int, float> AreaByLabel = new Dictionary<int, float>();
    public readonly Dictionary<int, Vector2> CentroidByLabel = new Dictionary<int, Vector2>();

    /// <summary>
    /// Runs the fill over <paramref name="terrain"/>.
    ///
    /// <paramref name="isExcluded"/> marks extra ground as barrier by world XZ — the lake, for the
    /// placement bake. Pass null to fill on the painted roads alone, which is what the foliage
    /// scatter does: adding a barrier there would renumber its pockets and break the centroid
    /// matching its themes rely on.
    /// </summary>
    public GardenTerrainRegions(Terrain terrain, Func<float, float, bool> isExcluded = null)
    {
        TerrainData td = terrain.terrainData;
        Res = td.alphamapResolution;
        Size = td.size;
        Origin = terrain.transform.position;
        CellSize = Size.x / Res;

        float[,,] alpha = td.GetAlphamaps(0, 0, Res, Res);
        int n = Res * Res;
        Label = new int[n];
        var barrier = new bool[n];

        for (int y = 0; y < Res; y++)
        {
            for (int x = 0; x < Res; x++)
            {
                bool b = alpha[y, x, LayerDirt] > RoadThreshold || alpha[y, x, LayerSand] > SandThreshold;

                if (!b && isExcluded != null)
                {
                    // Cell centres, not corners: a cell is in or out as a whole, and its centre is the
                    // point every later world-space lookup of that cell resolves to.
                    b = isExcluded(Origin.x + (x + 0.5f) * CellSize, Origin.z + (y + 0.5f) * CellSize);
                }

                barrier[y * Res + x] = b;
                Label[y * Res + x] = b ? -1 : 0;
            }
        }

        // Multi-source BFS out from every barrier cell. An 8-connected walk is close enough to
        // euclidean at the few-metre scale the clearance rules actually test against.
        RoadDistance = new float[n];
        var q = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            RoadDistance[i] = barrier[i] ? 0f : float.MaxValue;
            if (barrier[i]) q.Enqueue(i);
        }

        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int cy = c / Res, cx = c % Res;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= Res || ny >= Res) continue;
                    int ni = ny * Res + nx;
                    float step = (dx == 0 || dy == 0) ? CellSize : CellSize * 1.41421f;
                    if (RoadDistance[c] + step < RoadDistance[ni])
                    {
                        RoadDistance[ni] = RoadDistance[c] + step;
                        q.Enqueue(ni);
                    }
                }
            }
        }

        EdgeDistance = new float[n];
        for (int y = 0; y < Res; y++)
        {
            for (int x = 0; x < Res; x++)
            {
                EdgeDistance[y * Res + x] =
                    Mathf.Min(Mathf.Min(x, Res - 1 - x), Mathf.Min(y, Res - 1 - y)) * CellSize;
            }
        }

        // Label the pockets.
        int next = 0;
        var fill = new Queue<int>();
        float cellArea = CellSize * CellSize;

        for (int s = 0; s < n; s++)
        {
            if (Label[s] != 0) continue;
            next++;
            Label[s] = next;
            fill.Enqueue(s);
            long count = 0, sx = 0, sy = 0;

            while (fill.Count > 0)
            {
                int c = fill.Dequeue();
                int cy = c / Res, cx = c % Res;
                count++; sx += cx; sy += cy;
                if (cx > 0 && Label[c - 1] == 0) { Label[c - 1] = next; fill.Enqueue(c - 1); }
                if (cx < Res - 1 && Label[c + 1] == 0) { Label[c + 1] = next; fill.Enqueue(c + 1); }
                if (cy > 0 && Label[c - Res] == 0) { Label[c - Res] = next; fill.Enqueue(c - Res); }
                if (cy < Res - 1 && Label[c + Res] == 0) { Label[c + Res] = next; fill.Enqueue(c + Res); }
            }

            AreaByLabel[next] = count * cellArea;
            CentroidByLabel[next] = new Vector2(
                Origin.x + (float)sx / count / Res * Size.x,
                Origin.z + (float)sy / count / Res * Size.z);
        }
    }

    /// <summary>World XZ of the centre of cell <paramref name="index"/>.</summary>
    public Vector2 CellCentre(int index)
    {
        int cy = index / Res, cx = index % Res;
        return new Vector2(Origin.x + (cx + 0.5f) * CellSize, Origin.z + (cy + 0.5f) * CellSize);
    }
}
