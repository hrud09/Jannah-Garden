using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks the player along the garden's paths to a destination, on their behalf.
///
/// <para>One walker serves every "take me there" button. Starting a new walk replaces whatever walk
/// was running, so pressing Move to Unlocked Area mid-way back to base simply turns the player around
/// — there is never a second autopilot to fight the first.</para>
///
/// <para><b>The player always wins.</b> The walk ends the moment they push the joystick (or keys or
/// gamepad), drag to look around, or enter Inspector Mode. It steers through
/// <see cref="IdyllicFantasyNature.PlayerMovement.SetAutoMove"/>, so gravity, auto-climb, the
/// unstick hop and footsteps all behave exactly as they do under the player's own hand.</para>
///
/// <para>Lives on the player. The UI buttons find or add it through <see cref="GetOrCreate"/>, so it
/// needs no scene wiring.</para>
/// </summary>
public class PlayerAutoWalker : MonoBehaviour
{
    public enum Destination
    {
        None,
        Base,
        UnlockedArea,
    }

    public enum EndReason
    {
        /// <summary>Reached the destination.</summary>
        Arrived,
        /// <summary>The player took over, or code called <see cref="Cancel"/>.</summary>
        Cancelled,
        /// <summary>No route exists — off the terrain, cut off by water, or no area to go to.</summary>
        NoPath,
        /// <summary>A route existed but something on it would not let the player through.</summary>
        Blocked,
    }

    [Header("Walking")]
    [Tooltip("Walk at run speed. Off walks at the player's normal pace.")]
    public bool run = true;

    [Tooltip("How close (metres) to a waypoint counts as reaching it before turning for the next.")]
    [Range(0.2f, 3f)]
    public float waypointRadius = 0.9f;

    [Tooltip("How close (metres) to the final point counts as arrived.")]
    [Range(0.1f, 2f)]
    public float arriveRadius = 0.4f;

    [Tooltip("How fast the view turns to face the way it is walking, in degrees per second.")]
    [Range(45f, 1080f)]
    public float turnSpeed = 300f;

    [Tooltip("Ease the camera's pitch toward this while walking, so the player sees where they are " +
             "going instead of their feet or the sky. Degrees; positive looks down.")]
    [Range(-30f, 45f)]
    public float walkingPitch = 8f;

    [Header("Handing Control Back")]
    [Tooltip("End the walk when the player drags to look around. Off lets them look while the " +
             "autopilot keeps walking — but the autopilot still steers the view, so the two will fight.")]
    public bool cancelOnLookInput = true;

    [Header("Getting Stuck")]
    [Tooltip("Seconds without getting closer to the next waypoint before the route is recalculated.")]
    [Range(1f, 10f)]
    public float stuckTimeout = 3.5f;

    [Tooltip("How many times to recalculate the route after getting stuck before giving up.")]
    [Range(0, 5)]
    public int maxRepaths = 2;

    [Header("Route Finding")]
    [Tooltip("How many times more a step on grass or sand costs than a step on road. Higher keeps the " +
             "walk on the paths more stubbornly; 1 ignores the paths and goes straight.")]
    [Range(1f, 30f)]
    public float offRoadCost = 8f;

    [Tooltip("Steepest ground, in degrees, routes may cross. Keep at or under the CharacterController's slope limit.")]
    [Range(10f, 80f)]
    public float maxWalkableSlope = 40f;

    [Tooltip("Ground inside the lake at or below this height is water and never walked through. " +
             "Matches the area bake: lake surface (3.94) plus its shore margin (0.45).")]
    public float waterLine = 4.39f;

    [Tooltip("The garden terrain. Auto-detected (LandTerrain, else the active terrain) if empty.")]
    public Terrain terrain;

    [Tooltip("The lake surface renderer. Auto-detected under Environment/Water if empty.")]
    public Renderer lakeSurface;

    /// <summary>Raised when a walk begins, with where it is headed.</summary>
    public event Action<Destination> WalkStarted;

    /// <summary>Raised when a walk ends, for any reason, with where it had been headed.</summary>
    public event Action<Destination, EndReason> WalkEnded;

    /// <summary>Where the current walk is headed, or <see cref="Destination.None"/>.</summary>
    public Destination Current { get; private set; }

    public bool IsWalking => Current != Destination.None;

    /// <summary>The area the current or last unlocked-area walk headed for.</summary>
    public GardenAreaDefinition TargetArea { get; private set; }

    private IdyllicFantasyNature.PlayerMovement _movement;
    private IdyllicFantasyNature.CameraMovement _camera;
    private GardenPathfinder _pathfinder;

    private bool _hasBase;
    private Vector3 _basePosition;
    private float _baseYaw;
    private float _basePitch;

    private List<Vector3> _waypoints;
    private int _waypointIndex;
    private Vector3 _goal;
    private bool _hasFinalFacing;
    private float _finalYaw;
    private float _finalPitch;
    private bool _turningInPlace;

    private float _bestDistance;
    private float _stuckTimer;
    private int _repaths;

    // ═══════════════════════════════════════════════════════════════════════════
    //  SETUP
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The walker on <paramref name="movement"/>'s object, adding one if there is none yet.</summary>
    public static PlayerAutoWalker GetOrCreate(IdyllicFantasyNature.PlayerMovement movement)
    {
        if (movement == null) return null;

        var walker = movement.GetComponent<PlayerAutoWalker>();
        return walker != null ? walker : movement.gameObject.AddComponent<PlayerAutoWalker>();
    }

    private void Awake()
    {
        _movement = GetComponent<IdyllicFantasyNature.PlayerMovement>();
        _camera = GetComponentInChildren<IdyllicFantasyNature.CameraMovement>();
        if (_camera == null) _camera = FindObjectOfType<IdyllicFantasyNature.CameraMovement>();
    }

    private void Start()
    {
        // Wherever the player stands when the scene starts is home, unless someone says otherwise.
        if (!_hasBase) CaptureBaseFromCurrent();

        if (_movement != null) _movement.OnInspectorModeChanged += HandleInspectorModeChanged;

        // Build the grid now, under the scene load, rather than as a hitch on the first button press.
        EnsurePathfinder();
    }

    private void OnDestroy()
    {
        if (_movement != null) _movement.OnInspectorModeChanged -= HandleInspectorModeChanged;
    }

    private void OnDisable()
    {
        if (IsWalking) End(EndReason.Cancelled);
    }

    /// <summary>Sets where Return to Base goes, and which way the player faces on arrival.</summary>
    public void SetBase(Vector3 position, float yaw, float pitch)
    {
        _hasBase = true;
        _basePosition = position;
        _baseYaw = yaw;
        _basePitch = pitch;
    }

    /// <summary>Sets base to <paramref name="marker"/>'s position and facing.</summary>
    public void SetBase(Transform marker)
    {
        if (marker == null) return;
        SetBase(marker.position, marker.eulerAngles.y, 0f);
    }

    /// <summary>Makes wherever the player is standing and looking right now their base.</summary>
    public void CaptureBaseFromCurrent()
    {
        // Read from the transforms, not CameraMovement's cached angles: this can run in Start before
        // CameraMovement's own Start has initialised them from the very same transforms.
        float pitch = _camera != null ? _camera.transform.localEulerAngles.x : 0f;
        if (pitch > 180f) pitch -= 360f;

        SetBase(transform.position, transform.eulerAngles.y, pitch);
    }

    private bool EnsurePathfinder()
    {
        if (_pathfinder != null) return true;

        if (terrain == null)
        {
            GameObject named = GameObject.Find("LandTerrain");
            terrain = named != null ? named.GetComponent<Terrain>() : null;
            if (terrain == null) terrain = Terrain.activeTerrain;
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("[PlayerAutoWalker] No terrain found — cannot plan routes.");
            return false;
        }

        _pathfinder = new GardenPathfinder(terrain, ResolveLakeFootprint(), waterLine, maxWalkableSlope, offRoadCost);
        return true;
    }

    /// <summary>The lake's XZ footprint. Same lookup as the area bake, so both agree on what is water.</summary>
    private Rect ResolveLakeFootprint()
    {
        if (lakeSurface == null)
        {
            GameObject env = GameObject.Find("Environment");
            Transform water = env != null ? env.transform.Find("Water") : null;

            if (water != null)
            {
                foreach (Renderer r in water.GetComponentsInChildren<Renderer>(true))
                {
                    // The lake surface is the one big flat plane in there; the gazebo floors are not.
                    if (r.bounds.size.x < 40f || r.bounds.size.y > 1f) continue;
                    lakeSurface = r;
                    break;
                }
            }
        }

        if (lakeSurface == null) return Rect.zero;

        Bounds b = lakeSurface.bounds;
        return new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STARTING A WALK
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks the player back to where they started, then turns them to face the way they started.
    /// Returns false when there is no way there; <see cref="WalkEnded"/> carries the reason either way.
    /// </summary>
    public bool WalkToBase()
    {
        if (!PrepareForWalk(Destination.Base)) return false;

        List<Vector3> route = _pathfinder.FindPath(transform.position, _basePosition);
        if (route == null) return Fail(Destination.Base, EndReason.NoPath);

        TargetArea = null;
        Begin(Destination.Base, route, true, _baseYaw, _basePitch);
        return true;
    }

    /// <summary>
    /// Walks the player to the unlocked area nearest by road — other than the one they are already
    /// standing in. Ends at the heart of that area (the deepest point inside it, where its number is
    /// drawn on the minimap), so the player arrives with room to build around them.
    /// </summary>
    public bool WalkToNearestUnlockedArea()
    {
        if (!PrepareForWalk(Destination.UnlockedArea)) return false;

        GardenAreaManager areas = GardenAreaManager.Instance;
        if (areas == null || !areas.IsReady) return Fail(Destination.UnlockedArea, EndReason.NoPath);

        GardenAreaDefinition here = areas.AreaAt(transform.position);

        var candidates = new List<GardenAreaDefinition>();
        var anchors = new List<Vector3>();

        foreach (GardenAreaDefinition area in areas.map.areas)
        {
            if (area == null || area == here || !areas.IsUnlocked(area)) continue;

            candidates.Add(area);
            anchors.Add(new Vector3(area.labelAnchor.x, transform.position.y, area.labelAnchor.y));
        }

        if (candidates.Count == 0) return Fail(Destination.UnlockedArea, EndReason.NoPath);

        List<Vector3> route = _pathfinder.FindPathToNearest(transform.position, anchors, out int reached);
        if (route == null) return Fail(Destination.UnlockedArea, EndReason.NoPath);

        TargetArea = candidates[reached];
        Begin(Destination.UnlockedArea, route, false, 0f, 0f);
        return true;
    }

    /// <summary>
    /// True when the player is in an unlocked area and no <em>other</em> area is unlocked — the one
    /// case where Move to Unlocked Area has nowhere to go because they are already there.
    /// </summary>
    public bool IsAlreadyInOnlyUnlockedArea()
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        if (areas == null || !areas.IsReady) return false;

        GardenAreaDefinition here = areas.AreaAt(transform.position);
        if (here == null || !areas.IsUnlocked(here)) return false;

        foreach (GardenAreaDefinition area in areas.map.areas)
        {
            if (area != null && area != here && areas.IsUnlocked(area)) return false;
        }

        return true;
    }

    /// <summary>Stops the current walk, if any, and hands control back.</summary>
    public void Cancel()
    {
        if (IsWalking) End(EndReason.Cancelled);
    }

    private bool PrepareForWalk(Destination destination)
    {
        // A new walk replaces the old one; the old one's listeners hear it was cancelled.
        Cancel();

        if (_movement == null || !EnsurePathfinder()) return Fail(destination, EndReason.NoPath);

        // Routes follow the ground, so the walk happens on foot.
        if (_movement.IsInspectorMode) _movement.ExitInspectorMode();

        return true;
    }

    private bool Fail(Destination destination, EndReason reason)
    {
        WalkEnded?.Invoke(destination, reason);
        return false;
    }

    private void Begin(Destination destination, List<Vector3> route, bool faceAtEnd, float yaw, float pitch)
    {
        Current = destination;
        _waypoints = route;
        _waypointIndex = route.Count > 1 ? 1 : 0; // The first point is the cell the player stands in.
        _goal = route[route.Count - 1];
        _hasFinalFacing = faceAtEnd;
        _finalYaw = yaw;
        _finalPitch = pitch;
        _turningInPlace = false;
        _repaths = 0;
        ResetStuck();

        WalkStarted?.Invoke(destination);
    }

    private void End(EndReason reason)
    {
        Destination ended = Current;

        Current = Destination.None;
        _waypoints = null;
        _turningInPlace = false;
        if (_movement != null) _movement.ClearAutoMove();

        WalkEnded?.Invoke(ended, reason);
    }

    private void HandleInspectorModeChanged(bool entering)
    {
        if (entering) Cancel();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  WALKING
    // ═══════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!IsWalking) return;

        if (_movement == null || _movement.IsInspectorMode || _movement.HasManualMoveInput ||
            (cancelOnLookInput && _camera != null && _camera.HasLookInput))
        {
            End(EndReason.Cancelled);
            return;
        }

        if (_turningInPlace)
        {
            UpdateFinalTurn();
            return;
        }

        // A climb owns the transform; keep pushing the same way so it carries on after landing.
        if (_movement.IsClimbing) return;

        Vector3 position = transform.position;
        Vector3 target = _waypoints[_waypointIndex];
        bool isLast = _waypointIndex == _waypoints.Count - 1;

        Vector3 toTarget = target - position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance <= (isLast ? arriveRadius : waypointRadius))
        {
            if (!isLast)
            {
                _waypointIndex++;
                ResetStuck();
                return;
            }

            _movement.ClearAutoMove();

            if (_hasFinalFacing)
            {
                _turningInPlace = true;
                return;
            }

            End(EndReason.Arrived);
            return;
        }

        // Ease off over the last stretch so the player settles on the spot instead of overshooting it.
        float speed = isLast ? Mathf.Clamp(distance / 1.5f, 0.35f, 1f) : 1f;
        Vector3 direction = toTarget / distance;
        _movement.SetAutoMove(direction * speed, run && !(isLast && distance < 2f));

        Face(Quaternion.LookRotation(direction).eulerAngles.y, walkingPitch);
        TrackProgress(distance);
    }

    private void UpdateFinalTurn()
    {
        Face(_finalYaw, _finalPitch);

        bool yawDone = Mathf.Abs(Mathf.DeltaAngle(CurrentYaw, _finalYaw)) < 0.5f;
        bool pitchDone = _camera == null || Mathf.Abs(_camera.Pitch - _finalPitch) < 0.5f;

        if (yawDone && pitchDone) End(EndReason.Arrived);
    }

    private float CurrentYaw => _camera != null ? _camera.Yaw : transform.eulerAngles.y;

    /// <summary>Turns the view toward <paramref name="yaw"/>/<paramref name="pitch"/> at <see cref="turnSpeed"/>.</summary>
    private void Face(float yaw, float pitch)
    {
        float step = turnSpeed * Time.deltaTime;
        float newYaw = Mathf.MoveTowardsAngle(CurrentYaw, yaw, step);

        if (_camera != null)
        {
            // CameraMovement writes the controller's rotation every frame, so steer through it.
            float newPitch = Mathf.MoveTowards(_camera.Pitch, pitch, step * 0.5f);
            _camera.SetLookAngles(newYaw, newPitch);
        }
        else
        {
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STUCK HANDLING
    // ═══════════════════════════════════════════════════════════════════════════

    private void ResetStuck()
    {
        _bestDistance = float.MaxValue;
        _stuckTimer = 0f;
    }

    /// <summary>
    /// Re-plans from where the player actually is when they stop getting closer — someone placed an
    /// item on the path, or a fence turned up. PlayerMovement's own unstick hop gets the first go;
    /// this is for when hopping is not enough.
    /// </summary>
    private void TrackProgress(float distance)
    {
        if (distance < _bestDistance - 0.25f)
        {
            _bestDistance = distance;
            _stuckTimer = 0f;
            return;
        }

        _stuckTimer += Time.deltaTime;
        if (_stuckTimer < stuckTimeout) return;

        if (_repaths >= maxRepaths)
        {
            End(EndReason.Blocked);
            return;
        }

        _repaths++;

        List<Vector3> route = _pathfinder.FindPath(transform.position, _goal);
        if (route == null)
        {
            End(EndReason.Blocked);
            return;
        }

        _waypoints = route;
        _waypointIndex = route.Count > 1 ? 1 : 0;
        ResetStuck();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_waypoints == null) return;

        Gizmos.color = Color.cyan;
        for (int i = 1; i < _waypoints.Count; i++)
        {
            Gizmos.DrawLine(_waypoints[i - 1] + Vector3.up * 0.3f, _waypoints[i] + Vector3.up * 0.3f);
        }

        if (_hasBase)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_basePosition, 0.5f);
        }
    }
#endif
}
