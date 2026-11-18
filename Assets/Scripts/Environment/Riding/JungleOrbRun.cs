using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The Jungle Orb Run: a mini-game played on camelback in the jungle.
///
/// <para>Riding a camel in the jungle puts an "Orb Run" button beside Dismount. Starting it scatters
/// <see cref="orbCount"/> glowing orbs along the jungle path, each at camel-back height, and starts a
/// timer. Riding through an orb collects it. Collecting every one before time runs out wins
/// <see cref="rewardCoins"/> Noor Coins, with the offer to double them by watching an ad. Running out of
/// time, getting off, or swapping onto a pegasus ends the run with nothing.</para>
///
/// <para>Orbs are placed on the jungle's painted road (so a camel can always reach them), spread as far
/// apart as possible, with room around them and a route from the camel's start.</para>
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
    [Range(3, 30)]
    public int orbCount = 10;

    [Tooltip("Time allowed for one run (seconds).")]
    public float timeLimit = 240f;

    [Tooltip("Noor Coins for collecting every orb (doubled by watching an ad).")]
    public int rewardCoins = 50;

    [Tooltip("How close (metres, measured flat) the camel's back must pass to collect an orb.")]
    public float collectRadius = 3f;

    [Tooltip("Orb height above the ground (metres): about the rider's chest on a camel.")]
    public float orbHeight = 3.3f;

    [Tooltip("Orbs keep at least this far apart (metres).")]
    public float minSpacing = 12f;

    [Header("Art")]
    [Tooltip("What an orb looks like. Built from the question-mark orb's glow by the ride features builder.")]
    public GameObject orbPrefab;

    [Header("HUD")]
    public GameObject hud;
    public TMP_Text countText;
    public TMP_Text timerText;

    /// <summary>True while a run is on.</summary>
    public bool IsRunning { get; private set; }

    private readonly List<GameObject> _orbs = new List<GameObject>();
    private AnimalRider _rider;
    private float _endTime;
    private int _collected;
    private int _total;
    private int _lastSecond = -1;

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
            RideFeatures.Loc("orb_run.body", orbCount, Mathf.RoundToInt(timeLimit / 60f), rewardCoins),
            RideFeatures.Loc("orb_run.note"),
            features != null ? features.orbIcon : null,
            RideFeatures.Loc("orb_run.start"), false, () => Begin(rider),
            RideFeatures.Loc("orb_run.not_now"), null,
            null);
    }

    /// <summary>Places the orbs and starts the clock.</summary>
    public void Begin(AnimalRider rider)
    {
        if (!CanOffer(rider)) return;

        List<Vector3> spots = PickSpots(rider.Mount.transform.position);
        if (spots.Count < 3)
        {
            RideFeatures.Toast(RideFeatures.Loc("orb_run.no_spots"), ToastTone.Warning);
            return;
        }

        ClearOrbs();
        foreach (Vector3 spot in spots) _orbs.Add(SpawnOrb(spot + Vector3.up * orbHeight));

        _rider = rider;
        _total = _orbs.Count;
        _collected = 0;
        _endTime = Time.time + timeLimit;
        _lastSecond = -1;
        IsRunning = true;

        if (hud != null) UIToggleMotion.SetActive(hud, true, 0f, new Vector2(0f, 40f));
        RefreshHud(true);
        GameSfx.Play(SoundEffect.OrbAppear);
        RideFeatures.Toast(RideFeatures.Loc("orb_run.go"), ToastTone.Info);
    }

    private void Update()
    {
        if (!IsRunning) return;

        // Off the camel, onto something else, or the camel taken away: the run is over.
        if (_rider == null || !_rider.IsRiding || _rider.Mount == null || _rider.Mount.canFly)
        {
            End(RideFeatures.Loc("orb_run.abandoned"));
            return;
        }

        if (Time.time >= _endTime)
        {
            End(RideFeatures.Loc("orb_run.time_up"));
            return;
        }

        Vector3 seat = _rider.Mount.SeatPosition;
        for (int i = _orbs.Count - 1; i >= 0; i--)
        {
            GameObject orb = _orbs[i];
            if (orb == null) { _orbs.RemoveAt(i); continue; }

            Vector3 d = orb.transform.position - seat;
            float vertical = Mathf.Abs(d.y);
            d.y = 0f;
            if (d.magnitude > collectRadius || vertical > 2.5f) continue;

            Collect(orb);
            _orbs.RemoveAt(i);
        }

        RefreshHud(false);

        if (_collected >= _total) Win();
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
        IsRunning = false;
        ClearOrbs();
        HideHud();
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
        IsRunning = false;
        ClearOrbs();
        HideHud();
        RideFeatures.Toast(message, ToastTone.Warning);
    }

    /// <summary>Stops a run without a message (scene teardown, tests).</summary>
    public void Cancel()
    {
        if (!IsRunning) return;
        IsRunning = false;
        ClearOrbs();
        HideHud();
    }

    private void OnDisable() => Cancel();

    // ─── HUD ────────────────────────────────────────────────────────────────────

    private void RefreshHud(bool force)
    {
        if (countText != null)
        {
            LocalizedRendering.SetPlainText(countText, _collected + " / " + _total);
        }

        int left = Mathf.CeilToInt(Mathf.Max(0f, _endTime - Time.time));
        if (timerText != null && (force || left != _lastSecond))
        {
            _lastSecond = left;
            LocalizedRendering.SetPlainText(timerText, (left / 60) + ":" + (left % 60).ToString("00"));
            // The last half-minute reads in warm red.
            timerText.color = left <= 30 ? new Color(0.96f, 0.55f, 0.45f) : new Color(0.98f, 0.96f, 0.93f);
        }
    }

    private void HideHud()
    {
        if (hud != null) UIToggleMotion.SetActive(hud, false, 0f, new Vector2(0f, 40f));
    }

    // ─── Orbs ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Jungle road points with room for a camel, reachable from <paramref name="start"/>, spread apart and
    /// not right where the camel already stands.
    /// </summary>
    private List<Vector3> PickSpots(Vector3 start)
    {
        var candidates = new List<Vector3>();
        GardenPathfinder pf = RideRoutes.Pathfinder;
        foreach (Vector3 p in RideRoutes.RoadPoints(true, 3f))
        {
            if (Vector3.Distance(p, start) < 8f) continue;
            if (!RideRoutes.IsClear(p, 1.2f, orbHeight + 1f)) continue;
            candidates.Add(p);
        }

        List<Vector3> spread = RideRoutes.Spread(candidates, orbCount * 2, start);
        var picked = new List<Vector3>();
        foreach (Vector3 p in spread)
        {
            if (picked.Count >= orbCount) break;
            bool tooClose = false;
            foreach (Vector3 q in picked)
            {
                if (Vector3.Distance(p, q) < minSpacing) { tooClose = true; break; }
            }
            if (tooClose) continue;
            if (pf != null && pf.FindPath(start, p) == null) continue;
            picked.Add(p);
        }
        return picked;
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
