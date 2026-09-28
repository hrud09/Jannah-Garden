using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a ground-hugging mesh over a chosen set of <see cref="GardenAreaMap"/> cells.
///
/// <para>Shared by the in-game locked-ground overlay (<see cref="PlacementGridView"/>) and the Scene
/// view preview (<c>GardenAreaMapEditor</c>) so the two cannot drift: what the editor draws to check
/// the garden's areas is produced by the same code, from the same grid, as what the player sees.</para>
///
/// <para><b>Why runs and not one quad per cell:</b> the map is roughly half a metre per cell, so even
/// a 12 m placement radius covers some two thousand cells and the whole garden covers a quarter of a
/// million. Merging each row's consecutive selected cells into one strip cuts that by an order of
/// magnitude.</para>
///
/// <para>A strip, specifically, and not a single stretched quad. A quad has only its four corners to
/// sample ground height at, so across undulating terrain its middle sinks below the surface and the
/// ground tears through the overlay in ragged holes. A strip keeps a pair of vertices at every cell
/// boundary along the run, so it follows the ground exactly — and it is <em>cheaper</em> than separate
/// quads too, sharing each boundary's vertices instead of duplicating them.</para>
/// </summary>
public static class GardenAreaMesh
{
    /// <summary>Ground height at a world XZ. Terrain sampling, supplied by the caller.</summary>
    public delegate float HeightSampler(float x, float z);

    // Reused between calls: this runs whenever the player walks far enough, and a fresh set of
    // buffers each time would hand the collector hundreds of kilobytes for nothing.
    private static readonly List<Vector3> Vertices = new List<Vector3>();
    private static readonly List<int> Triangles = new List<int>();

    /// <summary>
    /// Ground height at each corner of the patch's cell lattice, and the rebuild it was sampled on.
    ///
    /// <para>Every run shares its end corners with the run beside it, and every row shares its whole
    /// upper edge with the row above — so sampling per vertex asked the terrain for the same point
    /// about twice over. Sampling per corner instead halves the calls, and terrain height sampling was
    /// measured at roughly two thirds of the entire rebuild.</para>
    ///
    /// <para>The stamp is what makes the cache free to reset: bumping one integer invalidates every
    /// entry, where clearing the array would cost a pass over it on every rebuild.</para>
    /// </summary>
    private static float[] _cornerHeight = new float[0];
    private static int[] _cornerStamp = new int[0];
    private static int _stamp;

    private static int _cornerPitch;
    private static int _cornerMinX;
    private static int _cornerMinY;

    /// <summary>
    /// Builds the lookup the patch is selected with: one entry per slot, saying whether that slot's
    /// ground is covered by the overlay.
    ///
    /// <para>A table rather than a predicate because the patch tests every cell in the window — fifteen
    /// thousand of them — and a delegate call per cell, each one walking to a definition and hashing
    /// its id, is real time spent re-deciding fifteen facts. <paramref name="isLocked"/> runs once per
    /// area instead, and the inner loop becomes a single array index.</para>
    ///
    /// <para>Grows <paramref name="buffer"/> only when the map gains areas, so steady state allocates
    /// nothing.</para>
    /// </summary>
    public static bool[] FillLockedSlots(
        GardenAreaMap map, Func<GardenAreaDefinition, bool> isLocked, ref bool[] buffer)
    {
        int needed = (map != null ? map.areas.Count : 0) + 1;
        if (buffer == null || buffer.Length < needed) buffer = new bool[needed];

        // Roads, sand and water are never covered; see the note on why the picture is narrower than
        // the rule in LockedSlots below.
        buffer[GardenAreaMap.NoArea] = false;

        for (int i = 0; i < needed - 1; i++)
        {
            GardenAreaDefinition area = map.areas[i];
            buffer[i + 1] = area != null && isLocked != null && isLocked(area);
        }

        return buffer;
    }

    /// <summary>
    /// Fills <paramref name="mesh"/> with ground-following strips over every cell whose slot is marked
    /// in <paramref name="selectedBySlot"/>.
    ///
    /// <paramref name="radius"/> limits the patch to a circle around <paramref name="centre"/>; pass
    /// a negative radius to cover the whole map, which is what the Scene view preview wants and the
    /// in-game overlay never does.
    ///
    /// Returns the number of quads written, so the caller can skip enabling an empty renderer.
    /// </summary>
    public static int BuildGroundPatch(
        GardenAreaMap map,
        bool[] selectedBySlot,
        HeightSampler height,
        Vector3 centre,
        float radius,
        int maxRun,
        float heightOffset,
        Mesh mesh)
    {
        if (mesh == null) return 0;

        mesh.Clear();

        if (map == null || !map.IsBaked || selectedBySlot == null || height == null) return 0;

        float cell = map.CellSize;
        if (cell <= 0f) return 0;

        int res = map.resolution;
        bool bounded = radius >= 0f;
        float reachSqr = bounded ? radius * radius : 0f;

        int minX = 0, maxX = res - 1, minY = 0, maxY = res - 1;

        if (bounded)
        {
            minX = Mathf.Max(0, Mathf.FloorToInt((centre.x - radius - map.origin.x) / cell));
            maxX = Mathf.Min(res - 1, Mathf.CeilToInt((centre.x + radius - map.origin.x) / cell));
            minY = Mathf.Max(0, Mathf.FloorToInt((centre.z - radius - map.origin.z) / cell));
            maxY = Mathf.Min(res - 1, Mathf.CeilToInt((centre.z + radius - map.origin.z) / cell));
        }

        if (maxX < minX || maxY < minY) return 0;

        BeginCornerCache(minX, minY, maxX, maxY);

        int run = Mathf.Max(1, maxRun);
        int slots = selectedBySlot.Length;

        Vertices.Clear();
        Triangles.Clear();

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * res;
            float z0 = map.origin.z + y * cell;
            float zCentre = z0 + cell * 0.5f;
            float dz = zCentre - centre.z;
            float dzSqr = dz * dz;

            int runStart = 0;
            int runLength = 0;

            // The pass at maxX + 1 is off the end on purpose: it flushes a run that reaches the last
            // column, which a loop ending at maxX would leave unwritten.
            for (int x = minX; x <= maxX + 1; x++)
            {
                bool selected = false;

                if (x <= maxX)
                {
                    byte slot = map.cells[row + x];

                    if (slot < slots && selectedBySlot[slot])
                    {
                        if (bounded)
                        {
                            float dx = map.origin.x + (x + 0.5f) * cell - centre.x;
                            selected = dx * dx + dzSqr <= reachSqr;
                        }
                        else
                        {
                            selected = true;
                        }
                    }
                }

                if (selected && runLength < run)
                {
                    if (runLength == 0) runStart = x;
                    runLength++;
                    continue;
                }

                if (runLength > 0)
                {
                    AddRun(map, height, heightOffset, cell, runStart, y, runLength);
                    runLength = 0;
                }

                if (selected)
                {
                    runStart = x;
                    runLength = 1;
                }
            }
        }

        mesh.indexFormat = Vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(Vertices);
        mesh.SetTriangles(Triangles, 0);
        mesh.RecalculateBounds();

        return Triangles.Count / 6;
    }

    /// <summary>
    /// One row's run of <paramref name="cells"/> cells, as a ground-following strip.
    ///
    /// Heights come from the corner cache, so every lattice point in the patch is asked of the terrain
    /// exactly once however many runs and rows meet there.
    /// </summary>
    private static void AddRun(
        GardenAreaMap map, HeightSampler height, float offset, float cell, int x0, int y, int cells)
    {
        int baseIndex = Vertices.Count;

        float worldX = map.origin.x + x0 * cell;
        float z0 = map.origin.z + y * cell;
        float z1 = z0 + cell;

        for (int i = 0; i <= cells; i++)
        {
            float x = worldX + i * cell;
            Vertices.Add(new Vector3(x, CornerHeight(height, x0 + i, y, x, z0) + offset, z0));
            Vertices.Add(new Vector3(x, CornerHeight(height, x0 + i, y + 1, x, z1) + offset, z1));
        }

        // Wound to face up, matching the rest of the placement overlay.
        for (int i = 0; i < cells; i++)
        {
            int v = baseIndex + i * 2;

            Triangles.Add(v);
            Triangles.Add(v + 1);
            Triangles.Add(v + 2);

            Triangles.Add(v + 2);
            Triangles.Add(v + 1);
            Triangles.Add(v + 3);
        }
    }

    /// <summary>Readies the corner cache for a patch spanning the given cell window.</summary>
    private static void BeginCornerCache(int minX, int minY, int maxX, int maxY)
    {
        _cornerMinX = minX;
        _cornerMinY = minY;
        _cornerPitch = maxX - minX + 2;

        int needed = _cornerPitch * (maxY - minY + 2);

        if (_cornerHeight.Length < needed)
        {
            _cornerHeight = new float[needed];
            _cornerStamp = new int[needed];
            _stamp = 0;
        }

        // Wrapping would make stale entries look current for one rebuild. Clearing on the wrap is a
        // pass over the array once every two billion rebuilds, which is to say never.
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_cornerStamp, 0, _cornerStamp.Length);
            _stamp = 0;
        }

        _stamp++;
    }

    private static float CornerHeight(HeightSampler height, int cx, int cy, float worldX, float worldZ)
    {
        int index = (cy - _cornerMinY) * _cornerPitch + (cx - _cornerMinX);

        if (_cornerStamp[index] == _stamp) return _cornerHeight[index];

        float h = height(worldX, worldZ);
        _cornerHeight[index] = h;
        _cornerStamp[index] = _stamp;
        return h;
    }

    /// <summary>
    /// Which slots the locked-ground overlay covers: areas the player has not unlocked, and nothing else.
    ///
    /// <para><b>Roads are deliberately left bare, even though nothing may be built on them.</b> The
    /// paths are what divide the garden into areas in the first place — greying them over erases the
    /// very shape the overlay exists to explain, and turns a readable network of places into one flat
    /// smear. The player learns "not on the path" from the ghost turning red the once; they need to
    /// see, continuously, which <em>rooms</em> are theirs.</para>
    ///
    /// <para>So this is narrower than <see cref="GardenAreaManager.IsPlaceableAt"/> on purpose: the
    /// rule still refuses roads, sand and water, and the picture no longer claims to mark every inch
    /// the rule refuses. Anything enclosed by an area is folded into that area at bake time, so a
    /// dirt patch inside someone's own garden is neither speckled nor unbuildable.</para>
    /// </summary>
    public static bool[] LockedSlots(GardenAreaMap map, GardenAreaManager areas, ref bool[] buffer)
    {
        return FillLockedSlots(map, area => !areas.IsUnlocked(area), ref buffer);
    }
}
