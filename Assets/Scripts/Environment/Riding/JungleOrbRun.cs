using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The Jungle Orb Run: a mini-game played on camelback in the jungle.
///
/// <para>Whenever the player is in the jungle (on foot or riding) a field of <see cref="orbCount"/>
/// glowing orbs is laid out around them. Walking or riding through an orb collects it; the first one
/// collected starts a <see cref="timeLimit"/>-second timer. Collecting every orb in time wins
/// <see cref="rewardCoins"/> Noor Coins, with the offer to double them by watching an ad. When time runs
/// out the field is cleared and, after a short pause, a fresh one is laid out. Riding a camel also puts an
/// "Orb Run" button beside Dismount that lays out a fresh field and starts the clock straight away.</para>
///
/// <para>Orbs are placed among the trees (not only on the road) within <see cref="fieldRadius"/> of the
/// player, never inside a tree, rock or any other collider, off water and steep slopes, spread apart and
/// reachable from where the player stands.</para>
/// </summary>
public class JungleOrbRun : MonoBehaviour
{
    private static JungleOrbRun _instance;

    public static JungleOrbRun Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<JungleOrbRun>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    [Header("Game")]
    [Tooltip("Orbs to collect in one run.")]
    [Range(3, 40)]
    public int orbCount = 20;

    [Tooltip("Time allowed for one run (seconds), counted from the first orb collected.")]
    public float timeLimit = 60f;

    [Tooltip("Noor Coins for collecting every orb (doubled by watching an ad).")]
    public int rewardCoins = 50;

    [Tooltip("How close (metres, measured flat) the player, or their mount's back, must pass to collect an orb.")]
    public float collectRadius = 2.5f;

    [Tooltip("How far above or below the orb (metres) still counts, so it works on foot and on a camel.")]
    public float collectHeight = 3f;

    [Tooltip("Orb height above the ground (metres): reachable on foot and from a camel's back.")]
    public float orbHeight = 1.8f;

    [Tooltip("Orbs keep at least this far apart (metres).")]
    public float minSpacing = 8f;

    [Tooltip("Orbs are laid out within this distance of the player (metres). Keeps a full field " +
             "collectable inside the time limit.")]
    public float fieldRadius = 40f;

    [Tooltip("Free space needed around an orb (metres) — keeps orbs out of trees, rocks and props.")]
    public float clearance = 1.5f;

    [Tooltip("Seconds after a run ends (won or timed out) before a fresh field is laid out.")]
    public float respawnDelay = 8f;

    [Tooltip("An untouched field is cleared once the player has been out of the jungle this long (seconds).")]
    public float leaveJungleGrace = 3f;

    [Header("Art")]
    [Tooltip("What an orb looks like. Built from the question-mark orb's glow by the ride features builder.")]
    public GameObject orbPrefab;

    [Header("HUD")]
    public GameObject hud;
    public TMP_Text countText;
    public TMP_Text timerText;

    /// <summary>True while the clock is running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True while a field of orbs is out (waiting for the first pick-up, or mid-run).</summary>
    public bool HasField => _orbs.Count > 0 || IsRunning;

    private readonly List<GameObject> _orbs = new List<GameObject>();
    private AnimalRider _rider;
    private Transform _player;
    private float _endTime;
    private int _collected;
    private int _total;
    private int _lastSecond = -1;
    private float _nextFieldTime;
    private float _outOfJungleSince = -1f;
    private float _nextPlayerLookup;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
        if (hud != null) hud.SetActive(false);
    }

    /// <summary>Whether the Orb Run button belongs on screen: riding a camel (not a flier) in the jungle.</summary>
    public bool CanOffer(AnimalRider rider)
    {
        if (IsRunning || rider == null || !rider.IsRiding || rider.IsTransitioning) return false;
        if (_collected > 0) return false; // A field is mid-collection (the clock starts on the first orb).
        if (rider.Mount == null || rider.Mount.canFly) return false;
        if (rider.Driver != null) return false; // Not during Auto Travel.
        return RideRoutes.InJungle(rider.Mount.transform.position);
    }

    /// <summary>Shows the rules and a Start button.</summary>
    public void Offer(AnimalRider rider)
    {
        if (!CanOffer(rider)) return;

        RidePromptPanel panel = RidePromptPanel.Instance;
        if (panel == null)
        {
            Begin(rider);
            return;
        }

        RideFeatures features = RideFeatures.Instance;
        panel.Show(
            RideFeatures.Loc("orb_run.title"),
            RideFeatures.Loc("orb_run.body", orbCount, Mathf.RoundToInt(timeLimit), rewardCoins),
            RideFeatures.Loc("orb_run.note"),
            features != null ? features.orbIcon : null,
            RideFeatures.Loc("orb_run.start"), false, () => Begin(rider),
            RideFeatures.Loc("orb_run.not_now"), null,
            null);
    }

    /// <summary>Lays out a fresh field around the camel and starts the clock straight away.</summary>
    public void Begin(AnimalRider rider)
    {
        if (!CanOffer(rider)) return;
        _rider = rider;
        if (!LayField(rider.Mount.transform.position, false)) return;
        StartClock();
        RideFeatures.Toast(RideFeatures.Loc("orb_run.go"), ToastTone.Info);
    }

    /// <summary>
    /// Places <see cref="orbCount"/> orbs around <paramref name="centre"/> and shows the HUD with the full
    /// time still on it. False (with a toast unless <paramref name="quiet"/>) when there was no room.
    /// </summary>
    private bool LayField(Vector3 centre, bool quiet)
    {
        List<Vector3> spots = PickSpots(centre);
        if (spots.Count < 3)
        {
            if (!quiet) RideFeatures.Toast(RideFeatures.Loc("orb_run.no_spots"), ToastTone.Warning);
            return false;
        }

        ClearOrbs();
        foreach (Vector3 spot in spots) _orbs.Add(SpawnOrb(spot + Vector3.up * orbHeight));

        _total = _orbs.Count;
        _collected = 0;
        _lastSecond = -1;
        IsRunning = false;
        _outOfJungleSince = -1f;

        if (hud != null) UIToggleMotion.SetActive(hud, true, 0f, new Vector2(0f, 40f));
        RefreshHud(true);
        GameSfx.Play(SoundEffect.OrbAppear);
        return true;
    }

    private void StartClock()
    {
        _endTime = Time.time + timeLimit;
        _lastSecond = -1;
        IsRunning = true;
        RefreshHud(true);
    }

    private void Update()
    {
        if (!ResolveCollector(out Vector3 collector, out bool autoDriven)) return;

        bool inJungle = RideRoutes.InJungle(collector);

        // No field out: lay one when the player is in the jungle and the pause after the last run is over.
        if (!HasField)
        {
            if (inJungle && !autoDriven && Time.time >= _nextFieldTime)
            {
                if (LayField(collector, true))
                    RideFeatures.Toast(RideFeatures.Loc("orb_run.field_hint", _total, Mathf.RoundToInt(timeLimit)), ToastTone.Info);
                else
                    _nextFieldTime = Time.time + respawnDelay; // No room here; try again in a bit.
            }
            return;
        }

        if (IsRunning && Time.time >= _endTime)
        {
            End(RideFeatures.Loc("orb_run.time_up"));
            return;
        }

        // An untouched field is tidied away once the player has left the jungle for a moment.
        if (!IsRunning)
        {
            if (inJungle) _outOfJungleSince = -1f;
            else if (_outOfJungleSince < 0f) _outOfJungleSince = Time.time;
            else if (Time.time - _outOfJungleSince > leaveJungleGrace)
            {
                ClearField();
                return;
            }
        }

        // Auto Travel steers on its own: it doesn't get to pick orbs up for the player.
        if (!autoDriven)
        {
            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                GameObject orb = _orbs[i];
                if (orb == null) { _orbs.RemoveAt(i); continue; }

                Vector3 d = orb.transform.position - collector;
                float vertical = Mathf.Abs(d.y);
                d.y = 0f;
                if (d.magnitude > collectRadius || vertical > collectHeight) continue;

                Collect(orb);
                _orbs.RemoveAt(i);
                if (!IsRunning) StartClock(); // The first orb starts the clock.
            }
        }

        RefreshHud(false);

        if (_collected >= _total) Win();
    }

    /// <summary>
    /// Where pick-ups are measured from: the mount's back while riding, otherwise the player's body.
    /// <paramref name="autoDriven"/> is true while Auto Travel is steering.
    /// </summary>
    private bool ResolveCollector(out Vector3 collector, out bool autoDriven)
    {
        collector = default;
        autoDriven = false;

        if ((_player == null || _rider == null) && Time.unscaledTime >= _nextPlayerLookup)
        {
            _nextPlayerLookup = Time.unscaledTime + 1f;
            if (_rider == null) _rider = FindFirstObjectByType<AnimalRider>();
            if (_player == null)
            {
                GameObject tagged = null;
                try { tagged = GameObject.FindGameObjectWithTag("Player"); }
                catch (UnityException) { }
                _player = tagged != null ? tagged.transform : _rider != null ? _rider.transform : null;
            }
        }

        if (_rider != null && _rider.IsRiding && _rider.Mount != null)
        {
            collector = _rider.Mount.SeatPosition;
            autoDriven = _rider.Driver != null;
            return true;
        }

        if (_player == null) return false;
        collector = _player.position + Vector3.up * 1f;
        return true;
    }

    private void Collect(GameObject orb)
    {
        _collected++;
        GameSfx.PlayAt(SoundEffect.OrbCollect, orb.transform.position);
        if (orb.TryGetComponent(out JungleOrb jungleOrb)) jungleOrb.Collect();
        else Destroy(orb);
    }

    private void Win()
    {
        ClearField();
        GameSfx.Play(SoundEffect.TreasureBoxSetComplete);

        int coins = rewardCoins;
        RidePromptPanel panel = RidePromptPanel.Instance;
        if (panel == null)
        {
            Grant(coins);
            return;
        }

        RideFeatures features = RideFeatures.Instance;
        panel.Show(
            RideFeatures.Loc("orb_run.complete_title"),
            RideFeatures.Loc("orb_run.complete_body", _total, coins),
            RideFeatures.Loc("orb_run.double_note", coins * 2),
            features != null ? features.coinIcon : null,
            RideFeatures.Loc("orb_run.double", coins * 2), true,
            () => RideAdGate.ShowAd(earned =>
            {
                if (!earned) RideFeatures.Toast(RideFeatures.Loc("orb_run.double_failed"), ToastTone.Warning);
                Grant(earned ? coins * 2 : coins);
            }),
            RideFeatures.Loc("orb_run.collect", coins), () => Grant(coins),
            // Closing the reward still pays it: the orbs were collected.
            () => Grant(coins));
    }

    private static void Grant(int coins)
    {
        if (NoorCoinManager.Instance != null)
        {
            NoorCoinManager.Instance.Earn(coins, true, "OrbRun", "jungle_orb_run");
        }
    }

    private void End(string message)
    {
        ClearField();
        RideFeatures.Toast(message, ToastTone.Warning);
    }

    /// <summary>
    /// Clears the field without a message (Auto Travel starting, scene teardown, tests). A fresh one is
    /// laid out after <see cref="respawnDelay"/> if the player is still in the jungle.
    /// </summary>
    public void Cancel()
    {
        if (!HasField) return;
        ClearField();
    }

    private void ClearField()
    {
        IsRunning = false;
        _collected = 0;
        ClearOrbs();
        HideHud();
        _nextFieldTime = Time.time + respawnDelay;
        _outOfJungleSince = -1f;
    }

    private void OnDisable() => Cancel();

    // ─── HUD ────────────────────────────────────────────────────────────────────

    private int _lastCollected = -1, _lastTotal = -1;

    private void RefreshHud(bool force)
    {
        // Only rebuild the count string when it changes: this runs every frame while an orb field exists.
        if (countText != null && (force || _collected != _lastCollected || _total != _lastTotal))
        {
            _lastCollected = _collected;
            _lastTotal = _total;
            LocalizedRendering.SetPlainText(countText, _collected + " / " + _total);
        }

        // Before the first pick-up the clock shows the full time it will start from.
        float remaining = IsRunning ? _endTime - Time.time : timeLimit;
        int left = Mathf.CeilToInt(Mathf.Max(0f, remaining));
        if (timerText != null && (force || left != _lastSecond))
        {
            _lastSecond = left;
            LocalizedRendering.SetPlainText(timerText, (left / 60) + ":" + (left % 60).ToString("00"));
            // The last ten seconds read in warm red.
            timerText.color = IsRunning && left <= 10 ? new Color(0.96f, 0.55f, 0.45f) : new Color(0.98f, 0.96f, 0.93f);
        }
    }

    private void HideHud()
    {
        if (hud != null) UIToggleMotion.SetActive(hud, false, 0f, new Vector2(0f, 40f));
    }

    // ─── Orbs ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Up to <see cref="orbCount"/> spots around <paramref name="centre"/>. Thick jungle can leave too
    /// little open ground at the full spacing, so each pass that falls short reaches a bit further out and
    /// packs the orbs a bit closer, until the full count fits.
    /// </summary>
    private List<Vector3> PickSpots(Vector3 centre)
    {
        // (radius, spacing, clearance) multipliers, loosest last.
        Vector3[] passes =
        {
            new Vector3(1f, 1f, 1f),
            new Vector3(1.3f, 0.85f, 0.85f),
            new Vector3(1.6f, 0.7f, 0.7f),
            new Vector3(2f, 0.55f, 0.6f),
        };

        List<Vector3> best = new List<Vector3>();
        foreach (Vector3 k in passes)
        {
            List<Vector3> spots = PickSpots(centre, fieldRadius * k.x, minSpacing * k.y, clearance * k.z);
            if (spots.Count > best.Count) best = spots;
            if (best.Count >= orbCount) break;
        }
        return best;
    }

    /// <summary>
    /// Jungle ground within <paramref name="radius"/> of <paramref name="centre"/>: off water and steep
    /// slopes, clear of every collider (trees, rocks, lanterns, props) by <paramref name="clear"/>,
    /// reachable from the centre, <paramref name="spacing"/> apart and not right where the player stands.
    /// </summary>
    private List<Vector3> PickSpots(Vector3 centre, float radius, float spacing, float clear)
    {
        var candidates = new List<Vector3>();
        GardenPathfinder pf = RideRoutes.Pathfinder;
        Terrain terrain = RideRoutes.Terrain;
        const float step = 2.5f;

        for (float x = -radius; x <= radius; x += step)
        {
            for (float z = -radius; z <= radius; z += step)
            {
                if (x * x + z * z > radius * radius) continue;
                // A little jitter so the field doesn't read as a grid.
                var p = new Vector3(centre.x + x + Random.Range(-1f, 1f), 0f, centre.z + z + Random.Range(-1f, 1f));
                if (!RideRoutes.InJungle(p) || !RideRoutes.InPlayArea(p, 6f)) continue;
                p.y = RideRoutes.GroundHeight(p);
                if (FlatDistance(p, centre) < 6f) continue;
                if (terrain != null && Steepness(terrain, p) > 30f) continue;
                if (!RideRoutes.IsClear(p, clear, orbHeight + 1f)) continue;
                candidates.Add(p);
            }
        }

        List<Vector3> spread = RideRoutes.Spread(candidates, orbCount * 3, centre);
        var picked = new List<Vector3>();
        foreach (Vector3 p in spread)
        {
            if (picked.Count >= orbCount) break;
            bool tooClose = false;
            foreach (Vector3 q in picked)
            {
                if (FlatDistance(p, q) < spacing) { tooClose = true; break; }
            }
            if (tooClose) continue;
            // Water, cliffs and the lake: skip anything the player can't actually get to.
            if (pf != null && pf.FindPath(centre, p) == null) continue;
            picked.Add(p);
        }
        return picked;
    }

    private static float Steepness(Terrain terrain, Vector3 world)
    {
        TerrainData data = terrain.terrainData;
        Vector3 local = world - terrain.transform.position;
        return data.GetSteepness(local.x / data.size.x, local.z / data.size.z);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private GameObject SpawnOrb(Vector3 at)
    {
        GameObject orb = orbPrefab != null
            ? Instantiate(orbPrefab, at, Quaternion.identity, transform)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Jungle Orb";
        orb.transform.position = at;
        if (orbPrefab == null)
        {
            Destroy(orb.GetComponent<Collider>());
            orb.transform.localScale = Vector3.one * 0.8f;
        }
        if (!orb.TryGetComponent(out JungleOrb _)) orb.AddComponent<JungleOrb>();
        return orb;
    }

    private void ClearOrbs()
    {
        foreach (GameObject orb in _orbs)
        {
            if (orb != null) Destroy(orb);
        }
        _orbs.Clear();
    }
}
