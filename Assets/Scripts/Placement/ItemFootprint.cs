using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Works out how many grid cells an item takes up.
///
/// <para><b>Measured from the prefab, never from the ghost.</b> The ghost may be a separate, lighter
/// preview prefab, and a placed item keeps growing from 0.2x toward full size for as long as its
/// placement timer runs, so neither is a reliable thing to measure. The real prefab asset sits at its
/// authored — that is, fully grown — scale, so that is what gets measured (and the ghost is shown at
/// that same full size).</para>
///
/// <para>Sizes round <em>up</em> to whole cells, so nothing ever claims less ground than it physically
/// covers. At a 1/12 m lattice the rounding waste is under 9 cm per axis.</para>
/// </summary>
public static class ItemFootprint
{
    /// <summary>
    /// XZ extent per prefab, in metres, at full scale. Keyed by prefab because
    /// <see cref="Objectpool"/> hands out recycled instances — an instance is not a stable identity,
    /// and the bounds walk is far too expensive to repeat from Update every frame.
    /// </summary>
    private static readonly Dictionary<GameObject, Vector2> _sizeCache = new Dictionary<GameObject, Vector2>();

    /// <summary>Fallback when a prefab has no measurable geometry at all.</summary>
    private const float DefaultSizeMetres = 0.5f;

    /// <summary>
    /// Fraction of the model's measured body a geometry-derived footprint claims. 1 means an item
    /// reserves exactly the ground it physically covers (Clash-of-Clans style) so nothing else can be
    /// placed inside it. An item's own <c>footprintScale</c> multiplies on top of this, and
    /// <c>gridFootprintOverride</c> replaces it outright for models whose bounds mislead (wide canopies).
    /// </summary>
    private const float DefaultFootprintScale = 1f;

    /// <summary>
    /// Cells claimed by <paramref name="prefab"/> when yawed by <paramref name="rotationSteps"/>
    /// quarter-turns. A non-zero <paramref name="authoredOverride"/> wins outright — designers need
    /// that for a tree whose canopy collider is three times the width of the trunk anyone would
    /// actually expect to plant around, and for path tiles that want deliberate padding. Otherwise the
    /// footprint is derived from measured geometry, shrunk by <paramref name="footprintScale"/> on top
    /// of the built-in <see cref="DefaultFootprintScale"/>.
    /// </summary>
    public static Vector2Int Compute(GameObject prefab, int rotationSteps, float cellSize, Vector2Int authoredOverride, float footprintScale = 1f)
    {
        Vector2Int footprint = authoredOverride.x > 0 && authoredOverride.y > 0
            ? authoredOverride
            : FromGeometry(prefab, cellSize, footprintScale);

        // Odd quarter-turns swap the axes; even ones leave the block as it was.
        if ((rotationSteps & 1) != 0) footprint = new Vector2Int(footprint.y, footprint.x);

        return footprint;
    }

    /// <summary>Cells derived from the model's measured body, shrunk by <paramref name="footprintScale"/>.</summary>
    private static Vector2Int FromGeometry(GameObject prefab, float cellSize, float footprintScale)
    {
        Vector2 size = MeasureSize(prefab) * DefaultFootprintScale * footprintScale;

        return new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize - 0.001f)),
            Mathf.Max(1, Mathf.CeilToInt(size.y / cellSize - 0.001f)));
    }

    /// <summary>The prefab's XZ extent in metres, measured once and remembered.</summary>
    public static Vector2 MeasureSize(GameObject prefab)
    {
        if (prefab == null) return new Vector2(DefaultSizeMetres, DefaultSizeMetres);
        if (_sizeCache.TryGetValue(prefab, out Vector2 cached)) return cached;

        Vector2 size = new Vector2(DefaultSizeMetres, DefaultSizeMetres);

        PlaceableItem placeable = prefab.GetComponent<PlaceableItem>();
        Transform content = placeable != null && placeable.itemGFX != null
            ? placeable.itemGFX
            : prefab.transform;
        bool renderersFallback = placeable == null || placeable.alignUsingRenderersIfNoCollider;

        if (GfxBounds.TryGetLocalBounds(prefab.transform, content, renderersFallback, out Bounds b))
        {
            // The pivot is not necessarily centred in the model. What the item occupies on the ground is
            // the box that both contains the geometry and is centred on the pivot the item is placed by —
            // otherwise an off-centre model would overhang the cells it claimed on one side.
            float halfX = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x));
            float halfZ = Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z));
            size = new Vector2(halfX * 2f, halfZ * 2f);
        }

        _sizeCache[prefab] = size;
        return size;
    }

    /// <summary>
    /// Recovers a quarter-turn count from a saved rotation, relative to the prefab's authored one.
    /// Legacy items saved before rotation existed come back as 0, which is exactly right.
    /// </summary>
    public static int StepsFromRotation(Quaternion placed, Quaternion authored)
    {
        float delta = Mathf.DeltaAngle(authored.eulerAngles.y, placed.eulerAngles.y);
        return ((Mathf.RoundToInt(delta / 90f) % 4) + 4) % 4;
    }

    /// <summary>
    /// The continuous yaw offset of <paramref name="placed"/> relative to <paramref name="authored"/>,
    /// in degrees, signed and in the range (-180, 180]. Used to restore the rotation slider to the
    /// exact facing a relocated item already had, rather than snapping it to the nearest quarter-turn.
    /// </summary>
    public static float YawDegreesFromRotation(Quaternion placed, Quaternion authored)
    {
        return Mathf.DeltaAngle(authored.eulerAngles.y, placed.eulerAngles.y);
    }

    /// <summary>Drops every cached measurement. Called when prefabs are evicted from the addressable cache.</summary>
    public static void ClearCache() => _sizeCache.Clear();
}
