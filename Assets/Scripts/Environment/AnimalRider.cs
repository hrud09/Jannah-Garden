using System;
using UnityEngine;
using IdyllicFantasyNature;

/// <summary>
/// Lets the player climb onto one of the herd animals (the camels) and ride it around the garden.
///
/// <para><b>Getting on.</b> <see cref="FindMountable"/> picks the nearest rideable animal in front of the
/// player; <see cref="PlayersInteractionManager"/> shows the Ride button for it and calls
/// <see cref="TryMount"/>. The player is lifted onto the seat in a short arc.</para>
///
/// <para><b>Riding.</b> The usual movement controls steer the animal instead of the player: forward and
/// back set the pace (shift, or the joystick pushed all the way, breaks into a run), left and right turn
/// it and the view with it. Riding forward also eases the animal toward wherever the player is looking,
/// so dragging to look is a second way to steer, as it is on foot. The animal keeps to dry, not-too-steep
/// ground and stops at anything solid in front of its chest; it has no collider of its own.</para>
///
/// <para><b>Getting off.</b> <see cref="Dismount"/> sets the player down beside the animal on whichever
/// side has room, and gives the animal back its own idling. Entering Inspector Mode or starting an
/// autopilot walk also gets the player off first.</para>
///
/// <para>Lives on the player next to <see cref="PlayerMovement"/>, which it suspends while riding. Added
/// on demand through <see cref="GetOrCreate"/>, so it needs no scene wiring.</para>
/// </summary>
[DisallowMultipleComponent]
public class AnimalRider : MonoBehaviour
{
    [Header("Getting On")]
    [Tooltip("How close (metres, to the animal's seat, measured flat) the player must be to climb on.")]
    [Range(1f, 10f)]
    public float mountRange = 4f;

    [Tooltip("How far either side of straight ahead (degrees) an animal can be and still be offered.")]
    [Range(10f, 180f)]
    public float mountViewAngle = 75f;

    [Tooltip("How long climbing on or off takes (seconds).")]
    [Range(0.1f, 2f)]
    public float mountDuration = 0.55f;

    [Tooltip("How high the climb arcs above the straight line between ground and seat (metres).")]
    [Range(0f, 2f)]
    public float mountArcHeight = 0.5f;

    [Header("Sitting")]
    [Tooltip("Eye height above the seat while riding (metres).")]
    [Range(0.3f, 1.5f)]
    public float eyeHeightAboveSeat = 0.8f;

    [Header("Controls")]
    [Tooltip("How quickly the animal speeds up and slows down (m/s per second).")]
    [Range(0.5f, 10f)]
    public float acceleration = 3f;

    [Tooltip("While riding forward, how fast (degrees/second) the animal turns toward where the player " +
             "is looking. 0 turns this off and leaves steering to left/right alone.")]
    [Range(0f, 180f)]
    public float lookSteering = 45f;

    [Tooltip("Looking further than this (degrees) from the animal's heading counts as looking around, " +
             "not steering, so glancing behind never turns the animal round.")]
    [Range(10f, 120f)]
    public float lookSteeringMaxAngle = 70f;

    [Header("Where It Can Go")]
    [Tooltip("Ground inside the lake at or below this height is water (lake surface 3.94 plus its shore " +
             "margin, as the autopilot uses). Low ground outside the lake is dry garden, not water.")]
    public float waterLevel = 4.39f;

    [Tooltip("The lake surface renderer, whose footprint bounds the water. Auto-detected under " +
             "Environment/Water if empty.")]
    public Renderer lakeSurface;

    [Tooltip("Steepest ground the animal will walk onto, in degrees.")]
    [Range(10f, 60f)]
    public float maxSlope = 42f;

    [Tooltip("When the way straight ahead is blocked, how far to either side (degrees) the animal looks for " +
             "a way round before stopping. It slides along banks, fences and the shore instead of " +
             "freezing in place.")]
    [Range(0f, 90f)]
    public float slideSearchAngle = 70f;

    [Tooltip("Radius of the probe swept ahead of the animal's chest to find obstacles (metres).")]
    [Range(0.1f, 1.5f)]
    public float obstacleProbeRadius = 0.5f;

    [Tooltip("Height of that probe above the ground, as a fraction of the seat height.")]
    [Range(0.2f, 1f)]
    public float obstacleProbeHeight = 0.6f;

    [Tooltip("Layers that block the animal. Terrain is ignored here; ground is handled separately.")]
    public LayerMask obstacleMask = ~0;

    [Header("Audio")]
    [Range(0f, 1f)]
    public float hoofVolume = 0.6f;

    /// <summary>Raised when the player gets on (true) and once they are fully off (false).</summary>
    public event Action<bool> RideStateChanged;

    /// <summary>The animal being ridden (also while climbing on or off), or null.</summary>
    public GrazingAnimal Mount { get; private set; }

    public bool IsRiding => Mount != null;

    /// <summary>True while climbing on or off; the Dismount button waits for this.</summary>
    public bool IsTransitioning => _phase == Phase.Mounting || _phase == Phase.Dismounting;

    private enum Phase { None, Mounting, Riding, Dismounting }

    private PlayerMovement _movement;
    private CameraMovement _camera;
    private CharacterController _controller;
    private PlayerAutoWalker _walker;
    private Terrain _terrain;
    private AudioSource _hoofSource;

    private Phase _phase;
    private float _phaseTimer;
    private Vector3 _fromPosition;
    private Vector3 _toPosition;
    private float _cameraHeight; // Camera height above the player's pivot, captured on mounting.
    private float _speed;
    private float _lastStridePhase = -1f;

    private static readonly RaycastHit[] ProbeHits = new RaycastHit[16];

    // ═══════════════════════════════════════════════════════════════════════════
    //  SETUP
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The rider on <paramref name="movement"/>'s object, adding one if there is none yet.</summary>
    public static AnimalRider GetOrCreate(PlayerMovement movement)
    {
        if (movement == null) return null;

        var rider = movement.GetComponent<AnimalRider>();
        return rider != null ? rider : movement.gameObject.AddComponent<AnimalRider>();
    }

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        _controller = GetComponent<CharacterController>();
        _camera = GetComponentInChildren<CameraMovement>();
        if (_camera == null) _camera = FindFirstObjectByType<CameraMovement>();

        _hoofSource = gameObject.AddComponent<AudioSource>();
        _hoofSource.playOnAwake = false;
        _hoofSource.spatialBlend = 0f;
    }

    private void OnEnable()
    {
        if (_movement != null) _movement.OnInspectorModeChanged += HandleInspectorModeChanged;
    }

    private void OnDisable()
    {
        if (_movement != null) _movement.OnInspectorModeChanged -= HandleInspectorModeChanged;
        if (IsRiding) Release(placePlayer: true);
    }

    private void HandleInspectorModeChanged(bool entering)
    {
        // Inspector Mode lifts the player into the air and owns the controller from here; just let go.
        if (entering && IsRiding) Release(placePlayer: false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GETTING ON
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The rideable animal the player is best placed to climb onto: within <see cref="mountRange"/>, no
    /// more than <see cref="mountViewAngle"/> off the way they are facing, nearest first. Null while
    /// already riding, in Inspector Mode, mid-climb, or with nothing in reach.
    /// </summary>
    public GrazingAnimal FindMountable()
    {
        if (IsRiding || _movement == null) return null;
        if (_movement.IsInspectorMode || _movement.IsClimbing) return null;

        Vector3 here = transform.position;
        Vector3 facing = transform.forward;
        facing.y = 0f;
        facing.Normalize();

        GrazingAnimal best = null;
        float bestScore = float.MaxValue;

        foreach (GrazingAnimal animal in GrazingAnimal.Active)
        {
            if (animal == null || !animal.CanBeRidden) continue;

            Vector3 to = animal.SeatPosition - here;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > mountRange) continue;

            float angle = distance < 0.5f ? 0f : Vector3.Angle(facing, to / distance);
            if (angle > mountViewAngle) continue;

            // Mostly nearest, but an animal straight ahead beats a slightly closer one off to the side.
            float score = distance * (1f + angle / 90f);
            if (score < bestScore)
            {
                bestScore = score;
                best = animal;
            }
        }

        return best;
    }

    /// <summary>Climbs onto <paramref name="animal"/>. Returns false if that can't happen right now.</summary>
    public bool TryMount(GrazingAnimal animal)
    {
        if (animal == null || !animal.CanBeRidden || IsRiding || _movement == null) return false;
        if (_movement.IsInspectorMode) return false;

        if (_walker == null) _walker = GetComponent<PlayerAutoWalker>();
        if (_walker != null) _walker.Cancel();

        if (_terrain == null) _terrain = Terrain.activeTerrain;

        _cameraHeight = _camera != null ? _camera.transform.position.y - transform.position.y : 1.6f;

        Mount = animal;
        animal.BeginRide();
        _movement.SetMounted(true);

        _speed = 0f;
        _lastStridePhase = -1f;
        StartPhase(Phase.Mounting, transform.position);

        GameSfx.PlayAt(SoundEffect.ItemInteract, animal.transform);
        RideStateChanged?.Invoke(true);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GETTING OFF
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Climbs down beside the animal. <paramref name="instant"/> skips the climb, for callers that need
    /// the player on the ground this frame (the autopilot about to plan a route from there).
    /// </summary>
    public void Dismount(bool instant = false)
    {
        if (!IsRiding || _phase == Phase.Dismounting) return;

        _speed = 0f;
        Mount.SetRideGait(0f, 0f);

        Vector3 landing = FindLandingSpot();

        if (instant)
        {
            transform.position = landing;
            Release(placePlayer: false);
            return;
        }

        _fromPosition = transform.position;
        _toPosition = landing;
        _phase = Phase.Dismounting;
        _phaseTimer = 0f;
    }

    /// <summary>
    /// Lets go of the animal and hands the player back to <see cref="PlayerMovement"/>.
    /// <paramref name="placePlayer"/> drops them beside it first; false leaves them wherever they are.
    /// </summary>
    private void Release(bool placePlayer)
    {
        GrazingAnimal animal = Mount;
        if (animal == null) return;

        if (placePlayer) transform.position = FindLandingSpot();

        Mount = null;
        _phase = Phase.None;
        _speed = 0f;

        animal.EndRide();
        if (_movement != null) _movement.SetMounted(false);

        RideStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Where the player's pivot goes on climbing down: left of the animal, else right, behind, ahead —
    /// the first that is dry ground with room for the player's capsule. Falls back to the left side,
    /// and gravity sorts out the rest.
    /// </summary>
    private Vector3 FindLandingSpot()
    {
        Transform t = Mount.transform;
        float side = Mount.BodyWidth * 0.5f + PlayerRadius + 0.5f;
        float end = Mount.BodyLength * 0.5f + PlayerRadius + 0.4f;
        Vector3 centre = Mount.SeatPosition;
        centre.y = t.position.y;

        Vector3[] offsets = { -t.right * side, t.right * side, -t.forward * end, t.forward * end };

        Vector3 fallback = Vector3.zero;
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector3 feet = centre + offsets[i];
            feet.y = GroundHeight(feet);

            Vector3 pivot = PivotForFeet(feet);
            if (i == 0) fallback = pivot;

            if (IsWalkable(feet, ignoreSlope: true) && HasRoomFor(pivot)) return pivot;
        }

        return fallback;
    }

    private float PlayerRadius => _controller != null ? _controller.radius : 0.4f;

    /// <summary>The player's transform position that stands their capsule's feet on <paramref name="feet"/>.</summary>
    private Vector3 PivotForFeet(Vector3 feet)
    {
        if (_controller == null) return feet + Vector3.up * 0.05f;

        float footOffset = _controller.center.y - _controller.height * 0.5f;
        return feet + Vector3.up * (0.05f + _controller.skinWidth - footOffset);
    }

    private bool HasRoomFor(Vector3 pivot)
    {
        if (_controller == null) return true;

        float radius = _controller.radius * 0.95f;
        Vector3 centre = pivot + _controller.center;
        float half = Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
        return !Physics.CheckCapsule(centre - Vector3.up * half, centre + Vector3.up * half, radius,
                                     obstacleMask, QueryTriggerInteraction.Ignore);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  RIDING
    // ═══════════════════════════════════════════════════════════════════════════

    private void StartPhase(Phase phase, Vector3 from)
    {
        _phase = phase;
        _phaseTimer = 0f;
        _fromPosition = from;
    }

    private void Update()
    {
        if (!IsRiding) return;

        // The animal was switched off or taken away under us (scene change, optimiser, pooling).
        if (!Mount.isActiveAndEnabled || !Mount.IsRidden)
        {
            Mount = null;
            _phase = Phase.None;
            if (_movement != null) _movement.SetMounted(false);
            RideStateChanged?.Invoke(false);
            return;
        }

        if (_phase == Phase.Riding) Steer();
    }

    /// <summary>Turns and moves the animal from the movement controls.</summary>
    private void Steer()
    {
        float dt = Time.deltaTime;
        Vector2 input = _movement.ReadMoveInput(out bool fromJoystick);
        GrazingAnimal animal = Mount;
        Transform t = animal.transform;

        // ── Pace ──
        float target;
        if (input.y > 0.05f)
        {
            bool run = _movement.IsRunPressed() || (fromJoystick && input.magnitude > 0.95f);
            target = run ? animal.rideRunSpeed : animal.rideWalkSpeed * Mathf.Clamp01(input.y / 0.9f);
        }
        else if (input.y < -0.3f)
        {
            target = -animal.rideReverseSpeed;
        }
        else
        {
            target = 0f;
        }

        _speed = Mathf.MoveTowards(_speed, target, acceleration * dt);

        // ── Turning ──
        // Left/right turns the animal and the view together, like turning a head on its neck. Riding
        // forward with the stick centred also eases the animal toward where the player is looking.
        float manualTurn = input.x * animal.rideTurnSpeed * dt;
        if (_speed < -0.05f) manualTurn = -manualTurn; // Reversing steers like reversing a cart.

        float lookTurn = 0f;
        if (lookSteering > 0f && _camera != null && _speed > 0.3f && Mathf.Abs(input.x) < 0.2f)
        {
            float off = Mathf.DeltaAngle(t.eulerAngles.y, _camera.Yaw);
            if (Mathf.Abs(off) < lookSteeringMaxAngle)
            {
                lookTurn = Mathf.Clamp(off, -lookSteering * dt, lookSteering * dt);
            }
        }

        if (manualTurn != 0f || lookTurn != 0f)
        {
            t.rotation = Quaternion.Euler(0f, t.eulerAngles.y + manualTurn + lookTurn, 0f);
        }

        if (manualTurn != 0f && _camera != null)
        {
            _camera.SetLookAngles(_camera.Yaw + manualTurn, _camera.Pitch);
        }

        // ── Moving ──
        if (Mathf.Abs(_speed) > 0.01f)
        {
            Vector3 direction = _speed > 0f ? t.forward : -t.forward;
            float step = Mathf.Abs(_speed) * dt;

            if (TryStep(direction, step, out Vector3 next))
            {
                t.position = next;
            }
            else if (TrySlide(direction, step, input.x, out next, out float slideAngle))
            {
                // Skirt the obstacle: move along the free direction and turn to follow it, so pushing
                // forward into a bank or fence carries on along it rather than freezing.
                t.position = next;
                float follow = Mathf.Clamp(slideAngle, -animal.rideTurnSpeed * dt, animal.rideTurnSpeed * dt);
                t.rotation = Quaternion.Euler(0f, t.eulerAngles.y + follow, 0f);

                // The view turns with it, as for a steered turn, so the rider keeps facing the way they go.
                if (_camera != null) _camera.SetLookAngles(_camera.Yaw + follow, _camera.Pitch);
                _speed = Mathf.Min(Mathf.Abs(_speed), animal.rideWalkSpeed) * Mathf.Sign(_speed);
            }
            else
            {
                _speed = 0f;
            }
        }

        animal.SetRideGait(_speed, input.x);
        UpdateHoofFalls();
    }

    /// <summary>Where one step of <paramref name="step"/> metres along <paramref name="direction"/> lands, if it can be taken.</summary>
    private bool TryStep(Vector3 direction, float step, out Vector3 next)
    {
        Vector3 from = Mount.transform.position;
        next = from + direction * step;
        next.y = GroundHeight(next);
        return IsWalkable(next, ignoreSlope: next.y <= from.y) && !IsBlocked(direction, step);
    }

    /// <summary>
    /// Looks for a way round a blocked step, fanning out to either side up to
    /// <see cref="slideSearchAngle"/>. The side the player is steering toward is tried first. The step
    /// shrinks with the angle, as it would sliding along a wall. <paramref name="angle"/> is the signed
    /// yaw (degrees) of the direction that worked.
    /// </summary>
    private bool TrySlide(Vector3 direction, float step, float steer, out Vector3 next, out float angle)
    {
        next = Mount.transform.position;
        angle = 0f;
        if (slideSearchAngle <= 0f) return false;

        float first = steer < -0.1f ? -1f : 1f;
        for (float a = 20f; a <= slideSearchAngle + 0.01f; a += 25f)
        {
            for (int side = 0; side < 2; side++)
            {
                float signed = (side == 0 ? first : -first) * a;
                Vector3 dir = Quaternion.Euler(0f, signed, 0f) * direction;
                float scaled = step * Mathf.Cos(a * Mathf.Deg2Rad);
                if (TryStep(dir, scaled, out next))
                {
                    angle = signed;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Plays a hoof fall each time the animation's stride passes a footfall.</summary>
    private void UpdateHoofFalls()
    {
        float phase = Mount.StridePhase;
        if (phase < 0f)
        {
            _lastStridePhase = -1f;
            return;
        }

        if (_lastStridePhase >= 0f)
        {
            int beats = Mount.FootfallsPerStride;
            // Which footfall slot each phase is in; a change (including wrapping round) means a hoof landed.
            int before = Mathf.FloorToInt(_lastStridePhase * beats);
            int now = Mathf.FloorToInt(phase * beats);
            if (now != before) PlayHoof();
        }

        _lastStridePhase = phase;
    }

    private void PlayHoof()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null || _hoofSource == null) return;

        AudioClip clip = audio.NextClip(SoundEffect.Walk);
        if (clip == null) return;

        _hoofSource.mute = audio.IsSfxMuted;
        _hoofSource.pitch = UnityEngine.Random.Range(0.6f, 0.7f); // Heavier and slower than a person's step.
        _hoofSource.PlayOneShot(clip, hoofVolume);
    }

    /// <summary>Is there something solid in front of the animal's chest within <paramref name="step"/>?</summary>
    private bool IsBlocked(Vector3 direction, float step)
    {
        Transform t = Mount.transform;
        float seatHeight = Mount.SeatPosition.y - t.position.y;
        float halfLength = Mount.BodyLength * 0.5f;

        // Swept from the middle of the body out past the nose (or tail, reversing). The animal has no
        // colliders, so nothing of its own is in the way.
        Vector3 origin = Mount.SeatPosition;
        origin.y = t.position.y + Mathf.Max(obstacleProbeRadius + 0.3f, seatHeight * obstacleProbeHeight);
        float distance = halfLength + step + 0.1f;

        int count = Physics.SphereCastNonAlloc(origin, obstacleProbeRadius, direction, ProbeHits, distance,
                                               obstacleMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider c = ProbeHits[i].collider;
            if (c == null) continue;
            if (c is TerrainCollider) continue;
            if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(t)) continue;

            // Triggers are zones, not walls — except the play-area boundary, which the player can't
            // walk through either.
            if (c.isTrigger && !c.CompareTag("Boundary")) continue;

            // Something the body is already standing in (a low branch, a bush) reports distance 0.
            // Blocking on it would pin the animal in place for good, so only things ahead count.
            if (ProbeHits[i].distance <= 0f) continue;

            return true;
        }

        return false;
    }

    private bool IsWalkable(Vector3 p, bool ignoreSlope)
    {
        if (_terrain == null) _terrain = Terrain.activeTerrain;
        if (_terrain == null) return true;

        if (IsWater(p)) return false;

        TerrainData data = _terrain.terrainData;
        Vector3 local = p - _terrain.transform.position;
        float nx = local.x / data.size.x;
        float nz = local.z / data.size.z;
        if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return false;

        // Going downhill is always allowed, so the animal can never be trapped in a hollow.
        return ignoreSlope || data.GetSteepness(nx, nz) <= maxSlope;
    }

    private bool _lakeResolved;
    private Rect _lakeFootprint;

    /// <summary>
    /// Inside the lake's footprint and low enough to be under its surface or on its shore margin. Height
    /// alone isn't enough: parts of the garden lie lower than the shore line and are perfectly dry.
    /// </summary>
    private bool IsWater(Vector3 p)
    {
        if (!_lakeResolved)
        {
            _lakeResolved = true;
            _lakeFootprint = ResolveLakeFootprint();
        }

        // No lake found: only ground under the lake's own surface (shore margin removed) counts as water.
        if (_lakeFootprint.width <= 0f) return p.y <= waterLevel - 0.45f;

        return p.y <= waterLevel && _lakeFootprint.Contains(new Vector2(p.x, p.z));
    }

    /// <summary>The lake's XZ footprint — the same lookup PlayerAutoWalker uses, so both agree on the water.</summary>
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

    private float GroundHeight(Vector3 p)
    {
        if (_terrain == null) _terrain = Terrain.activeTerrain;
        if (_terrain == null) return p.y;
        return _terrain.SampleHeight(p) + _terrain.transform.position.y;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CARRYING THE PLAYER
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The player's pivot that puts their eyes <see cref="eyeHeightAboveSeat"/> above the seat. The seat
    /// rides on the animal's back joint, so the gait's sway and bounce come through as they animate.
    /// </summary>
    private Vector3 SeatedPivot()
    {
        return Mount.SeatPosition + Vector3.up * (eyeHeightAboveSeat - _cameraHeight);
    }

    // After the animal has moved (Update) and been animated this frame, so the rider sits on this frame's
    // pose rather than lagging one behind it.
    private void LateUpdate()
    {
        if (!IsRiding) return;

        switch (_phase)
        {
            case Phase.Mounting:
            {
                float k = AdvancePhase();
                transform.position = Arc(_fromPosition, SeatedPivot(), k);
                if (k >= 1f) _phase = Phase.Riding;
                break;
            }

            case Phase.Riding:
                transform.position = SeatedPivot();
                break;

            case Phase.Dismounting:
            {
                float k = AdvancePhase();
                transform.position = Arc(_fromPosition, _toPosition, k);
                if (k >= 1f) Release(placePlayer: false);
                break;
            }
        }
    }

    private float AdvancePhase()
    {
        _phaseTimer += Time.deltaTime;
        float k = mountDuration <= 0f ? 1f : Mathf.Clamp01(_phaseTimer / mountDuration);
        return k;
    }

    /// <summary>Eased move from <paramref name="a"/> to <paramref name="b"/>, bowed upward in the middle.</summary>
    private Vector3 Arc(Vector3 a, Vector3 b, float k)
    {
        float eased = k * k * (3f - 2f * k);
        Vector3 p = Vector3.LerpUnclamped(a, b, eased);
        p.y += Mathf.Sin(k * Mathf.PI) * mountArcHeight;
        return p;
    }
}
