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

        Quaternion _closed;
        float _t;          // 0 = closed, 1 = open
        bool _open;
        Collider[] _colliders;

        public bool IsOpen => _open;
        public bool IsMoving => !Mathf.Approximately(_t, _open ? 1f : 0f);

        void Awake()
        {
            _closed = transform.localRotation;
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
            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, speed * Time.deltaTime / Mathf.Max(1f, Mathf.Abs(openAngle)));
            Apply();
        }

        void Apply()
        {
            transform.localRotation = Quaternion.AngleAxis(openAngle * _t, Vector3.up) * _closed;
            if (!disableColliderWhileMoving || _colliders == null) return;
            bool solid = _t <= 0.001f || _t >= 0.999f;
            foreach (var c in _colliders)
                if (c) c.enabled = solid;
        }
    }
}
