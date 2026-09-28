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

    // Reused between calls: this runs on every placement move, and a fresh pair of lists each time
    // would hand the collector a few hundred kilobytes a second for nothing.
    private static readonly List<Vector3> Vertices = new List<Vector3>();
    private static readonly List<int> Triangles = new List<int>();

    /// <summary>
    /// Fills <paramref name="mesh"/> with quads over every cell <paramref name="selects"/> accepts.
    ///
    /// <paramref name="radius"/> limits the patch to a circle around <paramref name="centre"/>; pass
    /// a negative radius to cover the whole map, which is what the Scene view preview wants and the
    /// in-game overlay never does.
    ///
    /// Returns the number of quads written, so the caller can skip enabling an empty renderer.
    /// </summary>
    public static int BuildGroundPatch(
        GardenAreaMap map,
        Func<byte, bool> selects,
        HeightSampler height,
        Vector3 centre,
        float radius,
        int maxRun,
        float heightOffset,
        Mesh mesh)
    {
        if (mesh == null) return 0;

        mesh.Clear();

        if (map == null || !map.IsBaked || selects == null || height == null) return 0;

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

        int run = Mathf.Max(1, maxRun);

        Vertices.Clear();
        Triangles.Clear();

        for (int y = minY; y <= maxY; y++)
        {
            float z0 = map.origin.z + y * cell;
            float zCentre = z0 + cell * 0.5f;
            int runStart = 0;
            int runLength = 0;

            // The pass at maxX + 1 is off the end on purpose: it flushes a run that reaches the last
            // column, which a loop ending at maxX would leave unwritten.
            for (int x = minX; x <= maxX + 1; x++)
            {
                bool selected = false;

                if (x <= maxX)
                {
                    if (bounded)
                    {
                        float dx = map.origin.x + (x + 0.5f) * cell - centre.x;
                        float dz = zCentre - centre.z;
                        selected = dx * dx + dz * dz <= reachSqr && selects(map.cells[y * res + x]);
                    }
                    else
                    {
                        selected = selects(map.cells[y * res + x]);
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
                    AddRun(height, heightOffset, map.origin.x + runStart * cell, z0, cell, runLength);
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
    /// One row's run of <paramref name="cells"/> cells, as a ground-following strip anchored at
    /// <paramref name="x0"/>, <paramref name="z0"/>.
    ///
    /// Heights are sampled at every cell boundary rather than only at the run's ends, which is what
    /// keeps the sheet on the ground instead of chording across dips in it.
    /// </summary>
    private static void AddRun(HeightSampler height, float offset, float x0, float z0, float cell, int cells)
    {
        int baseIndex = Vertices.Count;
        float z1 = z0 + cell;

        for (int i = 0; i <= cells; i++)
        {
            float x = x0 + i * cell;
            Vertices.Add(new Vector3(x, height(x, z0) + offset, z0));
            Vertices.Add(new Vector3(x, height(x, z1) + offset, z1));
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
    public static Func<byte, bool> LockedSlots(GardenAreaMap map, GardenAreaManager areas)
    {
        return slot =>
        {
            if (slot == GardenAreaMap.NoArea) return false;

            GardenAreaDefinition area = map.BySlot(slot);
            return area != null && !areas.IsUnlocked(area);
        };
    }
}
