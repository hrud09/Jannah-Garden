using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One of the garden's numbered areas — a pocket of ground the painted roads close off.
///
/// <para>Three different identifiers live here and they are deliberately not the same thing:</para>
/// <list type="bullet">
/// <item><b>Id</b> never changes once the area first exists. Save data keys off it, so renumbering
/// or renaming an area can never hand the player back the wrong unlocked ground.</item>
/// <item><b>Number</b> is the 1..N sequence shown to the player and used to order unlocks. Free to
/// reshuffle at any time — that is the whole reason it is not the id.</item>
/// <item><b>Slot</b> (this entry's index in <see cref="GardenAreaMap.areas"/>, plus one) is what the
/// index grid stores. It is an implementation detail of the bake and is not stable across bakes.</item>
/// </list>
/// </summary>
[Serializable]
public class GardenAreaDefinition
{
    [Tooltip("Stable identity, assigned once when the area is first baked and preserved across " +
             "re-bakes by centroid. Unlock save data keys off this — never edit it by hand.")]
    public string id;

    [Tooltip("The 1..N sequence the player progresses through. Reorder freely; unlocks follow it, " +
             "and save data does not.")]
    public int number;

    [Tooltip("Localization key for the area's name, e.g. 'area.orchard'. Blank falls back to the " +
             "number alone.")]
    public string displayNameKey;

    [Tooltip("True for the single area the player starts with. The bake preserves whatever you set.")]
    public bool unlockedFromStart;

    [Tooltip("World XZ centre of the pocket. Written by the bake; used to re-match this area to its " +
             "pocket after the roads are repainted, and to aim the camera at it.")]
    public Vector2 centroid;

    [Tooltip("World XZ of the point deepest inside the pocket — the furthest any ground in it gets " +
             "from a road. Where the area's number is drawn. Written by the bake.")]
    public Vector2 labelAnchor;

    [Tooltip("Ground area in square metres. Written by the bake.")]
    public float squareMetres;

    [Tooltip("Colour used for this area in the Scene view overlay and the locked-ground visual.")]
    public Color editorColor = Color.white;
}

/// <summary>
/// Which of the garden's areas covers any given patch of ground.
///
/// <para><b>Why an index grid and not polygons:</b> the areas are defined by hand-painted roads that
/// curve and fork. Tracing them as polygons would approximate curves the player can plainly see, and
/// would have to be re-traced by hand every time a path moved. Baking the terrain's own paint into a
/// grid of area slots is exact by construction, re-bakeable in one click, and turns the runtime
/// question "which area is this?" into a single array index — cheaper than any point-in-polygon
/// test, which matters because placement asks it every frame the ghost moves.</para>
///
/// <para>The grid is anchored to the Terrain transform for the same reason <see cref="GardenGrid"/>
/// is: placements round-trip through Firebase as raw world positions, and an anchor computed at
/// runtime could land differently on two devices.</para>
///
/// <para>Baked by <c>Tools/Jannah Garden/Areas/Bake Area Map</c>. Nothing at runtime writes to it.</para>
/// </summary>
[CreateAssetMenu(fileName = "GardenAreaMap", menuName = "Jannah Garden/Garden Area Map")]
public class GardenAreaMap : ScriptableObject
{
    /// <summary>The value stored for ground belonging to no area at all: roads, sand, water, off-map.</summary>
    public const byte NoArea = 0;

    [Header("Baked Grid")]
    [Tooltip("Cells per side. Written by the bake.")]
    public int resolution;

    [Tooltip("Terrain transform position at bake time — the grid's anchor.")]
    public Vector3 origin;

    [Tooltip("Terrain extent in metres (X, Z) at bake time.")]
    public Vector2 size;

    [Tooltip("One byte per cell, row-major with row 0 at minimum Z. 0 means no area; anything else " +
             "is an index into 'areas', plus one.")]
    [HideInInspector]
    public byte[] cells;

    [Header("Areas")]
    public List<GardenAreaDefinition> areas = new List<GardenAreaDefinition>();

    /// <summary>True once a bake has produced a grid that matches the area list.</summary>
    public bool IsBaked =>
        resolution > 0 && cells != null && cells.Length == resolution * resolution && areas.Count > 0;

    /// <summary>Metres per cell along X. Square, because terrain alphamaps are.</summary>
    public float CellSize => resolution > 0 ? size.x / resolution : 0f;

    // ═══════════════════════════════════════════════════════════════════════════
    //  LOOKUP
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The slot covering <paramref name="world"/>, or <see cref="NoArea"/> for road, sand, water and
    /// anything off the terrain. Slots are 1-based indices into <see cref="areas"/>.
    /// </summary>
    public byte SlotAt(Vector3 world)
    {
        if (!IsBaked) return NoArea;

        int x = Mathf.FloorToInt((world.x - origin.x) / size.x * resolution);
        int y = Mathf.FloorToInt((world.z - origin.z) / size.y * resolution);

        if (x < 0 || y < 0 || x >= resolution || y >= resolution) return NoArea;

        return cells[y * resolution + x];
    }

    /// <summary>The area covering <paramref name="world"/>, or null when that ground belongs to none.</summary>
    public GardenAreaDefinition AreaAt(Vector3 world) => BySlot(SlotAt(world));

    /// <summary>The area a slot refers to, or null for <see cref="NoArea"/> or a slot past the list.</summary>
    public GardenAreaDefinition BySlot(byte slot)
    {
        int index = slot - 1;
        return index >= 0 && index < areas.Count ? areas[index] : null;
    }

    /// <summary>The area carrying <paramref name="id"/>, or null.</summary>
    public GardenAreaDefinition ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < areas.Count; i++)
        {
            if (areas[i] != null && areas[i].id == id) return areas[i];
        }

        return null;
    }

    /// <summary>The area carrying <paramref name="number"/>, or null.</summary>
    public GardenAreaDefinition ByNumber(int number)
    {
        for (int i = 0; i < areas.Count; i++)
        {
            if (areas[i] != null && areas[i].number == number) return areas[i];
        }

        return null;
    }

    /// <summary>
    /// The areas in player-facing order. A fresh list each call, so callers may sort or filter it
    /// without disturbing the asset's own ordering, which the index grid depends on.
    /// </summary>
    public List<GardenAreaDefinition> InNumberOrder()
    {
        var ordered = new List<GardenAreaDefinition>(areas);
        ordered.Sort((a, b) => a.number.CompareTo(b.number));
        return ordered;
    }

    /// <summary>World XZ of the centre of cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public Vector3 CellCentre(int x, int y)
    {
        float cell = CellSize;
        return new Vector3(origin.x + (x + 0.5f) * cell, origin.y, origin.z + (y + 0.5f) * cell);
    }
}

/// <summary>
/// When the grey over locked ground is shown to the player.
///
/// <para>Separate from whether the lock is <em>enforced</em>: refusing a placement and explaining the
/// refusal in advance are different jobs, and it must be possible to turn the picture off — to shoot
/// a trailer, or to check the garden's own colours — without quietly making the whole map buildable.</para>
/// </summary>
public enum LockedGroundVisibility
{
    /// <summary>Visible whenever the player is near locked ground, placing or not.</summary>
    Always,

    /// <summary>Only while a placement is open, as context for where the item may go.</summary>
    DuringPlacementOnly,

    /// <summary>Never drawn. Locked ground still refuses placements.</summary>
    Never,
}
