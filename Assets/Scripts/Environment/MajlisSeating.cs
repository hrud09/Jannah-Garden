using System;
using IdyllicFantasyNature;
using UnityEngine;

/// <summary>
/// Makes the majlis tent (SM_MajlisTent) something the player can join: walking in under the canopy
/// sits them on the nearest free spot of the U-shaped bench, facing into the circle, and moving again
/// stands them back up.
///
/// <para><b>Sitting.</b> Once the player's feet are inside the seating area, they are eased onto the
/// nearest seat in a short arc and the view turns to face the middle of the majlis. Walking is suspended
/// the same way riding a camel suspends it (<see cref="PlayerMovement.SetMounted"/>); looking around still
/// works.</para>
///
/// <para><b>Standing.</b> The stick (or WASD) has to be let go once after sitting down, so the push that
/// walked the player in doesn't stand them straight back up; the next push stands them on the rug in front
/// of the seat. They are not sat down again until they have stepped out of the tent.</para>
///
/// <para>The model has no colliders, so this adds simple boxes for the benches and backrests and capsules
/// for the four poles at runtime. Seat spots and collider boxes are in the tent's local space, so moving
/// or turning the tent keeps them lined up. Lives on the SM_MajlisTent root.</para>
/// </summary>
[DisallowMultipleComponent]
public class MajlisSeating : MonoBehaviour
{
    [Serializable]
    public struct Seat
    {
        [Tooltip("Middle of the seat cushion's top, in the tent's local space.")]
        public Vector3 position;

        [Tooltip("Which way the seated player faces, in degrees around local up (0 = local +Z).")]
        public float yaw;

        public Seat(float x, float y, float z, float yaw)
        {
            position = new Vector3(x, y, z);
            this.yaw = yaw;
        }
    }

    [Header("Seats")]
    [Tooltip("Places on the bench the player can be sat at. Filled with the majlis layout on Reset.")]
    public Seat[] seats = DefaultSeats();

    [Tooltip("Eye height above the seat cushion while sitting (metres).")]
    [Range(0.4f, 1.2f)]
    public float eyeHeightAboveSeat = 0.75f;

    [Tooltip("How far the view tilts down on sitting, toward the tray in the middle (degrees).")]
    [Range(-20f, 40f)]
    public float seatedPitch = 12f;

    [Tooltip("How long sitting down or standing up takes (seconds).")]
    [Range(0.1f, 2f)]
    public float transitionDuration = 0.6f;

    [Tooltip("How high the move onto the seat bows upward (metres).")]
    [Range(0f, 1f)]
    public float arcHeight = 0.15f;

    [Tooltip("How far in front of the seat (metres) the player is stood up.")]
    [Range(0.3f, 1.2f)]
    public float standOffset = 0.6f;

    [Header("Seating Area (tent local space)")]
    [Tooltip("Feet inside this box sit the player down. Covers the benches and the rug between them.")]
    public Bounds seatingArea = new Bounds(new Vector3(0f, 0.5f, -0.2f), new Vector3(2.9f, 2.2f, 2.5f));

    [Tooltip("After standing up, how far (metres) outside the seating area the player must go before " +
             "walking back in sits them down again.")]
    [Range(0f, 2f)]
    public float rearmMargin = 0.3f;

    [Header("Feedback")]
    [Tooltip("Show a toast on sitting down saying how to get up again.")]
    public bool showToast = true;

    [Header("Colliders")]
    [Tooltip("Add bench, backrest and pole colliders on start (the imported model has none).")]
    public bool buildColliders = true;

    /// <summary>Raised when the player sits down (true) and once they are back on their feet (false).</summary>
    public event Action<bool> SeatedChanged;

    /// <summary>True from the moment the player starts sitting down until they are fully stood up.</summary>
    public bool IsSeated => _phase != Phase.None;

    private enum Phase { None, SittingDown, Seated, StandingUp }

    private PlayerMovement _movement;
    private CharacterController _controller;
    private CameraMovement _camera;
    private AnimalRider _rider;
    private PlayerAutoWalker _walker;
    private float _nextPlayerSearch;

    private Phase _phase;
    private int _seatIndex = -1;
    private float _timer;
    private Vector3 _from, _to;
    private float _fromYaw, _fromPitch, _toYaw;
    private float _cameraHeight;
    private bool _inputReleased;
    private bool _waitForExit;

    // ═══════════════════════════════════════════════════════════════════════════
    //  LAYOUT
    // ═══════════════════════════════════════════════════════════════════════════

    // Measured off SM_MajlisTent_LOD0: cushion tops at y≈0.40, side benches centred on x=±0.95, back bench
    // on z=−0.95, open side toward +z.
    private static Seat[] DefaultSeats() => new[]
    {
        new Seat(-0.95f, 0.40f,  0.55f,  90f),  // left bench, facing right
        new Seat(-0.95f, 0.40f,  0.00f,  90f),
        new Seat(-0.95f, 0.40f, -0.50f,  90f),
        new Seat(-0.45f, 0.40f, -0.95f,   0f),  // back bench, facing the open front
        new Seat( 0.00f, 0.40f, -0.95f,   0f),
        new Seat( 0.45f, 0.40f, -0.95f,   0f),
        new Seat( 0.95f, 0.40f, -0.50f, -90f),  // right bench, facing left
        new Seat( 0.95f, 0.40f,  0.00f, -90f),
        new Seat( 0.95f, 0.40f,  0.55f, -90f),
    };

    private void Reset()
    {
        seats = DefaultSeats();
    }

    private void Awake()
    {
        if (seats == null || seats.Length == 0) seats = DefaultSeats();
        if (buildColliders) BuildColliders();
    }

    /// <summary>Boxes for the three bench blocks and backrests, capsules for the corner poles.</summary>
    private void BuildColliders()
    {
        if (transform.Find("MajlisColliders") != null) return;

        var root = new GameObject("MajlisColliders").transform;
        root.SetParent(transform, false);
        root.gameObject.layer = gameObject.layer;

        // Seat blocks (top ≈ 0.40) and backrests (top ≈ 0.85), from the mesh's plan view.
        AddBox(root, "BenchLeft",      new Vector3(-0.97f, 0.20f, -0.05f), new Vector3(0.58f, 0.40f, 2.40f));
        AddBox(root, "BenchRight",     new Vector3( 0.97f, 0.20f, -0.05f), new Vector3(0.58f, 0.40f, 2.40f));
        AddBox(root, "BenchBack",      new Vector3( 0.00f, 0.20f, -0.95f), new Vector3(1.40f, 0.40f, 0.55f));
        AddBox(root, "BackrestLeft",   new Vector3(-1.31f, 0.42f, -0.08f), new Vector3(0.22f, 0.84f, 2.66f));
        AddBox(root, "BackrestRight",  new Vector3( 1.31f, 0.42f, -0.08f), new Vector3(0.22f, 0.84f, 2.66f));
        AddBox(root, "BackrestBack",   new Vector3( 0.00f, 0.42f, -1.31f), new Vector3(2.84f, 0.84f, 0.22f));

        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? -1.5f : 1.5f;
            float z = (i & 2) == 0 ? -1.5f : 1.5f;
            var pole = new GameObject("Pole" + i);
            pole.layer = gameObject.layer;
            pole.transform.SetParent(root, false);
            pole.transform.localPosition = new Vector3(x, 1.4f, z);
            var cap = pole.AddComponent<CapsuleCollider>();
            cap.radius = 0.1f;
            cap.height = 2.8f;
        }
    }

    private void AddBox(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = centre;
        box.size = size;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PLAYER
    // ═══════════════════════════════════════════════════════════════════════════

    private bool ResolvePlayer()
    {
        if (_movement != null) return true;
        if (Time.unscaledTime < _nextPlayerSearch) return false;
        _nextPlayerSearch = Time.unscaledTime + 1f;

        _movement = FindFirstObjectByType<PlayerMovement>();
        if (_movement == null) return false;

        _controller = _movement.GetComponent<CharacterController>();
        _camera = _movement.GetComponentInChildren<CameraMovement>();
        if (_camera == null) _camera = FindFirstObjectByType<CameraMovement>();
        _movement.OnInspectorModeChanged += HandleInspectorModeChanged;
        return true;
    }

    private void OnDisable()
    {
        if (IsSeated && _movement != null)
        {
            if (_phase == Phase.StandingUp) _movement.transform.position = _to;
            else _movement.transform.position = StandingPivot(_seatIndex);
            Finish(handBack: true);
        }
    }

    private void OnDestroy()
    {
        if (_movement != null) _movement.OnInspectorModeChanged -= HandleInspectorModeChanged;
        if (_walker != null) _walker.WalkStarted -= HandleWalkStarted;
    }

    private void HandleInspectorModeChanged(bool entering)
    {
        // Inspector Mode lifts the player away and owns the controller from here; just let go.
        if (entering && IsSeated) Finish(handBack: true);
    }

    private void HandleWalkStarted(PlayerAutoWalker.Destination destination)
    {
        if (!IsSeated) return;
        _movement.transform.position = StandingPivot(_seatIndex);
        Finish(handBack: true);
        _waitForExit = true;
    }

    private void HookWalker()
    {
        if (_walker != null) return;
        _walker = _movement.GetComponent<PlayerAutoWalker>();
        if (_walker != null) _walker.WalkStarted += HandleWalkStarted;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ═══════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!ResolvePlayer()) return;
        HookWalker();

        if (_rider == null) _rider = _movement.GetComponent<AnimalRider>();

        if (IsSeated)
        {
            // Climbed onto a camel straight from the bench: the rider owns the player now.
            if (_rider != null && _rider.IsRiding)
            {
                Finish(handBack: false);
                _waitForExit = true;
                return;
            }

            if (_phase == Phase.Seated) WatchForStandUp();
            return;
        }

        bool inside = FeetInside(rearmMargin * (_waitForExit ? 1f : 0f));
        if (_waitForExit)
        {
            if (!inside) _waitForExit = false;
            return;
        }

        if (inside && CanSitNow()) SitDown(NearestSeat());
    }

    private bool CanSitNow()
    {
        if (seats == null || seats.Length == 0) return false;
        if (_movement.IsMounted || _movement.IsInspectorMode || _movement.IsClimbing) return false;
        if (_walker != null && _walker.IsWalking) return false;
        if (_controller != null && !_controller.enabled) return false;
        return true;
    }

    private void WatchForStandUp()
    {
        bool moving = _movement.ReadMoveInput(out _) != Vector2.zero;
        if (!moving)
        {
            _inputReleased = true;
            return;
        }
        if (_inputReleased) StandUp();
    }

    // After everything else has moved this frame, so the camera and body sit on the final pose.
    private void LateUpdate()
    {
        if (_movement == null) return;

        switch (_phase)
        {
            case Phase.SittingDown:
            {
                float k = Advance();
                _movement.transform.position = Arc(_from, _to, k);
                TurnView(k);
                if (k >= 1f) _phase = Phase.Seated;
                break;
            }

            case Phase.Seated:
                _movement.transform.position = _to;
                break;

            case Phase.StandingUp:
            {
                float k = Advance();
                _movement.transform.position = Arc(_from, _to, k);
                if (k >= 1f)
                {
                    Finish(handBack: true);
                    _waitForExit = true;
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SIT / STAND
    // ═══════════════════════════════════════════════════════════════════════════

    private void SitDown(int seat)
    {
        if (seat < 0) return;

        Transform player = _movement.transform;
        _cameraHeight = _camera != null ? _camera.transform.position.y - player.position.y : 0.7f;

        _seatIndex = seat;
        _movement.SetMounted(true);

        _from = player.position;
        _to = SeatedPivot(seat);
        _toYaw = transform.eulerAngles.y + seats[seat].yaw;
        _fromYaw = _camera != null ? _camera.Yaw : player.eulerAngles.y;
        _fromPitch = _camera != null ? _camera.Pitch : 0f;
        _inputReleased = false;
        _timer = 0f;
        _phase = Phase.SittingDown;

        GameSfx.PlayAt(SoundEffect.ItemInteract, transform);
        if (showToast) RideFeatures.Toast(RideFeatures.Loc("majlis.joined"), ToastTone.Info);
        if (AudioManager.Instance != null) AudioManager.Instance.SetInMajlis(true);
        SeatedChanged?.Invoke(true);
    }

    private void StandUp()
    {
        if (_phase != Phase.Seated) return;

        _from = _movement.transform.position;
        _to = StandingPivot(_seatIndex);
        _timer = 0f;
        _phase = Phase.StandingUp;
    }

    /// <summary>Ends the seated state. <paramref name="handBack"/> gives walking back to the player.</summary>
    private void Finish(bool handBack)
    {
        if (_phase == Phase.None) return;

        _phase = Phase.None;
        _seatIndex = -1;
        if (handBack && _movement != null && !(_rider != null && _rider.IsRiding)) _movement.SetMounted(false);
        if (AudioManager.Instance != null) AudioManager.Instance.SetInMajlis(false);
        SeatedChanged?.Invoke(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GEOMETRY
    // ═══════════════════════════════════════════════════════════════════════════

    private Vector3 PlayerFeet()
    {
        Transform p = _movement.transform;
        if (_controller == null) return p.position;
        return p.position + Vector3.up * (_controller.center.y - _controller.height * 0.5f);
    }

    private bool FeetInside(float margin)
    {
        Vector3 local = transform.InverseTransformPoint(PlayerFeet());
        Bounds area = seatingArea;
        area.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
        return area.Contains(local);
    }

    private int NearestSeat()
    {
        Vector3 local = transform.InverseTransformPoint(PlayerFeet());
        int best = -1;
        float bestSq = float.MaxValue;
        for (int i = 0; i < seats.Length; i++)
        {
            Vector3 d = seats[i].position - local;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Player pivot that puts the eyes <see cref="eyeHeightAboveSeat"/> over the cushion.</summary>
    private Vector3 SeatedPivot(int seat)
    {
        Vector3 cushion = transform.TransformPoint(seats[seat].position);
        return cushion + Vector3.up * (eyeHeightAboveSeat - _cameraHeight);
    }

    /// <summary>Player pivot standing on the rug <see cref="standOffset"/> in front of the seat.</summary>
    private Vector3 StandingPivot(int seat)
    {
        if (seat < 0 || seat >= seats.Length) return _movement.transform.position;

        Vector3 local = seats[seat].position;
        Vector3 facing = Quaternion.Euler(0f, seats[seat].yaw, 0f) * Vector3.forward;
        local += facing * standOffset;
        local.y = 0f;
        Vector3 feet = transform.TransformPoint(local);

        if (_controller == null) return feet + Vector3.up * 1f;
        float footOffset = _controller.center.y - _controller.height * 0.5f;
        return feet + Vector3.up * (0.05f + _controller.skinWidth - footOffset);
    }

    private float Advance()
    {
        _timer += Time.deltaTime;
        return transitionDuration <= 0f ? 1f : Mathf.Clamp01(_timer / transitionDuration);
    }

    private Vector3 Arc(Vector3 a, Vector3 b, float k)
    {
        float eased = k * k * (3f - 2f * k);
        Vector3 p = Vector3.LerpUnclamped(a, b, eased);
        p.y += Mathf.Sin(k * Mathf.PI) * arcHeight;
        return p;
    }

    private void TurnView(float k)
    {
        if (_camera == null) return;
        float eased = k * k * (3f - 2f * k);
        float yaw = Mathf.LerpAngle(_fromYaw, _toYaw, eased);
        float pitch = Mathf.Lerp(_fromPitch, seatedPitch, eased);
        _camera.SetLookAngles(yaw, pitch);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(seatingArea.center, seatingArea.size);
        if (seats == null) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        foreach (Seat s in seats)
        {
            Gizmos.DrawSphere(s.position, 0.08f);
            Gizmos.DrawLine(s.position, s.position + Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward * 0.4f);
        }
    }
#endif
}
