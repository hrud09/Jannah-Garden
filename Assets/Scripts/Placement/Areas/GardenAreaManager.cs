using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which of the garden's areas the player has earned the right to build in.
///
/// <para>Locking is a <em>placement</em> rule and nothing else. The player walks the whole garden
/// from the first minute — the areas they have not unlocked yet are meant to be seen, so that
/// wanting them is what drives the progression. Nothing here touches movement or the camera.</para>
///
/// <para>Unlock state is keyed by <see cref="GardenAreaDefinition.id"/> rather than by number or by
/// slot, so renumbering the progression or re-baking the map after repainting a road can never hand
/// a returning player a different patch of ground than the one they unlocked.</para>
///
/// <para>Reads <see cref="GardenAreaMap"/>; owns the unlock set and its persistence.</para>
/// </summary>
public class GardenAreaManager : MonoBehaviour
{
    public static GardenAreaManager Instance { get; private set; }

    private const string SaveKey = "garden_area_unlocks";

    /// <summary>
    /// PlayerPrefs key for the locked-area visuals flag. A preference, not progress: it says how the
    /// player wants to be told about the rule, never what the rule is, so it lives alongside the other
    /// display settings rather than in the unlock save.
    /// </summary>
    private const string ShowAlwaysKey = "LockedAreaVisualsAlways";

    [Tooltip("The baked map. Produced by Tools/Jannah Garden/Areas/Bake Area Map.")]
    public GardenAreaMap map;

    [Tooltip("When the map has no area flagged 'unlocked from start', fall back to unlocking the " +
             "lowest-numbered one so a fresh player is never left with nowhere to build.")]
    public bool unlockLowestNumberedIfNoneFlagged = true;

    [Tooltip("Off, every area is placeable and the lock rule does nothing. For testing the rest of " +
             "the garden without unlocking areas one at a time.")]
    public bool enforceLocks = true;

    [Tooltip("Whether roads, sand, water and other ground that belongs to no area at all may be " +
             "built on. Off keeps the paths walkable and the garden's shape readable.")]
    public bool allowPlacementOutsideAreas;

    [Tooltip("Whether an area refuses further items once it is at its limit. Off, the limits still " +
             "drive the zone meters but nothing is ever blocked - for testing a full garden, or for " +
             "a build where the limit is guidance rather than a rule.")]
    public bool enforcePlacementLimits = true;

    [Header("Locked Area Visuals")]
    [Tooltip("ON: the grey over locked ground is visible whenever the player is near it, so they can " +
             "always see which of the garden is theirs. OFF: it only appears while they are actually " +
             "placing something. This is the starting value; once the player changes it in game their " +
             "own choice is remembered and wins.")]
    public bool showLockedAreaVisualsAlways = true;

    [Tooltip("Ignore the player's remembered choice and use the flag above every run. For trying both " +
             "settings in the editor without the last run quietly overriding what you ticked here.")]
    public bool ignoreSavedLockedAreaVisuals;

    /// <summary>
    /// Raised when the locked-area visuals flag changes, carrying the mode it now amounts to.
    ///
    /// <para>The overlay redraws off this rather than polling, because switching it on while the player
    /// is standing still has to take effect at once — and the overlay only otherwise rebuilds when
    /// they move.</para>
    /// </summary>
    public event Action<LockedGroundVisibility> LockedGroundModeChanged;

    /// <summary>Raised after an area is newly unlocked, with the area that opened.</summary>
    public event Action<GardenAreaDefinition> AreaUnlocked;

    /// <summary>Raised whenever the unlock set changes at all, including on load.</summary>
    public event Action UnlocksChanged;

    private readonly HashSet<string> _unlocked = new HashSet<string>();
    private bool _loaded;

    private bool _showAlways;
    private bool _showAlwaysLoaded;

    /// <summary>The XP manager whose level event is currently hooked, so it can be unhooked again.</summary>
    private PlayerXPManager _subscribedXp;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        Load();
        EnsureVisualsFlagLoaded();
    }

    private void Start()
    {
        SyncXpSubscription();
    }

    /// <summary>
    /// Keeps the level subscription pointed at whichever XP manager currently exists.
    ///
    /// <para>Polled rather than hooked once, for the same reason <c>PlacementGridView</c> polls for
    /// this manager: the two are independent scene objects and neither controls the order the other
    /// wakes in, so a subscription taken in <c>Awake</c> would be a coin flip.</para>
    /// </summary>
    private void Update() => SyncXpSubscription();

    private void OnDestroy()
    {
        if (_subscribedXp != null) _subscribedXp.OnXPChanged -= HandleXpChanged;
        _subscribedXp = null;

        if (Instance == this) Instance = null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>True when a map is baked and available to query.</summary>
    public bool IsReady => map != null && map.IsBaked;

    /// <summary>The ids currently unlocked. Read-only snapshot.</summary>
    public IReadOnlyCollection<string> UnlockedIds => _unlocked;

    public bool IsUnlocked(GardenAreaDefinition area) =>
        area != null && !string.IsNullOrEmpty(area.id) && _unlocked.Contains(area.id);

    public bool IsUnlocked(string id) => !string.IsNullOrEmpty(id) && _unlocked.Contains(id);

    /// <summary>
    /// True when an item may stand on the single point <paramref name="world"/>.
    ///
    /// Ground belonging to no area (road, sand, water, off-map) follows
    /// <see cref="allowPlacementOutsideAreas"/>; everything else follows its area's unlock state.
    /// </summary>
    public bool IsPlaceableAt(Vector3 world)
    {
        if (!enforceLocks || !IsReady) return true;

        GardenAreaDefinition area = map.AreaAt(world);
        return area == null ? allowPlacementOutsideAreas : IsUnlocked(area);
    }

    /// <summary>The area covering <paramref name="world"/>, or null.</summary>
    public GardenAreaDefinition AreaAt(Vector3 world) => IsReady ? map.AreaAt(world) : null;

    /// <summary>
    /// Unlocks <paramref name="area"/> and saves. Returns false when it was already open, so callers
    /// can tell a genuine unlock (worth a celebration) from a redundant one.
    /// </summary>
    public bool Unlock(GardenAreaDefinition area)
    {
        if (area == null || string.IsNullOrEmpty(area.id)) return false;
        if (!_unlocked.Add(area.id)) return false;

        Save();
        AreaUnlocked?.Invoke(area);
        UnlocksChanged?.Invoke();
        return true;
    }

    /// <summary>Unlocks the area carrying <paramref name="number"/>. False when unknown or already open.</summary>
    public bool UnlockByNumber(int number) => IsReady && Unlock(map.ByNumber(number));

    /// <summary>
    /// The lowest-numbered area still locked — the next one the progression should offer — or null
    /// once the whole garden is open.
    /// </summary>
    public GardenAreaDefinition NextLockedArea()
    {
        if (!IsReady) return null;

        GardenAreaDefinition best = null;
        foreach (GardenAreaDefinition area in map.areas)
        {
            if (area == null || IsUnlocked(area)) continue;
            if (best == null || area.number < best.number) best = area;
        }

        return best;
    }

    /// <summary>Unlocks the next area in number order. Null when there was none left to open.</summary>
    public GardenAreaDefinition UnlockNext()
    {
        GardenAreaDefinition next = NextLockedArea();
        return next != null && Unlock(next) ? next : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PLACEMENT LIMITS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>How many items <paramref name="area"/> may hold. 0 when there is no map.</summary>
    public int LimitFor(GardenAreaDefinition area) => IsReady ? map.PlacementLimitFor(area) : 0;

    /// <summary>How many items stand in <paramref name="area"/> right now.</summary>
    public int CountIn(GardenAreaDefinition area)
    {
        GardenZoneProgress progress = GardenZoneProgress.Instance;
        return progress != null ? progress.ItemsIn(area) : 0;
    }

    /// <summary>
    /// True when <paramref name="area"/> is already holding everything it may.
    ///
    /// <para>Counted from the placements that exist rather than from a running total, so it cannot
    /// drift out of step with the garden — see <see cref="GardenZoneProgress"/>.</para>
    /// </summary>
    public bool IsFull(GardenAreaDefinition area)
    {
        if (!enforcePlacementLimits || area == null || !IsUnlocked(area)) return false;

        int limit = LimitFor(area);
        return limit > 0 && CountIn(area) >= limit;
    }

    /// <summary>
    /// True when the area covering <paramref name="world"/> is full.
    ///
    /// <para>Asked of the aim point alone and not of every cell an item covers, unlike the unlock
    /// rule: an item that straddles a boundary belongs to the area its centre is in, and counting it
    /// against both would let a full zone be topped up from the edge of the one beside it.</para>
    /// </summary>
    public bool IsFullAt(Vector3 world) => IsFull(AreaAt(world));

    // ═══════════════════════════════════════════════════════════════════════════
    //  LEVEL-DRIVEN UNLOCKS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The XP level <paramref name="area"/> opens at. 0 for an area that is already open.</summary>
    public int LevelFor(GardenAreaDefinition area)
    {
        if (area == null) return 0;
        return IsUnlocked(area) ? 0 : area.RequiredLevel;
    }

    /// <summary>
    /// Opens every area the player's level has earned, and returns the ones that were still shut.
    ///
    /// <para>Written as "bring the world up to this level" rather than "the player just levelled up",
    /// so the same call serves both the level-up moment and load — a player who gained levels while
    /// the areas were unwired, or whose save predates them, is caught up on their next launch instead
    /// of having to level again to collect ground they already earned.</para>
    /// </summary>
    public List<GardenAreaDefinition> SyncUnlocksToLevel(int level)
    {
        List<GardenAreaDefinition> opened = null;
        if (!IsReady) return opened;

        // In number order so a jump of several levels announces the areas in the order the player
        // was meant to meet them, rather than in whatever order the bake happened to list them.
        foreach (GardenAreaDefinition area in map.InNumberOrder())
        {
            if (area == null || IsUnlocked(area) || area.RequiredLevel > level) continue;

            if (Unlock(area)) (opened ??= new List<GardenAreaDefinition>()).Add(area);
        }

        return opened;
    }

    private void SyncXpSubscription()
    {
        PlayerXPManager xp = PlayerXPManager.Instance;
        if (_subscribedXp == xp) return;

        if (_subscribedXp != null) _subscribedXp.OnXPChanged -= HandleXpChanged;

        _subscribedXp = xp;

        if (_subscribedXp == null) return;

        _subscribedXp.OnXPChanged += HandleXpChanged;

        // The level the player already holds, applied the moment the two objects find each other.
        SyncUnlocksToLevel(_subscribedXp.xpLevel);
    }

    private void HandleXpChanged(int level, float currentXp, float xpToNext) => SyncUnlocksToLevel(level);

    // ═══════════════════════════════════════════════════════════════════════════
    //  LOCKED AREA VISUALS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether the grey over locked ground is shown all the time, or only while placing something.
    ///
    /// <para>Owned here rather than on the overlay that draws it, for two reasons: it is a saved player
    /// preference and the overlay is a view that gets rebuilt, and a settings screen needs somewhere to
    /// point that is not "whichever renderer happens to exist right now".</para>
    /// </summary>
    public bool ShowLockedAreaVisualsAlways
    {
        get
        {
            EnsureVisualsFlagLoaded();
            return _showAlways;
        }
        set => SetShowLockedAreaVisualsAlways(value);
    }

    /// <summary>
    /// The flag as the overlay reads it.
    ///
    /// <para>Derived, never stored: one boolean is the whole setting, so there is no second copy of it
    /// to fall out of step. <see cref="LockedGroundVisibility.Never"/> is not reachable from here — the
    /// overlay uses it for its own "there is no area manager, so there is nothing to draw" case.</para>
    /// </summary>
    public LockedGroundVisibility LockedGroundMode => ShowLockedAreaVisualsAlways
        ? LockedGroundVisibility.Always
        : LockedGroundVisibility.DuringPlacementOnly;

    /// <summary>
    /// Sets the flag, saves it, and tells the overlay. A no-op when it is already that, so a settings
    /// screen may call it on every value change without restarting the fade.
    ///
    /// <para>Takes a bool so it drops straight onto <c>Toggle.onValueChanged</c> in the Inspector, the
    /// way the other display settings are wired.</para>
    /// </summary>
    public void SetShowLockedAreaVisualsAlways(bool always)
    {
        EnsureVisualsFlagLoaded();
        if (_showAlways == always) return;

        _showAlways = always;

        PlayerPrefs.SetInt(ShowAlwaysKey, always ? 1 : 0);
        PlayerPrefs.Save();

        LockedGroundModeChanged?.Invoke(LockedGroundMode);
    }

    /// <summary>Flips the flag. For a single settings button rather than a toggle.</summary>
    public void ToggleLockedAreaVisuals() => SetShowLockedAreaVisualsAlways(!ShowLockedAreaVisualsAlways);

    /// <summary>
    /// Reads the saved preference once, falling back to <see cref="showLockedAreaVisualsAlways"/>.
    ///
    /// Lazy rather than only in <c>Awake</c> so the flag is safe to read from another object's own
    /// Awake, where the order between the two is not something either of them controls.
    /// </summary>
    private void EnsureVisualsFlagLoaded()
    {
        if (_showAlwaysLoaded) return;
        _showAlwaysLoaded = true;

        _showAlways = showLockedAreaVisualsAlways;

        if (ignoreSavedLockedAreaVisuals) return;

        _showAlways = PlayerPrefs.GetInt(ShowAlwaysKey, showLockedAreaVisualsAlways ? 1 : 0) == 1;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PERSISTENCE
    // ═══════════════════════════════════════════════════════════════════════════

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var saved = SaveSystem.Load<List<string>>(SaveKey);

        if (saved != null)
        {
            for (int i = 0; i < saved.Count; i++)
            {
                if (!string.IsNullOrEmpty(saved[i])) _unlocked.Add(saved[i]);
            }
        }

        ApplyStartingUnlocks();
        UnlocksChanged?.Invoke();
    }

    /// <summary>
    /// Opens the areas the map flags as starting ground.
    ///
    /// Runs on every load rather than only on a fresh save so that flagging another area as starting
    /// ground — or re-baking a map that gained one — reaches players who already have a save, instead
    /// of only ever affecting brand new ones.
    /// </summary>
    private void ApplyStartingUnlocks()
    {
        if (!IsReady) return;

        bool changed = false;
        bool anyFlagged = false;

        foreach (GardenAreaDefinition area in map.areas)
        {
            if (area == null || !area.unlockedFromStart) continue;
            anyFlagged = true;
            if (!string.IsNullOrEmpty(area.id) && _unlocked.Add(area.id)) changed = true;
        }

        // A map where nobody remembered to flag a starting area would otherwise leave a new player
        // unable to place anything at all, with no in-game way out of it.
        if (!anyFlagged && unlockLowestNumberedIfNoneFlagged && _unlocked.Count == 0)
        {
            GardenAreaDefinition lowest = NextLockedArea();
            if (lowest != null && !string.IsNullOrEmpty(lowest.id) && _unlocked.Add(lowest.id))
            {
                changed = true;
                Debug.LogWarning("[GardenAreaManager] No area is flagged 'unlocked from start' — " +
                                 "opened area " + lowest.number + " so the player has somewhere to build.");
            }
        }

        if (changed) Save();
    }

    private void Save() => SaveSystem.Save(SaveKey, new List<string>(_unlocked));

    /// <summary>Relocks everything but the starting ground. For a save wipe or a testing reset.</summary>
    public void ResetToStart()
    {
        _unlocked.Clear();
        ApplyStartingUnlocks();
        Save();
        UnlocksChanged?.Invoke();
    }
}
