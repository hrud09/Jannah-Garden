using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Auto Travel: a hands-free tour on the animal the player is riding, unlocked by a rewarded ad.
///
/// The route strings together spots spread along the garden's painted roads and then the jungle path,
/// joined by the same road-preferring routes the autopilot walks, and loops it until the time is up
/// (five minutes by default). A camel walks it; a pegasus takes off and flies the same line, holding a
/// cruising height above the ground ahead, and lands when the tour ends.
///
/// It steers through <see cref="AnimalRider.Driver"/>, feeding the same stick, run and up/down controls
/// a player would, so speed limits, obstacle checks, gaits and flight animation all behave exactly as
/// in a normal ride. The player can still look around; the view turns with the animal.
/// </summary>
[DisallowMultipleComponent]
public class RideAutoTravel : MonoBehaviour, IRideDriver
{
    [Tooltip("How long a tour lasts (seconds).")]
    public float duration = 300f;

    [Tooltip("Spots picked along the garden's roads per loop.")]
    public int gardenStops = 6;

    [Tooltip("Spots picked along the jungle path per loop.")]
    public int jungleStops = 5;

    [Tooltip("Stick pushed this far forward on the ground (0.9 = full walking pace).")]
    [Range(0.2f, 1f)]
    public float groundPace = 0.85f;

    [Tooltip("Stick pushed this far forward in the air (fraction of cruising airspeed).")]
    [Range(0.2f, 1f)]
    public float airPace = 0.75f;

    [Tooltip("Height kept above the ground ahead while flying (metres).")]
    public float cruiseAltitude = 12f;

    [Tooltip("In the air, keep one route point every this many metres (the road's small bends are smoothed out).")]
    public float flightPointSpacing = 10f;

    [Tooltip("How far ahead along the route to aim (metres) on the ground / in the air.")]
    public float groundLookAhead = 4f;
    public float airLookAhead = 12f;

    [Tooltip("Reaching no new route point for this long (seconds) counts as stuck: skip ahead along the route.")]
    public float stuckTime = 6f;

    [Tooltip("How far ahead (metres) a stuck animal skips its target.")]
    public float stuckSkip = 8f;

    /// <summary>True while a tour is running.</summary>
    public bool IsTravelling => _rider != null && _rider.Driver == (IRideDriver)this;

    /// <summary>Seconds left on the tour.</summary>
    public float Remaining => IsTravelling ? Mathf.Max(0f, _endTime - Time.time) : 0f;

    /// <summary>The route point being headed for (for debugging and gizmos).</summary>
    public Vector3 CurrentWaypoint => _route != null && _route.Count > 0 ? _route[_index] : Vector3.zero;

    /// <summary>Route points in one loop, and which one is next.</summary>
    public int RouteLength => _route != null ? _route.Count : 0;
    public int RouteIndex => _index;

    private AnimalRider _rider;
    private List<Vector3> _route;
    private int _index;
    private float _endTime;
    private bool _landing;
    private float _sinceProgress;

    public static RideAutoTravel GetOrCreate(AnimalRider rider)
    {
        if (rider == null) return null;
        var travel = rider.GetComponent<RideAutoTravel>();
        return travel != null ? travel : rider.gameObject.AddComponent<RideAutoTravel>();
    }

    /// <summary>Plans the tour from where the mount stands and takes the reins. False when there is no route.</summary>
    public bool Begin(AnimalRider rider)
    {
        if (rider == null || !rider.IsRiding || rider.IsTransitioning) return false;

        Vector3 start = rider.Mount.transform.position;
        List<Vector3> loop = PlanLoop(start);
        if (loop != null && rider.Mount.canFly) loop = SmoothForFlight(loop);
        if (loop == null || loop.Count < 4) return false;

        _rider = rider;
        _route = loop;
        _index = 0;
        _endTime = Time.time + duration;
        _landing = false;
        _sinceProgress = 0f;

        rider.Driver = this;
        return true;
    }

    /// <summary>Ends the tour now and hands the mount back (a pegasus is left hovering where it is).</summary>
    public void Stop()
    {
        if (_rider != null && _rider.Driver == (IRideDriver)this) _rider.Driver = null;
        _rider = null;
        _route = null;
    }

    /// <summary>
    /// One loop: the garden's road spots (nearest first) then the jungle's, then back to the first garden
    /// spot so the loop closes. Starting in the jungle runs the jungle half first.
    /// </summary>
    private List<Vector3> PlanLoop(Vector3 start)
    {
        List<Vector3> garden = RideRoutes.OrderAsLoop(
            RideRoutes.Spread(RideRoutes.RoadPoints(false, 3f), gardenStops, start), start);
        List<Vector3> jungle = RideRoutes.Spread(RideRoutes.RoadPoints(true, 3f), jungleStops,
            garden.Count > 0 ? garden[garden.Count - 1] : start);
        jungle = RideRoutes.OrderAsLoop(jungle, garden.Count > 0 ? garden[garden.Count - 1] : start);

        var stops = new List<Vector3>();
        if (RideRoutes.InJungle(start))
        {
            stops.AddRange(jungle);
            stops.AddRange(garden);
            if (jungle.Count > 0) stops.Add(jungle[0]);
        }
        else
        {
            stops.AddRange(garden);
            stops.AddRange(jungle);
            if (garden.Count > 0) stops.Add(garden[0]);
        }

        if (stops.Count == 0) return null;
        return RideRoutes.RouteThrough(start, stops);
    }

    /// <summary>
    /// A flier doesn't need every kink of the road under it: keep a point every
    /// <see cref="flightPointSpacing"/> metres so it sweeps along the route instead of weaving, and pull
    /// points near the boundary walls inward, where flight would be held back short of them.
    /// </summary>
    private List<Vector3> SmoothForFlight(List<Vector3> route)
    {
        var smooth = new List<Vector3> { route[0] };
        for (int i = 1; i < route.Count; i++)
        {
            bool last = i == route.Count - 1;
            if (!last && FlatDistance(route[i], smooth[smooth.Count - 1]) < flightPointSpacing) continue;

            Vector3 p = route[i];
            for (int n = 0; n < 12 && !RideRoutes.InPlayArea(p, 12f); n++)
            {
                // Step toward the middle of the map until clear of the walls.
                Vector3 toCentre = new Vector3(100f, p.y, 100f) - p;
                toCentre.y = 0f;
                p += toCentre.normalized * 2f;
            }
            smooth.Add(p);
        }
        return smooth;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DRIVING
    // ═══════════════════════════════════════════════════════════════════════════

    public bool Drive(AnimalRider rider, out Vector2 move, out bool run, out float vertical)
    {
        move = Vector2.zero;
        run = false;
        vertical = 0f;
        if (rider != _rider || _route == null || rider.Mount == null) return false;

        Transform t = rider.Mount.transform;
        Vector3 pos = t.position;
        bool flyer = rider.Mount.canFly;

        // ── Time's up: a camel simply stops; a pegasus comes down to land first ──
        if (Time.time >= _endTime || _landing)
        {
            if (flyer && rider.IsFlying)
            {
                _landing = true;
                vertical = -1f;
                move.y = 0.2f; // A gentle glide down rather than a vertical drop.
                return true;
            }

            Finish(true);
            return false;
        }

        // ── Follow the route ──
        float lookAhead = flyer ? airLookAhead : groundLookAhead;
        AdvanceAlongRoute(pos, flyer ? 6f : 2.2f);
        Vector3 aim = PointAhead(pos, lookAhead);

        Vector3 to = aim - pos;
        to.y = 0f;
        float off = to.sqrMagnitude > 0.01f ? Mathf.DeltaAngle(t.eulerAngles.y, Quaternion.LookRotation(to).eulerAngles.y) : 0f;

        move.x = Mathf.Clamp(off / 35f, -1f, 1f);
        float pace = flyer ? airPace : groundPace;
        // Ease off for sharp corners so the animal turns on the spot instead of swinging wide.
        move.y = pace * Mathf.Lerp(1f, 0.15f, Mathf.InverseLerp(30f, 90f, Mathf.Abs(off)));

        if (flyer)
        {
            // Hold the cruising height above the highest ground between here and the aim point.
            float ground = Mathf.Max(RideRoutes.GroundHeight(pos), RideRoutes.GroundHeight(aim),
                                     RideRoutes.GroundHeight(Vector3.Lerp(pos, aim, 0.5f)));
            float want = ground + cruiseAltitude;
            vertical = Mathf.Clamp((want - pos.y) / 4f, -0.6f, 1f);
            // Not yet up: climb before setting off, so it doesn't gallop along the road.
            if (!rider.IsFlying || pos.y - RideRoutes.GroundHeight(pos) < cruiseAltitude * 0.4f)
            {
                vertical = 1f;
                move.y *= 0.4f;
            }
        }

        TrackProgress(pos);
        return true;
    }

    /// <summary>Moves the route index past points already reached, looping back to the start at the end.</summary>
    private void AdvanceAlongRoute(Vector3 pos, float reach)
    {
        int guard = 0;
        while (guard++ < 64 && FlatDistance(pos, _route[_index]) < reach)
        {
            _index++;
            if (_index >= _route.Count) _index = 1; // The loop closes at its first stop.
            _sinceProgress = 0f;
        }
    }

    /// <summary>The route point at least <paramref name="distance"/> ahead of the current index.</summary>
    private Vector3 PointAhead(Vector3 pos, float distance)
    {
        int i = _index;
        for (int n = 0; n < 32; n++)
        {
            if (FlatDistance(pos, _route[i]) >= distance) return _route[i];
            int next = i + 1 >= _route.Count ? 1 : i + 1;
            if (next == _index) break;
            i = next;
        }
        return _route[i];
    }

    /// <summary>
    /// No new route point reached for a while — blocked by a rock, or circling a point on ground it can't
    /// quite stand on? Aim for the first point a few metres further along instead. Measured by route
    /// progress rather than movement, because circling keeps the animal moving without getting anywhere.
    /// </summary>
    private void TrackProgress(Vector3 pos)
    {
        _sinceProgress += Time.deltaTime;
        if (_sinceProgress < stuckTime) return;
        _sinceProgress = 0f;

        for (int n = 0; n < _route.Count; n++)
        {
            _index++;
            if (_index >= _route.Count) _index = 1;
            if (FlatDistance(pos, _route[_index]) >= stuckSkip) break;
        }
    }

    private void Finish(bool completed)
    {
        _rider = null;
        _route = null;
        if (completed) RideFeatures.Toast(RideFeatures.Loc("auto_travel.finished"), ToastTone.Success);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
