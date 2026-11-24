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
/// so dragging to look is a second way to steer, as it is on foot. The animal keeps to not-too-steep
/// ground and stops at anything solid in front of its chest; it has no collider of its own. It can wade
/// into the lake and swims where the water is deep, keeping the rider above the surface.</para>
///
/// <para><b>Getting off.</b> <see cref="Dismount"/> sets the player down beside the animal on whichever
/// side has room, and gives the animal back its own idling. Entering Inspector Mode or starting an
/// autopilot walk also gets the player off first.</para>
///
/// <para><b>Flying.</b> Animals with <see cref="GrazingAnimal.canFly"/> (the pegasus) take off when the
/// up control is held (Space, or the on-screen up button) and sink with the down control (left Ctrl, or
/// the down button). In the air, forward sets the airspeed, left/right banks round, and looking well up or
/// down while flying forward climbs or dives. Flight stays inside the garden's boundary walls and under
/// their tops, and stops short of anything solid. Sinking to the ground lands it. Getting off in the air
/// first glides down and lands, then climbs off as usual.</para>
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
    [Tooltip("The lake surface renderer: its height is the water line and its footprint bounds the water. " +
             "Auto-detected under Environment/Water if empty.")]
    public Renderer lakeSurface;

    [Tooltip("Water line used when no lake surface is found (the lake surface sits at ~3.94).")]
    public float waterLevel = 3.94f;

    [Tooltip("The animal can walk into the lake. Where the water is deeper than this (metres) it stops " +
             "sinking and swims instead, with its body this far under the surface, so the rider stays dry.")]
    [Range(0.5f, 3f)]
    public float swimDepth = 1.7f;

    [Tooltip("How far (metres) to look for dry shore when the rider gets off in the water.")]
    [Range(5f, 60f)]
    public float shoreSearchRadius = 30f;

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

    [Header("Flying")]
    [Tooltip("While flying forward, looking further up or down than this (degrees) climbs or dives. " +
             "0 turns look-to-climb off, leaving it to the up/down controls.")]
    [Range(0f, 45f)]
    public float lookClimbDeadZone = 14f;

    [Tooltip("How quickly airspeed and climb rate change (m/s per second).")]
    [Range(1f, 20f)]
    public float flightAcceleration = 5f;

    [Tooltip("Room kept between the flying animal and the garden's boundary walls (metres).")]
    [Range(0f, 20f)]
    public float boundaryMargin = 4f;

    [Header("Audio")]
    [Range(0f, 1f)]
    public float hoofVolume = 0.6f;

    /// <summary>Raised when the player gets on (true) and once they are fully off (false).</summary>
    public event Action<bool> RideStateChanged;

    /// <summary>The animal being ridden (also while climbing on or off), or null.</summary>
    public GrazingAnimal Mount { get; private set; }

    public bool IsRiding => Mount != null;

    /// <summary>True while climbing on or off; the Dismount button waits for this.</summary>
    public bool IsTransitioning => _phase == Phase.Mounting || _phase == Phase.Dismounting || _landingToDismount;

    /// <summary>True while the mount is in the air.</summary>
    public bool IsFlying => IsRiding && _flying;

    /// <summary>
    /// Something steering the mount in place of the player's controls (Auto Travel). Cleared when it
    /// reports it is done, when the player gets off, or when the ride ends any other way.
    /// </summary>
    public IRideDriver Driver { get; set; }

    /// <summary>Raised when <see cref="Driver"/> lets go of the mount, however that happened.</summary>
    public event Action DriverReleased;

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

    private bool _flying;
    private bool _landingToDismount;
    private float _airSpeed;
    private float _climbRate;
    private float _pitch, _bank;
    private bool _flightAreaResolved;
    private Bounds _flightArea;

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
        ResetFlight();
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

        ReleaseDriver();

        if (_flying && !instant)
        {
            // Can't climb down from the sky: glide down and land first; Steer climbs off once grounded.
            _landingToDismount = true;
            return;
        }
        if (_flying) LandMount();

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

        ReleaseDriver();
        Mount = null;
        _phase = Phase.None;
        _speed = 0f;
        ResetFlight();

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

            if (IsDryLand(feet) && HasRoomFor(pivot)) return pivot;
        }

        // In the lake, nowhere beside the animal is dry: set the player down on the nearest shore instead.
        Vector3 shore;
        if (TryFindShore(centre, out shore)) return PivotForFeet(shore);

        return fallback;
    }

    private bool IsDryLand(Vector3 feet) => IsWalkable(feet, ignoreSlope: true) && !IsWater(feet);

    /// <summary>The nearest dry, roomy ground within <see cref="shoreSearchRadius"/>, searched in rings.</summary>
    private bool TryFindShore(Vector3 from, out Vector3 feet)
    {
        const int directions = 16;
        for (float radius = 2f; radius <= shoreSearchRadius; radius += 1.5f)
        {
            for (int i = 0; i < directions; i++)
            {
                float a = i * Mathf.PI * 2f / directions;
                feet = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                feet.y = GroundHeight(feet);
                if (IsDryLand(feet) && HasRoomFor(PivotForFeet(feet))) return true;
            }
        }

        feet = from;
        return false;
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
            ReleaseDriver();
            Mount = null;
            _phase = Phase.None;
            ResetFlight();
            if (_movement != null) _movement.SetMounted(false);
            RideStateChanged?.Invoke(false);
            return;
        }

        if (_phase == Phase.Riding) Steer();
    }

    /// <summary>
    /// This frame's controls: the <see cref="Driver"/>'s while one is steering, the player's otherwise.
    /// <paramref name="vertical"/> is up/down (-1..1) for flying mounts.
    /// </summary>
    private Vector2 ReadControls(out bool fromJoystick, out bool runHeld, out float vertical)
    {
        if (Driver != null)
        {
            if (Driver.Drive(this, out Vector2 move, out runHeld, out vertical))
            {
                fromJoystick = false;
                return move;
            }
            ReleaseDriver();
        }

        Vector2 input = _movement.ReadMoveInput(out fromJoystick);
        runHeld = _movement.IsRunPressed();
        vertical = _movement.ReadVerticalInput();
        return input;
    }

    private void ReleaseDriver()
    {
        if (Driver == null) return;
        Driver = null;
        DriverReleased?.Invoke();
    }

    /// <summary>Turns and moves the animal from the movement controls.</summary>
    private void Steer()
    {
        float dt = Time.deltaTime;
        Vector2 input = ReadControls(out bool fromJoystick, out bool runHeld, out float vertical);
        GrazingAnimal animal = Mount;
        Transform t = animal.transform;

        if (animal.canFly)
        {
            if (_flying)
            {
                SteerFlight(input, fromJoystick, runHeld, vertical);
                return;
            }

            if (_landingToDismount)
            {
                // Landed on the way to getting off.
                _landingToDismount = false;
                Dismount();
                return;
            }

            if (vertical > 0.1f)
            {
                TakeOff();
                SteerFlight(input, fromJoystick, runHeld, vertical);
                return;
            }
        }

        // ── Pace ──
        float target;
        if (input.y > 0.05f)
        {
            bool run = runHeld || (fromJoystick && input.magnitude > 0.95f);
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
        if (Driver == null && lookSteering > 0f && _camera != null && _speed > 0.3f && Mathf.Abs(input.x) < 0.2f)
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
        next.y = RideHeight(next);
        // Slopes don't matter in the water (the lake bed is hidden and the animal wades or swims), and
        // going downhill is always allowed so the animal can never be trapped in a hollow.
        bool ignoreSlope = next.y <= from.y || IsWater(from) || IsWater(next);
        return IsWalkable(next, ignoreSlope) && !IsBlocked(direction, step);
    }

    /// <summary>
    /// Height of the animal's feet at <paramref name="p"/>: the ground, except in deep water, where it
    /// swims <see cref="swimDepth"/> under the surface instead of walking along the lake bed.
    /// </summary>
    /// <summary>Same as the ride height, for a riderless animal wading home (see GrazingAnimal.WalkHome).</summary>
    public float FootHeight(Vector3 p) => RideHeight(p);

    private float RideHeight(Vector3 p)
    {
        float ground = GroundHeight(p);
        if (!InLakeFootprint(p)) return ground;
        return Mathf.Max(ground, WaterSurface - swimDepth);
    }

    /// <summary>True while the animal is in water deep enough to swim; hooves are silent then.</summary>
    private bool IsSwimming
    {
        get
        {
            Vector3 p = Mount.transform.position;
            return InLakeFootprint(p) && GroundHeight(p) < WaterSurface - swimDepth * 0.5f;
        }
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
        if (IsSwimming) return; // No hoof falls when the legs aren't touching anything.

        AudioClip clip = audio.NextClip(SoundEffect.Walk);
        if (clip == null) return;

        _hoofSource.mute = audio.IsSfxMuted;
        _hoofSource.pitch = UnityEngine.Random.Range(0.6f, 0.7f); // Heavier and slower than a person's step.
        _hoofSource.PlayOneShot(clip, hoofVolume);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FLYING
    // ═══════════════════════════════════════════════════════════════════════════

    private void ResetFlight()
    {
        _flying = false;
        _landingToDismount = false;
        _airSpeed = 0f;
        _climbRate = 0f;
        _pitch = 0f;
        _bank = 0f;
    }

    /// <summary>Leaves the ground, carrying the ground speed on as airspeed.</summary>
    private void TakeOff()
    {
        _flying = true;
        _airSpeed = Mathf.Max(0f, _speed);
        _climbRate = Mount.rideClimbSpeed * 0.5f; // A spring off the ground rather than a slow float.
        _pitch = 0f;
        _bank = 0f;
    }

    /// <summary>Touches down: levels out and hands the airspeed back to the ground gait.</summary>
    private void LandMount()
    {
        Transform t = Mount.transform;
        _flying = false;
        _speed = Mathf.Min(_airSpeed, Mount.rideRunSpeed);
        _airSpeed = 0f;
        _climbRate = 0f;
        _pitch = 0f;
        _bank = 0f;
        t.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        Mount.SetRideGait(_speed, 0f);
    }

    /// <summary>Flies the animal from the movement, up/down and look controls.</summary>
    private void SteerFlight(Vector2 input, bool fromJoystick, bool runHeld, float vertical)
    {
        float dt = Time.deltaTime;
        GrazingAnimal animal = Mount;
        Transform t = animal.transform;

        // ── Airspeed ──
        float targetSpeed;
        if (_landingToDismount)
        {
            targetSpeed = Mathf.Min(_airSpeed, animal.rideWalkSpeed * 1.5f);
        }
        else if (input.y > 0.05f)
        {
            bool boost = runHeld || (fromJoystick && input.magnitude > 0.95f);
            targetSpeed = boost ? animal.rideFlyBoostSpeed : animal.rideFlySpeed * Mathf.Clamp01(input.y / 0.9f);
        }
        else
        {
            targetSpeed = 0f; // Let go, or pulling back: slow to a hover.
        }
        float accel = targetSpeed < _airSpeed && input.y < -0.3f ? flightAcceleration * 1.8f : flightAcceleration;
        _airSpeed = Mathf.MoveTowards(_airSpeed, targetSpeed, accel * dt);

        // ── Climb and dive ──
        float targetClimb;
        if (_landingToDismount)
        {
            targetClimb = -animal.rideDiveSpeed * 0.6f;
        }
        else if (Mathf.Abs(vertical) > 0.1f)
        {
            targetClimb = vertical > 0f ? vertical * animal.rideClimbSpeed : vertical * animal.rideDiveSpeed;
        }
        else
        {
            targetClimb = 0f;
            // Looking well up or down while flying forward climbs or dives along the view.
            if (Driver == null && lookClimbDeadZone > 0f && _camera != null && _airSpeed > 2f && input.y > 0.05f)
            {
                float pitch = -_camera.Pitch; // Positive looks up.
                float beyond = Mathf.Sign(pitch) * Mathf.Max(0f, Mathf.Abs(pitch) - lookClimbDeadZone);
                targetClimb = Mathf.Sin(beyond * Mathf.Deg2Rad) * _airSpeed;
            }
            targetClimb = Mathf.Clamp(targetClimb, -animal.rideDiveSpeed, animal.rideClimbSpeed);
        }
        _climbRate = Mathf.MoveTowards(_climbRate, targetClimb, flightAcceleration * 1.5f * dt);

        // ── Turning ──
        float yaw = t.eulerAngles.y;
        float manualTurn = input.x * animal.rideTurnSpeed * dt;
        float lookTurn = 0f;
        if (Driver == null && lookSteering > 0f && _camera != null && _airSpeed > 0.3f && Mathf.Abs(input.x) < 0.2f)
        {
            float off = Mathf.DeltaAngle(yaw, _camera.Yaw);
            if (Mathf.Abs(off) < lookSteeringMaxAngle)
            {
                lookTurn = Mathf.Clamp(off, -lookSteering * dt, lookSteering * dt);
            }
        }
        yaw += manualTurn + lookTurn;
        if (manualTurn != 0f && _camera != null) _camera.SetLookAngles(_camera.Yaw + manualTurn, _camera.Pitch);

        // ── Moving ──
        Vector3 from = t.position;
        Vector3 heading = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 horizontal = heading * (_airSpeed * dt);
        if (horizontal.sqrMagnitude > 0f && IsBlockedInFlight(horizontal.normalized, horizontal.magnitude))
        {
            horizontal = Vector3.zero;
            _airSpeed = Mathf.Min(_airSpeed, 1f);
        }

        Vector3 next = from + horizontal + Vector3.up * (_climbRate * dt);
        if (_climbRate > 0f && IsBlockedInFlight(Vector3.up, _climbRate * dt)) next.y = from.y;
        next = ClampToFlightArea(next);

        float floor = RideHeight(next);
        float ceiling = Mathf.Min(GroundHeight(next) + animal.maxFlightAltitude, FlightCeiling);
        next.y = Mathf.Min(next.y, Mathf.Max(ceiling, floor));
        if (next.y >= ceiling - 0.05f && _climbRate > 0f) _climbRate = 0f;

        if (next.y <= floor + 0.02f && _climbRate <= 0.01f)
        {
            // Touched down (or settled onto the water to swim).
            next.y = floor;
            t.position = next;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            LandMount();
            return;
        }
        next.y = Mathf.Max(next.y, floor);
        t.position = next;

        // ── Attitude: nose up into a climb, down into a dive, lean into turns ──
        float turnRate = dt > 0f ? (manualTurn + lookTurn) / dt : 0f;
        // Rising or sinking on the spot only tips the nose a little; it takes airspeed to really climb or dive.
        float wantPitch = -Mathf.Clamp(_climbRate / Mathf.Max(1f, Mathf.Max(_airSpeed, 3f)), -1f, 1f) * animal.flightPitchAngle
                          * Mathf.Lerp(0.4f, 1f, _airSpeed / 6f);
        float wantBank = -Mathf.Clamp(turnRate / Mathf.Max(1f, animal.rideTurnSpeed), -1f, 1f) * animal.flightBankAngle
                         * Mathf.Clamp01(_airSpeed / 4f);
        _pitch = Mathf.MoveTowards(_pitch, wantPitch, 40f * dt);
        _bank = Mathf.MoveTowards(_bank, wantBank, 50f * dt);
        t.rotation = Quaternion.Euler(_pitch, yaw, _bank);

        animal.SetFlightGait(_airSpeed, _climbRate, next.y - GroundHeight(next));
    }

    /// <summary>Is anything solid within <paramref name="step"/> of the flying body along <paramref name="direction"/>?</summary>
    private bool IsBlockedInFlight(Vector3 direction, float step)
    {
        Transform t = Mount.transform;
        Vector3 origin = Mount.SeatPosition;
        float reach = Vector3.Dot(direction, Vector3.up) > 0.7f ? 0.6f : Mount.BodyLength * 0.5f;

        int count = Physics.SphereCastNonAlloc(origin, obstacleProbeRadius, direction, ProbeHits, reach + step + 0.1f,
                                               obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = ProbeHits[i].collider;
            if (c == null || c is TerrainCollider) continue; // The ground is handled by the height floor.
            if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(t)) continue;
            if (ProbeHits[i].distance <= 0f) continue;
            return true;
        }
        return false;
    }

    /// <summary>Keeps a flying position inside the boundary walls, <see cref="boundaryMargin"/> in from them.</summary>
    private Vector3 ClampToFlightArea(Vector3 p)
    {
        ResolveFlightArea();
        if (_flightArea.size == Vector3.zero) return p;

        float margin = boundaryMargin + Mount.BodyLength * 0.5f;
        p.x = Mathf.Clamp(p.x, _flightArea.min.x + margin, Mathf.Max(_flightArea.min.x + margin, _flightArea.max.x - margin));
        p.z = Mathf.Clamp(p.z, _flightArea.min.z + margin, Mathf.Max(_flightArea.min.z + margin, _flightArea.max.z - margin));
        return p;
    }

    /// <summary>Highest the mount may fly: a little under the tops of the boundary walls, so it can't hop over them.</summary>
    private float FlightCeiling
    {
        get
        {
            ResolveFlightArea();
            return _flightArea.size == Vector3.zero ? float.MaxValue : _flightArea.max.y - 3f;
        }
    }

    /// <summary>
    /// The box enclosed by the "Boundary" walls: each wall is a thin slab, so the inner faces of the four
    /// make the area. Empty when the scene has no such walls (then only the terrain's edge limits flight).
    /// </summary>
    private void ResolveFlightArea()
    {
        if (_flightAreaResolved) return;
        _flightAreaResolved = true;

        GameObject[] walls;
        try { walls = GameObject.FindGameObjectsWithTag("Boundary"); }
        catch (UnityException) { walls = Array.Empty<GameObject>(); }

        float minX = float.MinValue, maxX = float.MaxValue, minZ = float.MinValue, maxZ = float.MaxValue;
        float top = float.MaxValue;
        Vector3 centre = Vector3.zero;
        int found = 0;
        var boxes = new System.Collections.Generic.List<Bounds>();
        foreach (GameObject w in walls)
        {
            if (!w.TryGetComponent(out Collider c)) continue;
            boxes.Add(c.bounds);
            centre += c.bounds.center;
            found++;
        }
        if (found < 2)
        {
            if (_terrain == null) _terrain = Terrain.activeTerrain;
            if (_terrain != null)
            {
                // Heights kept finite: a Bounds stores centre and extents, so huge limits would swallow the real ones.
                Vector3 o = _terrain.transform.position, s = _terrain.terrainData.size;
                _flightArea.SetMinMax(new Vector3(o.x, o.y - 1000f, o.z), new Vector3(o.x + s.x, o.y + s.y + 1000f, o.z + s.z));
            }
            return;
        }

        centre /= found;
        foreach (Bounds b in boxes)
        {
            // A wall is long on one axis and thin on the other; its inner face is the side toward the middle.
            top = Mathf.Min(top, b.max.y);
            if (b.size.x < b.size.z)
            {
                if (b.center.x < centre.x) minX = Mathf.Max(minX, b.max.x);
                else maxX = Mathf.Min(maxX, b.min.x);
            }
            else
            {
                if (b.center.z < centre.z) minZ = Mathf.Max(minZ, b.max.z);
                else maxZ = Mathf.Min(maxZ, b.min.z);
            }
        }

        if (minX == float.MinValue || maxX == float.MaxValue || minZ == float.MinValue || maxZ == float.MaxValue) return;
        _flightArea.SetMinMax(new Vector3(minX, top - 2000f, minZ), new Vector3(maxX, top, maxZ));
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
    private float _waterSurface;

    /// <summary>World height of the lake's surface.</summary>
    private float WaterSurface
    {
        get { ResolveLake(); return _waterSurface; }
    }

    /// <summary>Inside the lake surface's rectangle (or anywhere, if no lake surface was found).</summary>
    private bool InLakeFootprint(Vector3 p)
    {
        ResolveLake();
        return _lakeFootprint.width <= 0f || _lakeFootprint.Contains(new Vector2(p.x, p.z));
    }

    /// <summary>
    /// Ground at <paramref name="p"/> lies under the lake's surface. Judged from the ground, not from the
    /// shore line plus a margin: the lake's rectangle also covers low, dry jungle ground along its edge
    /// that sits between the surface and that margin, and counting it as water walled riders out of the
    /// jungle.
    /// </summary>
    private bool IsWater(Vector3 p)
    {
        return InLakeFootprint(p) && GroundHeight(p) < WaterSurface;
    }

    private void ResolveLake()
    {
        if (_lakeResolved) return;
        _lakeResolved = true;
        _lakeFootprint = ResolveLakeFootprint();
        _waterSurface = lakeSurface != null ? lakeSurface.bounds.center.y : waterLevel;
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

/// <summary>Steers a ridden animal in place of the player's controls (see <see cref="AnimalRider.Driver"/>).</summary>
public interface IRideDriver
{
    /// <summary>
    /// Fills in this frame's controls, exactly as the player's would read: <paramref name="move"/> is the
    /// stick (x turns, y paces), <paramref name="run"/> the run key, <paramref name="vertical"/> up/down
    /// for flying mounts. Return false to hand the mount back to the player.
    /// </summary>
    bool Drive(AnimalRider rider, out Vector2 move, out bool run, out float vertical);
}
