using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Erases the painted road that divides a chosen handful of areas, so the next bake reads them as
/// one pocket.
///
/// <para>The areas are not authored shapes — they are whatever the paint encloses (see
/// <see cref="GardenTerrainRegions"/>). Joining two of them therefore means repainting, not editing
/// a list: unpaint the strand between them and <see cref="GardenAreaBaker"/> produces one area out of
/// the two on its own. Doing it by hand with the terrain brush is possible and was the alternative,
/// but a brush stroke cannot tell which side of a junction it is on, and overshooting by a metre
/// quietly dissolves a third area into the merge — which the bake would then report as an area the
/// player's save data no longer points at.</para>
///
/// <para><b>How the strand is found.</b> Not by tracing it. For every painted cell this measures how
/// far it is from each of the chosen pockets and from every pocket that was <em>not</em> chosen, and
/// unpaints only those that have two of the chosen ones close by and nothing else within reach. That
/// rule describes exactly "the ground that separates these areas from each other", so the road
/// bordering a neighbour the merge must not touch is preserved by construction rather than by
/// whoever is holding the brush being careful.</para>
///
/// <para><b>Why there is a second pass.</b> That rule alone leaves a stub at each end of the strand,
/// where the erased road ran close enough to an area outside the merge to be protected by it. A stub
/// is a path to nowhere, and reads as unfinished rather than as removed. Trimming it cannot be done
/// by distance — the stub's base genuinely is part of the boundary road — so the second pass asks a
/// different question of each surviving cell: how far are the <em>two nearest different</em> pockets?
/// Road that separates two areas has them on opposite kerbs, a road-width apart in total. A spur has
/// one area flush against it on every side and the next one far away, back along the spur. Cutting
/// on that sum trims the dead ends without narrowing any road that is still doing its job. Both
/// passes feather their edges over <see cref="FeatherMetres"/>.</para>
///
/// <para>Writes to the terrain's alphamaps, which is an asset edit and is undoable as one step. Re-run
/// <c>Tools/Jannah Garden/Areas/Bake Area Map</c> afterwards — this deliberately does not, because
/// the merged pocket inherits its identity from whichever of the merged areas it matches by centroid,
/// and which one that is wants looking at before it is written down.</para>
/// </summary>
public static class GardenRoadMerger
{
    /// <summary>
    /// The areas to join, by the number the player sees. They must be neighbours: a pair with another
    /// area between them has no shared strand to erase, and nothing will happen.
    /// </summary>
    private static readonly int[] TargetAreaNumbers = { 1, 2, 3 };

    /// <summary>
    /// How close a pocket counts as "on the other side of this strand".
    ///
    /// <para>Has to clear half the widest strand being erased — a cell in the middle of a four metre
    /// road is two metres from either side — and has to stay under the distance from that strand to
    /// the nearest area the merge must leave alone, which around the gateway junction is about eight
    /// metres. Six sits in the middle of that window.</para>
    /// </summary>
    private const float ReachMetres = 6f;

    /// <summary>How far back from the new opening the surviving road fades out.</summary>
    private const float FeatherMetres = 2f;

    /// <summary>How far out the dead-end test looks when asking what surrounds a cell.</summary>
    private const float RingRadiusMetres = 3f;

    /// <summary>Directions sampled around that ring.</summary>
    private const int RingSamples = 64;

    /// <summary>
    /// How much of the ring one single area has to occupy before the cell counts as a dead end rather
    /// than as road.
    ///
    /// <para>Road that separates two areas has one of them along one side of the ring and the other
    /// along the opposite side — neither gets much past a third of it, whichever kerb the cell sits
    /// on. A stub has the same area wrapped around its tip and sides and nothing but more stub
    /// behind, which lands far above this. The gap between the two cases is wide, so the exact value
    /// is not delicate.</para>
    /// </summary>
    private const float RingDominance = 0.62f;

    /// <summary>
    /// Cap on erosion passes. Each one shortens a stub by about a ring radius, and the stubs this
    /// leaves behind are a few metres long — the cap is a guard against a runaway, not a budget.
    /// </summary>
    private const int MaxTrimPasses = 10;

    /// <summary>
    /// How far from the erased strand the second pass is allowed to look.
    ///
    /// <para>The dead-end test is sound anywhere, but there is no reason to re-cut roads at the far
    /// side of the garden on the strength of it, and a bug in it should not be able to. Confining it
    /// to the neighbourhood of the merge keeps the blast radius the size of the edit.</para>
    /// </summary>
    private const float TrimNeighbourhoodMetres = 15f;

    /// <summary>Terrain layer the erased weight is handed to. Index 0 is the grass the garden sits on.</summary>
    private const int LayerGrass = 0;

    [MenuItem("Tools/Jannah Garden/Areas/Merge Areas By Erasing Their Road")]
    public static void Merge()
    {
        Terrain terrain = GardenAreaBaker.ResolveTerrain();
        if (terrain == null) return;

        GardenAreaMap map = GardenAreaBaker.LoadOrCreateMap();
        if (map == null || !map.IsBaked)
        {
            Debug.LogError("[GardenRoadMerger] The area map has not been baked, so there is nothing to " +
                           "name the areas being merged. Run Bake Area Map first.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Merging garden areas", "Reading terrain paint…", 0.1f);

            Rect lake = GardenAreaBaker.ResolveLakeFootprint();
            System.Func<float, float, bool> water = (x, z) => GardenAreaBaker.IsWater(terrain, lake, x, z);

            var regions = new GardenTerrainRegions(terrain, water);

            List<int> targets = ResolveTargetLabels(map, regions);
            if (targets == null) return;

            EditorUtility.DisplayProgressBar("Merging garden areas", "Finding the strand between them…", 0.4f);

            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Merge garden areas");

            float[,,] alpha = data.GetAlphamaps(0, 0, regions.Res, regions.Res);

            bool[] seam = EraseSeam(regions, targets, alpha, out string seamReport);
            if (seam == null)
            {
                Debug.LogWarning("[GardenRoadMerger] " + seamReport, data);
                return;
            }

            // Written before the second pass rather than after both, because that pass has to see the
            // pockets as the merge left them — and the fill reads them back off the terrain.
            data.SetAlphamaps(0, 0, alpha);

            EditorUtility.DisplayProgressBar("Merging garden areas", "Trimming the dead ends…", 0.7f);

            var merged = new GardenTerrainRegions(terrain, water);
            alpha = data.GetAlphamaps(0, 0, merged.Res, merged.Res);

            string trimReport = TrimDeadEnds(merged, seam, alpha);

            EditorUtility.DisplayProgressBar("Merging garden areas", "Writing the paint back…", 0.9f);

            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Debug.Log("[GardenRoadMerger] " + seamReport + trimReport, data);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  WHICH POCKETS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The fill labels of the areas named in <see cref="TargetAreaNumbers"/>, or null when any of them
    /// cannot be placed on the terrain as it is painted now.
    ///
    /// <para>Found by sampling the fill at each area's <see cref="GardenAreaDefinition.labelAnchor"/>
    /// rather than by matching centroids. The anchor is the point furthest inside the pocket, so it is
    /// guaranteed to land in it; a centroid is not — several of the garden's areas curve, and the
    /// centre of mass of a crescent lies outside the crescent.</para>
    /// </summary>
    private static List<int> ResolveTargetLabels(GardenAreaMap map, GardenTerrainRegions regions)
    {
        var labels = new List<int>(TargetAreaNumbers.Length);

        foreach (int number in TargetAreaNumbers)
        {
            GardenAreaDefinition def = map.ByNumber(number);
            if (def == null)
            {
                Debug.LogError("[GardenRoadMerger] The map has no area " + number + ".");
                return null;
            }

            int x = Mathf.FloorToInt((def.labelAnchor.x - regions.Origin.x) / regions.CellSize);
            int y = Mathf.FloorToInt((def.labelAnchor.y - regions.Origin.z) / regions.CellSize);

            if (x < 0 || y < 0 || x >= regions.Res || y >= regions.Res)
            {
                Debug.LogError("[GardenRoadMerger] Area " + number + "'s anchor is off the terrain. Re-bake first.");
                return null;
            }

            int label = regions.Label[y * regions.Res + x];
            if (label <= 0)
            {
                Debug.LogError("[GardenRoadMerger] Area " + number + "'s anchor is on road or water, so the " +
                               "map no longer matches the paint. Re-bake first.");
                return null;
            }

            if (labels.Contains(label))
            {
                Debug.LogWarning("[GardenRoadMerger] Areas " + number + " and an earlier one are already the " +
                                 "same pocket. Nothing to erase between them.");
                continue;
            }

            labels.Add(label);
        }

        if (labels.Count < 2)
        {
            Debug.LogError("[GardenRoadMerger] Fewer than two distinct pockets named; nothing to merge.");
            return null;
        }

        return labels;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ERASE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unpaints the road between the target pockets in <paramref name="alpha"/>, returning which cells
    /// it took out — or null, with <paramref name="report"/> saying why, when it found none.
    /// </summary>
    private static bool[] EraseSeam(
        GardenTerrainRegions regions, List<int> targets, float[,,] alpha, out string report)
    {
        int n = regions.Res * regions.Res;

        // One distance field per target, plus one from every pocket that is not a target. Measuring
        // from the pockets outwards rather than walking the road means the test below is a pure
        // lookup, and costs four sweeps of the grid regardless of how tangled the paint is.
        var toTarget = new float[targets.Count][];
        for (int t = 0; t < targets.Count; t++)
        {
            toTarget[t] = DistanceFrom(regions, label => label == targets[t]);
        }

        float[] toOther = DistanceFrom(regions, label => label > 0 && !targets.Contains(label));

        var erased = new bool[n];
        int count = 0;

        for (int i = 0; i < n; i++)
        {
            // Only painted road comes out. Sand and the lake are barriers too, and dissolving either
            // of them would open the garden onto ground it was never meant to reach.
            if (regions.Label[i] != -1) continue;
            if (alpha[i / regions.Res, i % regions.Res, GardenTerrainRegions.LayerDirt] <= GardenTerrainRegions.RoadThreshold) continue;

            // Anything else nearby means this cell is part of some other area's edge, not of the seam
            // between the chosen ones.
            if (toOther[i] <= ReachMetres) continue;

            int near = 0;
            for (int t = 0; t < targets.Count && near < 2; t++)
            {
                if (toTarget[t][i] <= ReachMetres) near++;
            }

            if (near < 2) continue;

            erased[i] = true;
            count++;
        }

        if (count == 0)
        {
            report = "Found no road that separates only areas " + string.Join(", ", TargetAreaNumbers) +
                     ". Nothing was changed — check they are actually neighbours.";
            return null;
        }

        for (int i = 0; i < n; i++)
        {
            if (erased[i]) Unpaint(alpha, regions.Res, i, 0f);
        }

        int feathered = Feather(regions, erased, alpha);

        var sb = new StringBuilder();
        sb.AppendLine("Erased the road between areas " + string.Join(", ", TargetAreaNumbers) + ".");
        sb.AppendLine("  seam unpainted:  " + Describe(regions, count) + ", faded over " + feathered + " more");
        report = sb.ToString();
        return erased;
    }

    /// <summary>
    /// Takes out the stubs the seam left behind — road near the merge with no second area on the far
    /// side of it — and returns an account of what went.
    /// </summary>
    private static string TrimDeadEnds(GardenTerrainRegions merged, bool[] seam, float[,,] alpha)
    {
        float[] toSeam = DistanceFrom(merged, seam);

        // Pockets are tracked here rather than re-filled from the terrain between passes: a cell this
        // takes out is, by the very test that took it out, surrounded by one area, so handing it to
        // that area is the answer the fill would have given — and it lets a stub erode from the tip
        // inwards without writing the paint back and reading it again ten times.
        int[] label = (int[])merged.Label.Clone();
        var erased = new bool[merged.Label.Length];
        int count = 0;
        var passes = new List<int>();

        var ring = BuildRing(merged.CellSize);
        var ballot = new Dictionary<int, int>();
        var marked = new List<int>();
        var markedTo = new List<int>();

        for (int pass = 0; pass < MaxTrimPasses; pass++)
        {
            marked.Clear();
            markedTo.Clear();

            for (int i = 0; i < label.Length; i++)
            {
                if (label[i] != -1) continue;
                if (toSeam[i] > TrimNeighbourhoodMetres) continue;
                if (alpha[i / merged.Res, i % merged.Res, GardenTerrainRegions.LayerDirt] <= GardenTerrainRegions.RoadThreshold) continue;

                int dominant = Dominant(label, merged.Res, i, ring, ballot, out float share);
                if (dominant <= 0 || share < RingDominance) continue;

                marked.Add(i);
                markedTo.Add(dominant);
            }

            if (marked.Count == 0) break;

            for (int m = 0; m < marked.Count; m++)
            {
                label[marked[m]] = markedTo[m];
                erased[marked[m]] = true;
            }

            count += marked.Count;
            passes.Add(marked.Count);
        }

        if (count == 0) return "  dead ends trimmed: none\n";

        for (int i = 0; i < erased.Length; i++)
        {
            if (erased[i]) Unpaint(alpha, merged.Res, i, 0f);
        }

        int feathered = Feather(merged, erased, alpha);

        var sb = new StringBuilder();
        sb.AppendLine("  dead ends gone:  " + Describe(merged, count) + ", faded over " + feathered + " more");
        sb.AppendLine("  eroded per pass: " + string.Join(", ", passes));
        sb.AppendLine("  Re-run Tools/Jannah Garden/Areas/Bake Area Map to fold the areas into one.");
        return sb.ToString();
    }

    /// <summary>Cell offsets of <see cref="RingSamples"/> points evenly around a <see cref="RingRadiusMetres"/> circle.</summary>
    private static List<Vector2Int> BuildRing(float cellSize)
    {
        var ring = new List<Vector2Int>(RingSamples);
        int radius = Mathf.Max(1, Mathf.RoundToInt(RingRadiusMetres / cellSize));

        for (int s = 0; s < RingSamples; s++)
        {
            float angle = s * 2f * Mathf.PI / RingSamples;
            ring.Add(new Vector2Int(
                Mathf.RoundToInt(Mathf.Cos(angle) * radius),
                Mathf.RoundToInt(Mathf.Sin(angle) * radius)));
        }

        return ring;
    }

    /// <summary>
    /// The area occupying most of the ring around <paramref name="index"/>, and what share of the
    /// whole ring that is. Samples that land on road, water or off the terrain count against the
    /// share rather than being discarded — a cell with road all around it is in the middle of a
    /// junction, which is exactly what must not be eroded.
    /// </summary>
    private static int Dominant(
        int[] label, int res, int index, List<Vector2Int> ring, Dictionary<int, int> ballot, out float share)
    {
        ballot.Clear();
        int cy = index / res, cx = index % res;

        foreach (Vector2Int offset in ring)
        {
            int x = cx + offset.x, y = cy + offset.y;
            if (x < 0 || y < 0 || x >= res || y >= res) continue;

            int at = label[y * res + x];
            if (at <= 0) continue;

            ballot.TryGetValue(at, out int seen);
            ballot[at] = seen + 1;
        }

        int winner = 0, best = 0;
        foreach (KeyValuePair<int, int> entry in ballot)
        {
            if (entry.Value <= best) continue;
            best = entry.Value;
            winner = entry.Key;
        }

        share = (float)best / ring.Count;
        return winner;
    }

    private static string Describe(GardenTerrainRegions regions, int cells)
    {
        float squareMetres = cells * regions.CellSize * regions.CellSize;
        return cells + " cells (" + squareMetres.ToString("F0") + " m2)";
    }

    /// <summary>
    /// Ramps the surviving road down to nothing over <see cref="FeatherMetres"/> around the hole just
    /// made, and returns how many cells it touched.
    ///
    /// <para>Without this the roads that remain stop dead at the edge of the erased strand, which
    /// reads as a texture seam rather than as a path that ends. The ramp is cheap to compute because
    /// the distance it needs is the one the fill already knows how to measure.</para>
    /// </summary>
    private static int Feather(GardenTerrainRegions regions, bool[] erased, float[,,] alpha)
    {
        float[] toErased = DistanceFrom(regions, erased);
        int touched = 0;

        for (int i = 0; i < erased.Length; i++)
        {
            if (erased[i]) continue;

            float d = toErased[i];
            if (d <= 0f || d >= FeatherMetres) continue;
            if (alpha[i / regions.Res, i % regions.Res, GardenTerrainRegions.LayerDirt] <= 0f) continue;

            Unpaint(alpha, regions.Res, i, Mathf.SmoothStep(0f, 1f, d / FeatherMetres));
            touched++;
        }

        return touched;
    }

    /// <summary>
    /// Scales one cell's dirt weight to <paramref name="keep"/> of what it was and hands the remainder
    /// to the grass, so the weights still sum to one and the ground reads as lawn rather than as a
    /// hole in the splat.
    /// </summary>
    private static void Unpaint(float[,,] alpha, int res, int index, float keep)
    {
        int y = index / res, x = index % res;

        float dirt = alpha[y, x, GardenTerrainRegions.LayerDirt];
        float kept = dirt * keep;

        alpha[y, x, GardenTerrainRegions.LayerDirt] = kept;
        alpha[y, x, LayerGrass] += dirt - kept;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DISTANCE
    // ═══════════════════════════════════════════════════════════════════════════

    private static float[] DistanceFrom(GardenTerrainRegions regions, System.Func<int, bool> isSource)
    {
        var sources = new bool[regions.Label.Length];
        for (int i = 0; i < sources.Length; i++) sources[i] = isSource(regions.Label[i]);
        return DistanceFrom(regions, sources);
    }

    /// <summary>
    /// Metres from every cell to the nearest source, walking through road and pocket alike — the same
    /// eight-connected sweep <see cref="GardenTerrainRegions"/> uses for its own clearance field, so
    /// the two agree about what "two metres away" means.
    /// </summary>
    private static float[] DistanceFrom(GardenTerrainRegions regions, bool[] sources)
    {
        int res = regions.Res;
        var distance = new float[sources.Length];
        var queue = new Queue<int>();

        for (int i = 0; i < sources.Length; i++)
        {
            distance[i] = sources[i] ? 0f : float.MaxValue;
            if (sources[i]) queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cy = c / res, cx = c % res;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= res || ny >= res) continue;

                    int ni = ny * res + nx;
                    float step = (dx == 0 || dy == 0) ? regions.CellSize : regions.CellSize * 1.41421f;

                    if (distance[c] + step >= distance[ni]) continue;

                    distance[ni] = distance[c] + step;
                    queue.Enqueue(ni);
                }
            }
        }

        return distance;
    }
}
