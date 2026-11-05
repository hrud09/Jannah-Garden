using System;
using System.Collections.Generic;
using UnityEngine;

namespace IdyllicFantasyNature
{
    /// <summary>
    /// Decides which finger drives the camera on iOS. Whether a finger may look is settled once, from
    /// where it first went down, and never changes. So a finger that started on the joystick or on UI
    /// can't take over the camera later, however far it wanders, and it can't get in the way of the
    /// finger that should be looking.
    ///
    /// Call <see cref="Report"/> for every touch in progress, then <see cref="EndFrame"/> once.
    /// </summary>
    internal sealed class IOSTouchLookTracker
    {
        private const int NoTouch = -1;

        private struct TrackedTouch
        {
            public bool CanLook;
            public bool SeenThisFrame;
            public Vector2 Position;
        }

        private readonly Dictionary<int, TrackedTouch> _touches = new Dictionary<int, TrackedTouch>();
        private readonly List<int> _ids = new List<int>();
        private int _lookingId = NoTouch;
        private Vector2 _lastPosition;
        private bool _ignoreHeldTouches;

        public void Report(int touchId, Vector2 startPosition, Vector2 position, Func<Vector2, bool> isBlocked)
        {
            if (!_touches.TryGetValue(touchId, out TrackedTouch touch))
                touch.CanLook = !_ignoreHeldTouches && !isBlocked(startPosition);

            touch.SeenThisFrame = true;
            touch.Position = position;
            _touches[touchId] = touch;
        }

        /// <summary>Returns how far the looking finger moved this frame, in screen pixels.</summary>
        public Vector2 EndFrame()
        {
            _ignoreHeldTouches = false;

            _ids.Clear();
            _ids.AddRange(_touches.Keys);

            int nextLookingId = NoTouch;
            foreach (int id in _ids)
            {
                TrackedTouch touch = _touches[id];
                if (!touch.SeenThisFrame)
                {
                    _touches.Remove(id);
                    if (id == _lookingId)
                        _lookingId = NoTouch;
                    continue;
                }

                touch.SeenThisFrame = false;
                _touches[id] = touch;

                if (touch.CanLook && nextLookingId == NoTouch)
                    nextLookingId = id;
            }

            if (_lookingId == NoTouch)
            {
                // Start from the finger's current position so taking over never makes the view jump.
                if (nextLookingId != NoTouch)
                {
                    _lookingId = nextLookingId;
                    _lastPosition = _touches[nextLookingId].Position;
                }
                return Vector2.zero;
            }

            Vector2 position = _touches[_lookingId].Position;
            Vector2 delta = position - _lastPosition;
            _lastPosition = position;
            return delta;
        }

        /// <summary>
        /// Drops all tracking and ignores whatever is held down on the next frame. Used when the app
        /// loses focus, because touches held at that moment may never report an end and would
        /// otherwise keep hold of the camera after the app resumes.
        /// </summary>
        public void Reset()
        {
            _touches.Clear();
            _lookingId = NoTouch;
            _ignoreHeldTouches = true;
        }
    }
}
