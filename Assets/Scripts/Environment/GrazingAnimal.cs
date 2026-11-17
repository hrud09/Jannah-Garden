using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient behaviour for herd animals (the camels): cycles through the model's idle animations and,
/// when <see cref="wanderRadius"/> is above zero, occasionally strolls to a nearby spot around where it
/// was placed in the scene.
///
/// Deliberately not NavMesh-driven - the scene has no baked NavMesh and these are pure scenery. Instead
/// a wander target is accepted only if it is on dry, gentle terrain, clear of placed garden items, and
/// not on top of another animal. Animals placed among the jungle trees get a radius of 0 so they stay
/// put and only play idles.
///
/// Expects an Animator whose controller has one state per clip, named exactly as in
/// <see cref="idleStates"/> and <see cref="walkState"/>. The walk clip is in-place; movement comes from
/// this script (root motion is turned off).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class GrazingAnimal : MonoBehaviour
{
    [Header("Animator states")]
    public string[] idleStates =
    {
        "Idle_01", "Idle_02", "Idle_03_Graze", "Idle_04_LookAround", "Idle_05_Ruminate", "Idle_06_FlySwat"
    };
    public string walkState = "WalkCycle";
    [Tooltip("Crossfade time between animations, in seconds.")]
    public float crossFade = 0.4f;
    [Tooltip("Random animator speed range so a herd never moves in lockstep.")]
    public Vector2 animationSpeedRange = new Vector2(0.9f, 1.1f);

    [Header("Idling")]
    [Tooltip("How long one idle animation is held before choosing what to do next (seconds).")]
    public Vector2 idleDuration = new Vector2(6f, 16f);

    [Header("Wandering")]
    [Tooltip("How far from its placed position the animal may stroll. 0 = never walks, idles only.")]
    public float wanderRadius = 8f;
    [Range(0f, 1f)]
    [Tooltip("Chance that the animal goes for a walk when an idle finishes.")]
    public float walkChance = 0.35f;
    [Tooltip("Ground speed while the walk animation plays (m/s). Tune together with the walk clip's stride.")]
    public float walkSpeed = 1.4f;
    [Tooltip("Turning speed while walking (degrees/second).")]
    public float turnSpeed = 70f;
    [Tooltip("Shortest stroll worth taking (metres).")]
    public float minWalkDistance = 2.5f;
    [Tooltip("Ground below this world height counts as water (the lake surface is at ~3.94).")]
    public float waterLevel = 4.0f;
    [Tooltip("Steepest ground the animal will walk onto, in degrees.")]
    public float maxSlope = 25f;
    [Tooltip("Keep at least this far from other animals and from their walk targets (metres).")]
    public float personalSpace = 4f;
    [Tooltip("Half-width of the patch checked for placed garden items around a walk target (metres).")]
    public float placedItemClearance = 1.5f;

    [Header("Riding")]
    [Tooltip("Whether the player can climb on and ride this animal (see AnimalRider).")]
    public bool rideable = true;
    [Tooltip("Where the rider sits, in this animal's local space. Leave at zero to put the seat over the " +
             "hump automatically, worked out from the mesh.")]
    public Vector3 seatOffset = Vector3.zero;
    [Range(0.5f, 1f)]
    [Tooltip("Automatic seat height as a fraction of the body's height (head included). 0.96 lands on top " +
             "of the camel's hump.")]
    public float autoSeatHeight = 0.96f;
    [Range(-0.3f, 0.4f)]
    [Tooltip("Automatic seat shift backwards along the body, as a fraction of its length. The neck and head " +
             "push the mesh's centre well forward of the hump.")]
    public float autoSeatBack = 0.2f;
    [Tooltip("Joint the seat is carried on while ridden, so the rider sways and bounces with the gait. Found " +
             "by name (Back.002) if empty; without one the seat is fixed to the body.")]
    public Transform seatBone;
    [Tooltip("Ridden walking speed (m/s).")]
    public float rideWalkSpeed = 2f;
    [Tooltip("Ridden running speed (m/s) - shift, or the joystick pushed all the way.")]
    public float rideRunSpeed = 4.5f;
    [Tooltip("Ridden reversing speed (m/s).")]
    public float rideReverseSpeed = 0.8f;
    [Tooltip("Ridden turning speed (degrees/second).")]
    public float rideTurnSpeed = 80f;
    [Tooltip("Fastest the gait clips may be played while ridden, as a multiple of their normal speed.")]
    public float maxRideAnimationSpeed = 2.2f;

    [Header("Ridden animator states")]
    [Tooltip("Blend tree of shuffle (0) / walk (1) / run (2) on the float below. Built by " +
             "Jannah > Animals > Build Camel Ride Animations. Without it, riding falls back to the walk clip.")]
    public string rideMoveState = "RideMove";
    [Tooltip("Walking backwards.")]
    public string rideBackState = "RideBack";
    [Tooltip("Standing still under a rider.")]
    public string rideIdleState = "RideIdle";
    [Tooltip("Float parameter that picks the gait in the move blend tree.")]
    public string rideGaitParameter = "RideGait";
    [Tooltip("How much longer the run clip's stride is than the walk's (matches the builder's swing).")]
    public float runStrideScale = 1.45f;
    [Tooltip("How much shorter the shuffle clip's stride is than the walk's.")]
    public float shuffleStrideScale = 0.5f;

    private static readonly List<GrazingAnimal> All = new List<GrazingAnimal>();

    /// <summary>Every enabled animal, for riders looking for a mount.</summary>
    public static IReadOnlyList<GrazingAnimal> Active => All;

    /// <summary>True while a player is riding this animal; its own idling and wandering are paused.</summary>
    public bool IsRidden { get; private set; }

    /// <summary>Whether a rider could climb on right now.</summary>
    public bool CanBeRidden => rideable && isActiveAndEnabled && !IsRidden;

    /// <summary>
    /// The rider's seat in world space. While ridden it is carried on <see cref="seatBone"/>, so it moves
    /// with the animation (read it in LateUpdate to get this frame's pose).
    /// </summary>
    public Vector3 SeatPosition => IsRidden && _rideSeatBone != null
        ? _rideSeatBone.TransformPoint(_seatInBone)
        : transform.TransformPoint(LocalSeat);

    /// <summary>
    /// Where the ridden gait is in its stride (0..1, wrapping), or -1 while standing. Riders time hoof
    /// sounds off it.
    /// </summary>
    public float StridePhase { get; private set; } = -1f;

    /// <summary>Footfalls per stride cycle at the current ridden gait (four at a walk, two at a run).</summary>
    public int FootfallsPerStride => _gaitValue > 1.5f ? 2 : 4;

    /// <summary>Length of the body along its forward axis (metres), for keeping clear of obstacles.</summary>
    public float BodyLength { get { ResolveBodyBounds(); return _bodyBounds.size.z * transform.lossyScale.z; } }

    /// <summary>Width of the body (metres), for stepping off to the side.</summary>
    public float BodyWidth { get { ResolveBodyBounds(); return _bodyBounds.size.x * transform.lossyScale.x; } }

    private bool _boundsResolved;
    private Bounds _bodyBounds;
    private float _homeRadius;

    private enum RideGait { None, Stand, Move, Back }
    private RideGait _rideGait;
    private float _gaitValue;
    private bool _hasRideStates;
    private int _rideMoveHash, _rideBackHash, _rideIdleHash, _gaitParamHash;
    private Transform _rideSeatBone;
    private Vector3 _seatInBone;
    private AnimatorCullingMode _cullingBeforeRide;

    private enum Mode { Idle, Turning, Walking }

    private Animator _animator;
    private Terrain _terrain;
    private Vector3 _home;
    private Vector3 _target;
    private Mode _mode;
    private float _timer;
    private int _lastIdle = -1;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = false;
        _home = transform.position;
        _target = _home;
        _homeRadius = wanderRadius;
    }

    private void OnEnable()
    {
        All.Add(this);
        _terrain = Terrain.activeTerrain;
        _animator.speed = Random.Range(animationSpeedRange.x, animationSpeedRange.y);
        // Start mid-animation at a random point so placed herds don't all start the same pose.
        StartIdle(instant: true);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (IsRidden)
        {
            IsRidden = false;
            StridePhase = -1f;
            _rideGait = RideGait.None;
            _animator.cullingMode = _cullingBeforeRide;
        }
    }

    private void Update()
    {
        if (IsRidden) return; // The rider drives the transform and animator.

        _timer -= Time.deltaTime;

        switch (_mode)
        {
            case Mode.Idle:
                if (_timer <= 0f) ChooseNext();
                break;

            case Mode.Turning:
            case Mode.Walking:
                UpdateWalk();
                break;
        }
    }

    private void ChooseNext()
    {
        if (wanderRadius > 0f && Random.value < walkChance && TryPickTarget(out _target))
        {
            _mode = Mode.Walking;
            // A walk should never run forever if something odd happens (e.g. stuck turning).
            _timer = Vector3.Distance(transform.position, _target) / Mathf.Max(0.1f, walkSpeed) * 2f + 4f;
            _animator.CrossFadeInFixedTime(walkState, crossFade);
        }
        else
        {
            StartIdle(instant: false);
        }
    }

    private void StartIdle(bool instant)
    {
        _mode = Mode.Idle;
        _target = transform.position;
        _timer = Random.Range(idleDuration.x, idleDuration.y);

        if (idleStates == null || idleStates.Length == 0) return;

        int pick = Random.Range(0, idleStates.Length);
        if (idleStates.Length > 1 && pick == _lastIdle) pick = (pick + 1) % idleStates.Length;
        _lastIdle = pick;

        if (instant) _animator.Play(idleStates[pick], 0, Random.value);
        else _animator.CrossFadeInFixedTime(idleStates[pick], crossFade);
    }

    private void UpdateWalk()
    {
        Vector3 pos = transform.position;
        Vector3 to = _target - pos;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist < 0.3f || _timer <= 0f)
        {
            StartIdle(instant: false);
            return;
        }

        Quaternion want = Quaternion.LookRotation(to / dist, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);

        // Only move once roughly facing the target, so the animal arcs naturally instead of sliding sideways.
        float facing = Vector3.Dot(transform.forward, to / dist);
        float speed = walkSpeed * Mathf.Clamp01((facing - 0.3f) / 0.5f);
        Vector3 next = pos + transform.forward * (speed * Time.deltaTime);
        next.y = GroundHeight(next);
        transform.position = next;
    }

    private bool TryPickTarget(out Vector3 target)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = _home + new Vector3(offset.x, 0f, offset.y);

            Vector3 flat = candidate - transform.position;
            flat.y = 0f;
            if (flat.magnitude < minWalkDistance) continue;

            candidate.y = GroundHeight(candidate);
            if (!IsWalkable(candidate)) continue;
            // Check the midpoint too, so the route doesn't cut straight across the lake or a placed item.
            Vector3 mid = (transform.position + candidate) * 0.5f;
            mid.y = GroundHeight(mid);
            if (!IsWalkable(mid)) continue;
            if (Crowded(candidate)) continue;

            target = candidate;
            return true;
        }

        target = transform.position;
        return false;
    }

    private bool IsWalkable(Vector3 p)
    {
        if (_terrain == null) return true;

        if (p.y < waterLevel) return false;

        TerrainData data = _terrain.terrainData;
        Vector3 local = p - _terrain.transform.position;
        float nx = local.x / data.size.x;
        float nz = local.z / data.size.z;
        if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return false;
        if (data.GetSteepness(nx, nz) > maxSlope) return false;

        GardenGrid grid = GardenGrid.Instance;
        if (grid != null && grid.IsReady)
        {
            int cells = Mathf.Max(1, Mathf.CeilToInt(placedItemClearance * 2f / grid.CellSize));
            RectInt area = grid.AreaCovering(p, new Vector2Int(cells, cells));
            if (!grid.IsAreaFree(area)) return false;
        }

        return true;
    }

    private bool Crowded(Vector3 p)
    {
        float minSqr = personalSpace * personalSpace;
        foreach (GrazingAnimal other in All)
        {
            if (other == this) continue;
            if ((other.transform.position - p).sqrMagnitude < minSqr) return true;
            if ((other._target - p).sqrMagnitude < minSqr) return true;
        }
        return false;
    }

    private float GroundHeight(Vector3 p)
    {
        if (_terrain == null) return p.y;
        return _terrain.SampleHeight(p) + _terrain.transform.position.y;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  RIDING (driven by AnimalRider)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Hands the animal to a rider: stops wandering and stands still until told to move.</summary>
    public void BeginRide()
    {
        if (IsRidden) return;

        // Work out the seat on the body first: SeatPosition switches to the bone once IsRidden is set.
        Vector3 restSeat = transform.TransformPoint(LocalSeat);

        IsRidden = true;
        _mode = Mode.Idle;
        _target = transform.position;

        _hasRideStates = _animator.HasState(0, Animator.StringToHash(rideMoveState));
        _rideMoveHash = Animator.StringToHash(rideMoveState);
        _rideBackHash = Animator.StringToHash(rideBackState);
        _rideIdleHash = Animator.StringToHash(rideIdleState);
        _gaitParamHash = Animator.StringToHash(rideGaitParameter);
        if (!_hasRideStates)
        {
            Debug.LogWarning("[GrazingAnimal] " + name + "'s controller has no '" + rideMoveState + "' state; " +
                             "riding uses the plain walk. Run Jannah > Animals > Build Camel Ride Animations.");
        }

        // The rider sits right on top of the animal, so it must keep animating even if its renderers
        // briefly leave the camera's view.
        _cullingBeforeRide = _animator.cullingMode;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        _rideSeatBone = ResolveSeatBone();
        if (_rideSeatBone != null) _seatInBone = _rideSeatBone.InverseTransformPoint(restSeat);

        _rideGait = RideGait.None; // Forces SetRideGait below to switch state.
        _gaitValue = 0f;
        SetRideGait(0f, 0f);
    }

    /// <summary>
    /// Plays the gait for a ridden ground speed (m/s; negative reverses) and turning input (-1..1).
    /// Standing still plays the calm idle, unless turning, which shuffles the feet round. Moving forward
    /// blends shuffle → walk → run with speed, and the clips are sped up to match the ground so the hooves
    /// don't slide. Reversing plays the walk backwards.
    /// </summary>
    public void SetRideGait(float groundSpeed, float turnInput)
    {
        _target = transform.position; // Keeps other animals' personal-space checks honest.

        float speed = Mathf.Abs(groundSpeed);
        bool turning = Mathf.Abs(turnInput) > 0.1f;

        RideGait gait;
        if (speed < 0.05f) gait = turning ? RideGait.Move : RideGait.Stand;
        else gait = groundSpeed > 0f ? RideGait.Move : RideGait.Back;

        // ── Which gait inside the blend tree, and how fast to play it ──
        float targetGait;
        float rate;
        float walkNatural = Mathf.Max(0.1f, walkSpeed); // Ground speed the walk clip was matched to.

        if (gait == RideGait.Move && speed < 0.05f)
        {
            // Turning on the spot: short steps, quicker the harder the stick is pushed.
            targetGait = 0f;
            rate = Mathf.Lerp(0.6f, 1.1f, Mathf.Abs(turnInput));
        }
        else if (gait == RideGait.Move)
        {
            float walkTop = Mathf.Max(0.1f, rideWalkSpeed);
            targetGait = speed <= walkTop
                ? speed / walkTop
                : 1f + Mathf.Clamp01((speed - walkTop) / Mathf.Max(0.1f, rideRunSpeed - walkTop));

            // The stride the blended clips cover per cycle, as a ground speed at normal playback.
            float strideScale = targetGait <= 1f
                ? Mathf.Lerp(shuffleStrideScale, 1f, targetGait)
                : Mathf.Lerp(1f, runStrideScale, targetGait - 1f);
            rate = speed / (walkNatural * strideScale);
        }
        else if (gait == RideGait.Back)
        {
            targetGait = 1f;
            rate = speed / walkNatural;
        }
        else
        {
            targetGait = 0f;
            rate = 1f;
        }

        if (!_hasRideStates && gait == RideGait.Move)
        {
            // No blend tree: the plain walk, sped up to cover the ground.
            rate = Mathf.Max(speed, turning ? 0.6f : 0f) / walkNatural;
        }

        // Gait blends smoothly rather than snapping between shuffle, walk and run.
        _gaitValue = Mathf.MoveTowards(_gaitValue, targetGait, Time.deltaTime * 2.5f);
        if (_hasRideStates) _animator.SetFloat(_gaitParamHash, _gaitValue);

        if (gait != _rideGait) SwitchRideState(gait);

        _animator.speed = gait == RideGait.Stand ? 1f : Mathf.Clamp(rate, 0.3f, maxRideAnimationSpeed);

        StridePhase = gait == RideGait.Stand ? -1f : Mathf.Repeat(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
        if (gait == RideGait.Back && StridePhase >= 0f) StridePhase = 1f - StridePhase;
    }

    /// <summary>
    /// Crossfades to the state for <paramref name="gait"/>. Between the moving gaits the stride carries
    /// over (they are all the same cycle), so a leg mid-swing doesn't snap back to the start.
    /// </summary>
    private void SwitchRideState(RideGait gait)
    {
        bool wasMoving = _rideGait == RideGait.Move || _rideGait == RideGait.Back;
        _rideGait = gait;

        float phase = wasMoving ? Mathf.Repeat(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f) : 0f;
        float fade = crossFade * 0.6f;

        if (!_hasRideStates)
        {
            string state = gait == RideGait.Stand
                ? (idleStates != null && idleStates.Length > 0 ? idleStates[0] : walkState)
                : walkState;
            _animator.CrossFadeInFixedTime(state, fade);
            return;
        }

        switch (gait)
        {
            case RideGait.Stand:
                _animator.CrossFadeInFixedTime(_rideIdleHash, fade);
                break;
            case RideGait.Move:
                // Coming out of reverse, the walk's cycle runs the other way round.
                _animator.CrossFade(_rideMoveHash, 0.2f, 0, wasMoving ? 1f - phase : 0f);
                break;
            case RideGait.Back:
                _animator.CrossFade(_rideBackHash, 0.2f, 0, wasMoving ? 1f - phase : 0f);
                break;
        }
    }

    private Transform ResolveSeatBone()
    {
        if (seatBone != null) return seatBone;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("Back.002")) return t;
        }
        return null;
    }

    /// <summary>
    /// Gives the animal back its own behaviour where the rider left it. Left inside its old range it
    /// keeps wandering there; left anywhere else it only idles, because wander radii were sized against
    /// the trees around each herd and the wander check itself can't see trees.
    /// </summary>
    public void EndRide()
    {
        if (!IsRidden) return;
        IsRidden = false;
        StridePhase = -1f;
        _rideGait = RideGait.None;
        _animator.cullingMode = _cullingBeforeRide;

        Vector3 flat = transform.position - _home;
        flat.y = 0f;
        wanderRadius = flat.magnitude <= _homeRadius ? _homeRadius : 0f;

        _animator.speed = Random.Range(animationSpeedRange.x, animationSpeedRange.y);
        StartIdle(instant: false);
    }

    private Vector3 LocalSeat
    {
        get
        {
            if (seatOffset != Vector3.zero) return seatOffset;
            ResolveBodyBounds();
            Bounds b = _bodyBounds;
            return new Vector3(b.center.x, b.min.y + b.size.y * autoSeatHeight, b.center.z - b.size.z * autoSeatBack);
        }
    }

    /// <summary>
    /// The body's box in this object's local space, from the meshes' rest pose rather than renderer
    /// bounds: skinned bounds lag the animation, and world-space boxes balloon once the animal turns.
    /// </summary>
    private void ResolveBodyBounds()
    {
        if (_boundsResolved) return;
        _boundsResolved = true;

        bool any = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        Matrix4x4 toLocal = transform.worldToLocalMatrix;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
            Bounds mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }

        // No mesh found: a camel-sized guess beats a seat on the ground.
        _bodyBounds = any ? result : new Bounds(new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 2.2f, 2.4f));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (rideable)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(SeatPosition, 0.2f);
        }

        if (wanderRadius <= 0f) return;
        Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.6f);
        Vector3 c = Application.isPlaying ? _home : transform.position;
        const int steps = 32;
        for (int i = 0; i < steps; i++)
        {
            float a0 = i * Mathf.PI * 2f / steps, a1 = (i + 1) * Mathf.PI * 2f / steps;
            Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * wanderRadius,
                            c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * wanderRadius);
        }
    }
#endif
}
