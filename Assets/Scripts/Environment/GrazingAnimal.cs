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

    private static readonly List<GrazingAnimal> All = new List<GrazingAnimal>();

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
    }

    private void Update()
    {
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

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
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
