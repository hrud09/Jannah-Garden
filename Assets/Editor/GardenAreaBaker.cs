using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the garden's painted roads into the <see cref="GardenAreaMap"/> placement uses.
///
/// <para>One menu click re-reads the terrain paint, flood-fills it into pockets (the same fill the
/// foliage scatter runs — see <see cref="GardenTerrainRegions"/>), and writes a grid of area slots
/// plus one <see cref="GardenAreaDefinition"/> per pocket. Repaint a path, re-bake, done: there is
/// no outline to re-trace by hand and no way for the areas to disagree with what the player sees.</para>
///
/// <para><b>What a re-bake must never do</b> is move a player's unlocked ground. Pockets are matched
/// back to the existing definitions by centroid, exactly as the scatter matches its themes, so ids,
/// numbers, names and the starting-area flag survive. A pocket that has moved more than
/// <see cref="MatchRadius"/> is treated as new rather than quietly inheriting a neighbour's identity,
/// and the bake report says so.</para>
/// </summary>
public static class GardenAreaBaker
{
    private const string TerrainName = "LandTerrain";
    private const string DefaultAssetPath = "Assets/Resources/Placement/GardenAreaMap.asset";

    /// <summary>Surface of the Lake mesh, from <see cref="GardenRegionScatter"/>'s own water rules.</summary>
    private const float WaterLevel = 3.94f;

    /// <summary>
    /// How far above the lake surface still counts as water for placement. The shoreline shelves
    /// gently, so cutting exactly at the surface would leave a rim of technically-dry cells the
    /// player could build on and watch their item stand ankle-deep.
    /// </summary>
    private const float ShoreMargin = 0.45f;

    /// <summary>
    /// Pockets smaller than this are slivers the paint left between two nearly-touching roads, not
    /// places. They are folded back into "no area" rather than becoming areas nobody can find.
    /// </summary>
    private const float MinAreaSquareMetres = 100f;

    /// <summary>
    /// How far a pocket's centre may move between bakes and still be recognised as the same area.
    /// Matches the tolerance the foliage scatter uses for the same job.
    /// </summary>
    private const float MatchRadius = 12f;

    /// <summary>
    /// Largest patch of no-man's-land the bake will fold into the area surrounding it.
    ///
    /// <para>Sized to catch stray paint, not places. The specks this absorbs are single splotches of
    /// dirt texture a few metres across, dropped inside an area while the garden was being dressed —
    /// they are not roads and were never meant to divide anything. A cap is what keeps the rule from
    /// swallowing something that genuinely is its own place: a pond enclosed by one area would be
    /// absorbed too, and the player would be able to build on the water.</para>
    /// </summary>
    private const float MaxAbsorbedSpeckSquareMetres = 30f;

    /// <summary>
    /// Cap on the baked grid's resolution. The terrain's alphamap can be 1024 a side, which is a
    /// megabyte of bytes resident on a phone for a lookup whose finest real feature — the width of a
    /// path — is metres across. Half a metre per cell is already finer than the paint's own edges.
    /// </summary>
    private const int MaxResolution = 512;

    /// <summary>Distinct, readable colours for the Scene view overlay, cycled by area number.</summary>
    private static readonly Color[] Palette =
    {
        new Color(0.42f, 0.72f, 0.40f), new Color(0.95f, 0.64f, 0.76f), new Color(0.97f, 0.87f, 0.47f),
        new Color(0.18f, 0.55f, 0.42f), new Color(0.37f, 0.69f, 0.84f), new Color(0.66f, 0.63f, 0.59f),
        new Color(0.63f, 0.43f, 0.82f), new Color(0.88f, 0.46f, 0.24f), new Color(0.94f, 0.59f, 0.67f),
        new Color(0.29f, 0.47f, 0.86f), new Color(0.55f, 0.66f, 0.47f), new Color(0.85f, 0.78f, 0.60f),
        new Color(0.40f, 0.80f, 0.72f),
    };

    [MenuItem("Tools/Jannah Garden/Areas/Bake Area Map")]
    public static void Bake()
    {
        Terrain terrain = ResolveTerrain();
        if (terrain == null) return;

        GardenAreaMap map = LoadOrCreateMap();
        if (map == null) return;

        try
        {
            EditorUtility.DisplayProgressBar("Baking garden areas", "Reading terrain paint…", 0.1f);

            Rect lake = ResolveLakeFootprint();
            var regions = new GardenTerrainRegions(terrain, (x, z) => IsWater(terrain, lake, x, z));

            EditorUtility.DisplayProgressBar("Baking garden areas", "Matching pockets to areas…", 0.6f);
            string report = Write(map, regions);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();

            Selection.activeObject = map;
            EditorGUIUtility.PingObject(map);
            Debug.Log("[GardenAreaBaker] " + report, map);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BAKE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fills <paramref name="map"/> in from <paramref name="regions"/> and returns a human-readable
    /// account of what changed — which areas were recognised, which are new, which vanished.
    /// </summary>
    private static string Write(GardenAreaMap map, GardenTerrainRegions regions)
    {
        // Everything that is not a room of the garden is dropped before anything is matched or
        // numbered, so a sliver or the outer field can never consume an existing area's identity or
        // push the numbering along.
        HashSet<int> atEdge = LabelsTouchingEdge(regions);
        var pockets = new List<int>();
        var dropped = new List<string>();

        foreach (KeyValuePair<int, float> entry in regions.AreaByLabel)
        {
            if (atEdge.Contains(entry.Key))
            {
                // Ground that reaches the terrain boundary was never closed off by a road: it is the
                // field the whole garden sits in, not a place inside it. Size alone cannot tell the
                // two apart — the outer field is simply the largest pocket, and one day a real area
                // might be too.
                if (entry.Value >= MinAreaSquareMetres)
                {
                    dropped.Add("open ground, " + entry.Value.ToString("F0") + " m2");
                }
                continue;
            }

            if (entry.Value < MinAreaSquareMetres) continue;

            pockets.Add(entry.Key);
        }

        // North-to-south, then west-to-east: the order the areas read in a top-down view of the
        // garden. Only ever the *initial* numbering for a pocket nobody has numbered yet — an area
        // that already exists keeps whatever number it was given.
        pockets.Sort((a, b) =>
        {
            Vector2 ca = regions.CentroidByLabel[a], cb = regions.CentroidByLabel[b];
            int byRow = cb.y.CompareTo(ca.y);
            return byRow != 0 ? byRow : ca.x.CompareTo(cb.x);
        });

        var previous = new List<GardenAreaDefinition>(map.areas);
        var claimed = new HashSet<GardenAreaDefinition>();
        var definitions = new List<GardenAreaDefinition>(pockets.Count);
        var labelToSlot = new Dictionary<int, byte>(pockets.Count);

        Dictionary<int, Vector2> anchors = DeepestInteriorPoints(regions, pockets);

        var recognised = new List<string>();
        var created = new List<string>();

        for (int i = 0; i < pockets.Count; i++)
        {
            int label = pockets[i];
            Vector2 centroid = regions.CentroidByLabel[label];

            GardenAreaDefinition match = NearestUnclaimed(previous, claimed, centroid);
            GardenAreaDefinition def;

            if (match != null)
            {
                claimed.Add(match);
                def = match;
                recognised.Add("area " + match.number);
            }
            else
            {
                def = new GardenAreaDefinition { id = Guid.NewGuid().ToString("N") };
                created.Add("new pocket at (" + centroid.x.ToString("F0") + ", " + centroid.y.ToString("F0") + ")");
            }

            def.centroid = centroid;
            def.labelAnchor = anchors[label];
            def.squareMetres = regions.AreaByLabel[label];

            definitions.Add(def);

            // Slot is the 1-based position in the definition list, which is what the grid stores.
            // Byte, so a map may hold at most 255 areas — thirteen pockets is not close to that, and
            // a garden that ever got there would want a different structure anyway.
            if (definitions.Count > 255)
            {
                Debug.LogError("[GardenAreaBaker] More than 255 pockets — the remainder are unreachable.");
                break;
            }

            labelToSlot[label] = (byte)definitions.Count;
        }

        // Anything that was not claimed no longer exists on the terrain. Reported rather than silently
        // dropped: it usually means a road was painted across an area, and the player's unlock for it
        // is about to stop referring to any ground.
        var lost = new List<string>();
        foreach (GardenAreaDefinition old in previous)
        {
            if (old != null && !claimed.Contains(old)) lost.Add("area " + old.number);
        }

        AssignMissingNumbers(definitions);

        for (int i = 0; i < definitions.Count; i++)
        {
            definitions[i].editorColor = Palette[Mathf.Abs(definitions[i].number - 1) % Palette.Length];
        }

        map.areas = definitions;
        map.origin = regions.Origin;
        map.size = new Vector2(regions.Size.x, regions.Size.z);
        map.resolution = Mathf.Min(regions.Res, MaxResolution);
        map.cells = BuildCells(regions, labelToSlot, map.resolution);

        int absorbed = AbsorbEnclosedSpecks(map.cells, map.resolution, map.CellSize);

        return BuildReport(definitions, recognised, created, lost, dropped, absorbed, map);
    }

    /// <summary>
    /// The unclaimed definition whose centre is nearest <paramref name="centroid"/> and within
    /// <see cref="MatchRadius"/>, or null when this pocket is somewhere none of them used to be.
    /// </summary>
    private static GardenAreaDefinition NearestUnclaimed(
        List<GardenAreaDefinition> candidates, HashSet<GardenAreaDefinition> claimed, Vector2 centroid)
    {
        GardenAreaDefinition best = null;
        float bestDistance = float.MaxValue;

        foreach (GardenAreaDefinition candidate in candidates)
        {
            if (candidate == null || claimed.Contains(candidate)) continue;

            float distance = Vector2.Distance(candidate.centroid, centroid);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return bestDistance <= MatchRadius ? best : null;
    }

    /// <summary>
    /// Gives every definition without a usable number the lowest free one, leaving the numbers that
    /// were already authored exactly where they are. Duplicates are treated as unnumbered after the
    /// first, so a hand-edit that accidentally repeated a number resolves itself on the next bake.
    /// </summary>
    private static void AssignMissingNumbers(List<GardenAreaDefinition> definitions)
    {
        var taken = new HashSet<int>();

        foreach (GardenAreaDefinition def in definitions)
        {
            if (def.number > 0 && taken.Add(def.number)) continue;
            def.number = 0;
        }

        int next = 1;
        foreach (GardenAreaDefinition def in definitions)
        {
            if (def.number > 0) continue;
            while (taken.Contains(next)) next++;
            def.number = next;
            taken.Add(next);
        }
    }

    /// <summary>
    /// Flattens the labelled fill into the byte grid, downsampling to <paramref name="resolution"/>
    /// by point sampling. Nearest rather than majority on purpose: a majority filter would round off
    /// the corners where three areas meet at a path junction, and the one thing this grid has to get
    /// right is agreeing with the paint the player is looking at.
    /// </summary>
    private static byte[] BuildCells(
        GardenTerrainRegions regions, Dictionary<int, byte> labelToSlot, int resolution)
    {
        var cells = new byte[resolution * resolution];

        for (int y = 0; y < resolution; y++)
        {
            int sy = resolution == regions.Res ? y : Mathf.Min(regions.Res - 1, (y * regions.Res) / resolution);

            for (int x = 0; x < resolution; x++)
            {
                int sx = resolution == regions.Res ? x : Mathf.Min(regions.Res - 1, (x * regions.Res) / resolution);

                int label = regions.Label[sy * regions.Res + sx];
                cells[y * resolution + x] =
                    label > 0 && labelToSlot.TryGetValue(label, out byte slot) ? slot : GardenAreaMap.NoArea;
            }
        }

        return cells;
    }

    /// <summary>
    /// For each pocket, the world XZ of its cell furthest from any road — the point deepest inside it.
    ///
    /// <para>The centroid is the wrong anchor to draw a number at. Several of these pockets curve
    /// around the lake, and the centre of mass of a crescent lies outside the crescent: the label for
    /// the shoreline strip would float over open water. The distance-to-road field the fill already
    /// computed answers "how far inside this area am I" directly, so its maximum is the one point
    /// guaranteed to be within the area and as far from its edges as the shape allows.</para>
    ///
    /// <para>Centroid is still what identity matching uses — it moves predictably when a road is
    /// nudged, where the deepest point can jump across a pocket from one bake to the next.</para>
    /// </summary>
    private static Dictionary<int, Vector2> DeepestInteriorPoints(
        GardenTerrainRegions regions, List<int> pockets)
    {
        var best = new Dictionary<int, float>(pockets.Count);
        var anchors = new Dictionary<int, Vector2>(pockets.Count);

        foreach (int label in pockets)
        {
            best[label] = -1f;
            anchors[label] = regions.CentroidByLabel[label];
        }

        for (int i = 0; i < regions.Label.Length; i++)
        {
            int label = regions.Label[i];
            if (label <= 0 || !best.TryGetValue(label, out float deepest)) continue;

            float distance = regions.RoadDistance[i];
            if (distance <= deepest) continue;

            best[label] = distance;
            anchors[label] = regions.CellCentre(i);
        }

        return anchors;
    }

    /// <summary>
    /// Labels with at least one cell on the terrain's outer ring. A pocket the roads never closed
    /// reaches the boundary; every room of the garden is surrounded by paint on all sides.
    /// </summary>
    private static HashSet<int> LabelsTouchingEdge(GardenTerrainRegions regions)
    {
        var touching = new HashSet<int>();
        int res = regions.Res;

        for (int i = 0; i < res; i++)
        {
            AddIfPocket(touching, regions.Label[i]);                       // south row
            AddIfPocket(touching, regions.Label[(res - 1) * res + i]);     // north row
            AddIfPocket(touching, regions.Label[i * res]);                 // west column
            AddIfPocket(touching, regions.Label[i * res + (res - 1)]);     // east column
        }

        return touching;
    }

    private static void AddIfPocket(HashSet<int> into, int label)
    {
        if (label > 0) into.Add(label);
    }

    /// <summary>
    /// Folds every small patch of no-area ground that is completely surrounded by one area into that
    /// area. Returns how many were absorbed.
    ///
    /// <para>The garden's dressing left splotches of dirt texture inside the areas, and the fill reads
    /// dirt as road. Left alone they are holes: unbuildable pinpricks in the middle of ground the
    /// player owns, speckled over with the locked-ground hatching. Folding them in fixes the rule and
    /// the picture at once, and does it in the bake so nothing downstream has to know they existed.</para>
    ///
    /// <para>Only patches touching exactly one area qualify. One that borders two is the seam between
    /// them — a road, however short — and absorbing it would quietly join two areas into one.</para>
    /// </summary>
    private static int AbsorbEnclosedSpecks(byte[] cells, int resolution, float cellSize)
    {
        int maxCells = Mathf.Max(1, Mathf.FloorToInt(MaxAbsorbedSpeckSquareMetres / (cellSize * cellSize)));

        var visited = new bool[cells.Length];
        var patch = new List<int>();
        var frontier = new Queue<int>();
        int absorbed = 0;

        for (int start = 0; start < cells.Length; start++)
        {
            if (visited[start] || cells[start] != GardenAreaMap.NoArea) continue;

            patch.Clear();
            frontier.Clear();
            visited[start] = true;
            frontier.Enqueue(start);

            byte neighbour = GardenAreaMap.NoArea;
            bool manyNeighbours = false;
            bool reachesEdge = false;

            while (frontier.Count > 0)
            {
                int c = frontier.Dequeue();
                patch.Add(c);

                int cy = c / resolution, cx = c % resolution;

                if (cx == 0 || cy == 0 || cx == resolution - 1 || cy == resolution - 1) reachesEdge = true;

                Step(cells, resolution, visited, frontier, cx - 1, cy, ref neighbour, ref manyNeighbours);
                Step(cells, resolution, visited, frontier, cx + 1, cy, ref neighbour, ref manyNeighbours);
                Step(cells, resolution, visited, frontier, cx, cy - 1, ref neighbour, ref manyNeighbours);
                Step(cells, resolution, visited, frontier, cx, cy + 1, ref neighbour, ref manyNeighbours);
            }

            if (reachesEdge || manyNeighbours || neighbour == GardenAreaMap.NoArea) continue;
            if (patch.Count > maxCells) continue;

            for (int i = 0; i < patch.Count; i++) cells[patch[i]] = neighbour;
            absorbed++;
        }

        return absorbed;
    }

    /// <summary>
    /// Visits one neighbour of a patch cell: queues it when it is more no-area ground, and otherwise
    /// records which area it belongs to — tracking whether the patch has met more than one.
    /// </summary>
    private static void Step(
        byte[] cells, int resolution, bool[] visited, Queue<int> frontier,
        int x, int y, ref byte neighbour, ref bool manyNeighbours)
    {
        if (x < 0 || y < 0 || x >= resolution || y >= resolution) return;

        int index = y * resolution + x;
        byte slot = cells[index];

        if (slot != GardenAreaMap.NoArea)
        {
            if (neighbour == GardenAreaMap.NoArea) neighbour = slot;
            else if (neighbour != slot) manyNeighbours = true;
            return;
        }

        if (visited[index]) return;
        visited[index] = true;
        frontier.Enqueue(index);
    }

    private static string BuildReport(
        List<GardenAreaDefinition> definitions,
        List<string> recognised, List<string> created, List<string> lost, List<string> dropped,
        int absorbed, GardenAreaMap map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Baked " + definitions.Count + " areas at " + map.resolution + "x" + map.resolution +
                      " (" + map.CellSize.ToString("F2") + " m per cell).");
        sb.AppendLine("  recognised: " + (recognised.Count > 0 ? string.Join(", ", recognised) : "none"));
        sb.AppendLine("  new:        " + (created.Count > 0 ? string.Join(", ", created) : "none"));
        sb.AppendLine("  gone:       " + (lost.Count > 0 ? string.Join(", ", lost) + "  (their unlocks now point at nothing)" : "none"));
        sb.AppendLine("  not a room: " + (dropped.Count > 0 ? string.Join(", ", dropped) : "none"));
        sb.AppendLine("  specks absorbed into their surrounding area: " + absorbed);

        List<GardenAreaDefinition> ordered = map.InNumberOrder();
        foreach (GardenAreaDefinition def in ordered)
        {
            sb.AppendLine("  " + def.number.ToString().PadLeft(3) + "  " +
                          def.squareMetres.ToString("F0").PadLeft(6) + " m2  centre (" +
                          def.centroid.x.ToString("F0") + ", " + def.centroid.y.ToString("F0") + ")" +
                          (def.unlockedFromStart ? "  [starting area]" : ""));
        }

        if (map.areas.TrueForAll(a => !a.unlockedFromStart))
        {
            sb.AppendLine("  No area is flagged 'unlocked from start' — tick one in the inspector.");
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SCENE
    // ═══════════════════════════════════════════════════════════════════════════

    private static Terrain ResolveTerrain()
    {
        var go = GameObject.Find(TerrainName);
        Terrain terrain = go != null ? go.GetComponent<Terrain>() : null;

        if (terrain == null) terrain = Terrain.activeTerrain;

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("[GardenAreaBaker] No Terrain named '" + TerrainName + "' in the open scene.");
            return null;
        }

        return terrain;
    }

    /// <summary>
    /// Footprint of the Lake plane in XZ. Height alone cannot decide what is underwater — plenty of
    /// dry ground elsewhere in the garden sits below the lake's surface height.
    /// </summary>
    private static Rect ResolveLakeFootprint()
    {
        var env = GameObject.Find("Environment");
        Transform water = env != null ? env.transform.Find("Water") : null;
        if (water == null) return Rect.zero;

        foreach (Renderer r in water.GetComponentsInChildren<Renderer>(true))
        {
            // The lake surface is the one big flat plane in there; the gazebo floors are not.
            if (r.bounds.size.x < 40f || r.bounds.size.y > 1f) continue;
            return new Rect(r.bounds.min.x, r.bounds.min.z, r.bounds.size.x, r.bounds.size.z);
        }

        return Rect.zero;
    }

    private static bool IsWater(Terrain terrain, Rect lake, float x, float z)
    {
        if (lake.width <= 0f || !lake.Contains(new Vector2(x, z))) return false;

        float height = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        return height <= WaterLevel + ShoreMargin;
    }

    private static GardenAreaMap LoadOrCreateMap()
    {
        var existing = AssetDatabase.FindAssets("t:GardenAreaMap");

        if (existing.Length > 1)
        {
            Debug.LogWarning("[GardenAreaBaker] " + existing.Length + " area maps exist; baking the first. " +
                             "Delete the spares — a second map is a second answer to the same question.");
        }

        if (existing.Length > 0)
        {
            return AssetDatabase.LoadAssetAtPath<GardenAreaMap>(AssetDatabase.GUIDToAssetPath(existing[0]));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DefaultAssetPath));
        var map = ScriptableObject.CreateInstance<GardenAreaMap>();
        AssetDatabase.CreateAsset(map, DefaultAssetPath);
        Debug.Log("[GardenAreaBaker] Created " + DefaultAssetPath);
        return map;
    }
}
