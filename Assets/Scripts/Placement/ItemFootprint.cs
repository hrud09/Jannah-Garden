using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Works out how many grid cells an item takes up.
///
/// <para><b>Measured from the prefab, never from the ghost.</b> The preview is spawned at
/// <c>SetScaleMultiplier(0.2f)</c> and items keep growing toward full size for as long as their
/// placement timer runs. A footprint taken from the ghost would have a sapling claim a patch a fifth
/// of the size it eventually needs, and the fully grown tree would then be intersecting whatever the
/// player tucked in beside it. The prefab asset sits at its authored — that is, fully grown — scale,
/// so that is what gets measured.</para>
///
/// <para>Sizes round <em>up</em> to whole cells, so nothing ever claims less ground than it physically
/// covers. At a 0.25 m lattice the rounding waste is under a quarter-metre per axis.</para>
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
    /// Cells claimed by <paramref name="prefab"/> when yawed by <paramref name="rotationSteps"/>
    /// quarter-turns. A non-zero <paramref name="authoredOverride"/> wins outright — designers need
    /// that for a tree whose canopy collider is three times the width of the trunk anyone would
    /// actually expect to plant around, and for path tiles that want deliberate padding.
    /// </summary>
    public static Vector2Int Compute(GameObject prefab, int rotationSteps, float cellSize, Vector2Int authoredOverride)
    {
        Vector2Int footprint = authoredOverride.x > 0 && authoredOverride.y > 0
            ? authoredOverride
            : FromGeometry(prefab, cellSize);

        // Odd quarter-turns swap the axes; even ones leave the block as it was.
        if ((rotationSteps & 1) != 0) footprint = new Vector2Int(footprint.y, footprint.x);

        return footprint;
    }

    /// <summary>Cells derived purely from the model's measured body.</summary>
    private static Vector2Int FromGeometry(GameObject prefab, float cellSize)
    {
        Vector2 size = MeasureSize(prefab);

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
