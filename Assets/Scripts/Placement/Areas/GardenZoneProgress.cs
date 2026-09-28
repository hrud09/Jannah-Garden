using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How finished each garden area looks, as a number between 0 and 1.
///
/// <para><b>Why this exists at all:</b> the areas are the game's way of teaching arrangement. Without
/// something that answers "is this one done?", a player fills the first pocket with fifteen identical
/// palm trees and moves on, which is exactly the habit the division of the garden was meant to break.
/// The score is therefore deliberately <em>not</em> "how many items are in here": it rewards filling
/// the space, using different things, and spreading them out — and it stops rewarding more once a
/// pocket is crowded.</para>
///
/// <para><b>Nothing is saved.</b> The score is a reading of the placements that already exist, so it
/// is derived on demand from <see cref="ItemPlacementManager.ActivePlacedItems"/> and recomputed when
/// that list changes. A stored copy would be a second source of truth for something the garden itself
/// already states, and would go wrong the first time a save was restored on another device.</para>
/// </summary>
public class GardenZoneProgress : MonoBehaviour
{
    public static GardenZoneProgress Instance { get; private set; }

    [Header("Tuning")]
    [Tooltip("How many different kinds of item an area wants before variety counts as satisfied.")]
    [Range(1, 12)]
    public int varietyTarget = 5;

    [Tooltip("Edge of the coarse tile the spread test counts, in metres. Two items in the same tile " +
             "count once, so clustering everything in one corner cannot fill this part of the score.")]
    [Range(2f, 20f)]
    public float spreadTileMetres = 6f;

    [Header("Weights")]
    [Range(0f, 1f)] public float coverageWeight = 0.5f;
    [Range(0f, 1f)] public float varietyWeight = 0.25f;
    [Range(0f, 1f)] public float spreadWeight = 0.25f;

    /// <summary>Raised after the scores have been recomputed, so a meter can redraw without polling.</summary>
    public event System.Action Changed;

    /// <summary>Score per area id. Areas with nothing in them are simply absent.</summary>
    private readonly Dictionary<string, float> _scores = new Dictionary<string, float>();

    // Rebuilt per recompute rather than per area, so one pass over the placed items serves every area.
    private readonly Dictionary<string, Tally> _tallies = new Dictionary<string, Tally>();

    private bool _dirty = true;

    /// <summary>What one area's placements add up to, before any of it is turned into a score.</summary>
    private class Tally
    {
        public int items;
        public readonly HashSet<string> kinds = new HashSet<string>();
        public readonly HashSet<long> tiles = new HashSet<long>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        ItemPlacementManager.OnItemPlaced += HandleGardenChanged;
        ItemPlacementManager.OnItemRemoved += HandleGardenChanged;
    }

    private void OnDisable()
    {
        ItemPlacementManager.OnItemPlaced -= HandleGardenChanged;
        ItemPlacementManager.OnItemRemoved -= HandleGardenChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void HandleGardenChanged(PlaceableItem item) => MarkDirty();

    /// <summary>
    /// Marks the scores stale. Recomputing is deferred to the next read rather than done here, because
    /// restoring a saved garden raises the placed event once per item and would otherwise walk the
    /// whole list once per item on the way in.
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;
        Changed?.Invoke();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  READING
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>How finished <paramref name="area"/> looks, 0..1. Empty ground scores 0.</summary>
    public float ScoreFor(GardenAreaDefinition area)
    {
        if (area == null || string.IsNullOrEmpty(area.id)) return 0f;

        Recompute();
        return _scores.TryGetValue(area.id, out float score) ? score : 0f;
    }

    /// <summary>How many items stand in <paramref name="area"/>. For a meter's own caption.</summary>
    public int ItemsIn(GardenAreaDefinition area)
    {
        if (area == null || string.IsNullOrEmpty(area.id)) return 0;

        Recompute();
        return _tallies.TryGetValue(area.id, out Tally tally) ? tally.items : 0;
    }

    /// <summary>
    /// The number of items <paramref name="area"/> asks for before it reads as fully planted — which
    /// is the same number the placement rule stops accepting at.
    ///
    /// <para>Deliberately one number and not two. A meter that fills at twelve while the rule refuses
    /// the eleventh, or the other way round, teaches the player that neither can be trusted; the whole
    /// point of showing a count is that it is the count that matters.</para>
    /// </summary>
    public int TargetItemsFor(GardenAreaDefinition area)
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        int limit = areas != null ? areas.LimitFor(area) : 0;

        return limit > 0 ? limit : 1;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SCORING
    // ═══════════════════════════════════════════════════════════════════════════

    private void Recompute()
    {
        if (!_dirty) return;
        _dirty = false;

        _scores.Clear();
        _tallies.Clear();

        GardenAreaManager areas = GardenAreaManager.Instance;
        ItemPlacementManager placement = ItemPlacementManager.Instance;

        if (areas == null || !areas.IsReady || placement == null) return;

        IReadOnlyList<PlaceableItem> placed = placement.ActivePlacedItems;

        for (int i = 0; i < placed.Count; i++)
        {
            PlaceableItem item = placed[i];
            if (item == null) continue;

            GardenAreaDefinition area = areas.AreaAt(item.transform.position);
            if (area == null || string.IsNullOrEmpty(area.id)) continue;

            if (!_tallies.TryGetValue(area.id, out Tally tally))
            {
                tally = new Tally();
                _tallies[area.id] = tally;
            }

            tally.items++;

            // Items bought from the shop carry the id of what they are; anything else (a reward, an
            // item from before the field existed) still counts towards the total, just not towards
            // variety, which is the only part of the score that needs to tell two things apart.
            if (!string.IsNullOrEmpty(item.sourceItemId)) tally.kinds.Add(item.sourceItemId);

            tally.tiles.Add(TileKey(item.transform.position));
        }

        foreach (GardenAreaDefinition area in areas.map.areas)
        {
            if (area == null || string.IsNullOrEmpty(area.id)) continue;
            if (!_tallies.TryGetValue(area.id, out Tally tally)) continue;

            _scores[area.id] = Score(area, tally);
        }
    }

    private float Score(GardenAreaDefinition area, Tally tally)
    {
        int target = TargetItemsFor(area);

        // Under the target, more items help; past it the score comes back down again. With limits
        // enforced the placement rule stops the player getting there at all, so this is what a garden
        // that predates the limit — or one with enforcement turned off — reads as: a pocket packed
        // shoulder to shoulder is not a finished pocket, and the meter has to be willing to say so.
        float coverage = tally.items <= target
            ? (float)tally.items / target
            : Mathf.Clamp01(1f - (tally.items - target) / (float)Mathf.Max(1, target));

        float variety = Mathf.Clamp01(tally.kinds.Count / (float)Mathf.Max(1, varietyTarget));

        // How many tiles the area could offer at all, so a small pocket is not asked to spread across
        // ground it does not have.
        float tileArea = spreadTileMetres * spreadTileMetres;
        int tilesAvailable = Mathf.Max(1, Mathf.RoundToInt(area.squareMetres / Mathf.Max(1f, tileArea)));
        int tilesWanted = Mathf.Max(1, Mathf.Min(tilesAvailable, target));
        float spread = Mathf.Clamp01(tally.tiles.Count / (float)tilesWanted);

        float total = coverageWeight + varietyWeight + spreadWeight;
        if (total <= 0f) return 0f;

        return Mathf.Clamp01(
            (coverage * coverageWeight + variety * varietyWeight + spread * spreadWeight) / total);
    }

    /// <summary>
    /// A world position reduced to its coarse tile, packed into one long so a set can hold it.
    ///
    /// Offset before the floor so positions either side of the world origin do not both collapse onto
    /// tile zero, which would quietly merge two distant corners of the garden into one.
    /// </summary>
    private long TileKey(Vector3 world)
    {
        float size = Mathf.Max(1f, spreadTileMetres);
        long x = Mathf.FloorToInt(world.x / size) + 100000;
        long z = Mathf.FloorToInt(world.z / size) + 100000;
        return (x << 20) | z;
    }
}
