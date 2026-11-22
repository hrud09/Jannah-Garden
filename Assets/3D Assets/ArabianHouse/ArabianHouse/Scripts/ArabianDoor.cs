using UnityEngine;

namespace JannahGarden
{
    /// <summary>
    /// Swinging door leaf. The object's pivot sits on the hinge line (set up in Blender), so opening is a
    /// rotation around the parent's up axis. Added automatically to SM_Door_* when SM_ArabianHouse.fbx is imported.
    /// Call Toggle()/Open()/Close() from your interaction code.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArabianDoor : MonoBehaviour
    {
        [Tooltip("Signed swing angle in degrees (set on import so the leaf swings into the house).")]
        public float openAngle = 90f;

        [Tooltip("Swing speed in degrees per second.")]
        public float speed = 120f;

        public bool startOpen;

        [Tooltip("Turn the leaf's collider off while it is swinging so it does not shove the player.")]
        public bool disableColliderWhileMoving = true;

        [Tooltip("Open automatically while the object tagged Player is within autoOpenRadius of the closed leaf.")]
        public bool autoOpenForPlayer = true;

        [Tooltip("Horizontal distance (metres) from the closed leaf at which it opens for the player.")]
        public float autoOpenRadius = 2.5f;

        static Transform s_player;
        static float s_nextPlayerLookup;

        Vector3 _closedCentre;
        Quaternion _closed;
        float _t;          // 0 = closed, 1 = open
        bool _open;
        Collider[] _colliders;

        public bool IsOpen => _open;
        public bool IsMoving => !Mathf.Approximately(_t, _open ? 1f : 0f);

        void Awake()
        {
            _closed = transform.localRotation;
            var r = GetComponent<Renderer>();
            _closedCentre = r ? r.bounds.center : transform.position;
            _colliders = GetComponentsInChildren<Collider>(true);
            _open = startOpen;
            _t = _open ? 1f : 0f;
            Apply();
        }

        public void Toggle() => _open = !_open;
        public void Open() => _open = true;
        public void Close() => _open = false;

        void Update()
        {
            if (autoOpenForPlayer) UpdateAutoOpen();
            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, speed * Time.deltaTime / Mathf.Max(1f, Mathf.Abs(openAngle)));
            Apply();
        }

        void UpdateAutoOpen()
        {
            if (s_player == null)
            {
                if (Time.unscaledTime < s_nextPlayerLookup) return;
                s_nextPlayerLookup = Time.unscaledTime + 1f;
                var p = GameObject.FindWithTag("Player");
                if (p == null) return;
                s_player = p.transform;
            }
            Vector3 d = s_player.position - _closedCentre;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            // small hysteresis so the leaf doesn't flutter at the edge of the radius
            if (!_open && sq < autoOpenRadius * autoOpenRadius) _open = true;
            else if (_open && sq > (autoOpenRadius + 0.75f) * (autoOpenRadius + 0.75f)) _open = false;
        }

        void Apply()
        {
            transform.localRotation = Quaternion.AngleAxis(openAngle * _t, Vector3.up) * _closed;
            if (!disableColliderWhileMoving || _colliders == null) return;
            // only a closed leaf is solid: an open leaf lies against the inner wall and its collider would
            // otherwise eat ~0.2 m of the doorway on each side, too narrow for the player's capsule
            bool solid = _t <= 0.001f;
            foreach (var c in _colliders)
                if (c) c.enabled = solid;
        }
    }
}
