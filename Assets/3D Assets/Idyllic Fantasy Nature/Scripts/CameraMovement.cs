using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IdyllicFantasyNature
{
    public class CameraMovement : MonoBehaviour
    {
        [Range(1f, 10f)]
        [Tooltip("speed of the camera movement (Mouse)")]
        [SerializeField] private float _mouseSensity = 1f;

        [Range(0.01f, 5f)]
        [Tooltip("sensitivity of the mobile touch drag rotation")]
        [SerializeField] private float _touchDragSensitivity = 0.1f;

        [Range(0.5f, 100f)]
        [Tooltip("multiplier for touch drag and mouse sensitivity")]
        [SerializeField] private float _sensitivityMultiplier = 1f;

        public float SensitivityMultiplier => _sensitivityMultiplier;

        public void SetSensitivityMultiplier(float multiplier)
        {
            _sensitivityMultiplier = Mathf.Clamp(multiplier, 0.5f, 100f);
        }

        // mouse/touch rotation
        private float _xRotation;
        private float _yRotation;

        [Tooltip("the parent of this object")]
        [SerializeField] private Transform _controller;

        /// <summary>
        /// When true, allows the camera to look nearly straight up/down.
        /// Set by InspectorModeUI when entering/exiting Inspector Mode.
        /// </summary>
        [HideInInspector]
        public bool unlockFullPitch = false;

        private bool _isDraggingRotation = false;
        private int _activeTouchId = -1;
        private Vector2 _lastTouchPosition;

        // Start is called before the first frame update
        private void Start()
        {
            // Unlocks cursor and makes it visible so player can interact with the joystick UI
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Auto-detect controller parent if not set
            if (_controller == null && transform.parent != null)
            {
                _controller = transform.parent;
            }

            // Initialize rotation values from current transform state to prevent snapping
            if (_controller != null)
            {
                _yRotation = _controller.eulerAngles.y;
                _xRotation = transform.localEulerAngles.x;
            }
            else
            {
                Vector3 currentEuler = transform.eulerAngles;
                _yRotation = currentEuler.y;
                _xRotation = currentEuler.x;
            }

            if (_xRotation > 180f)
            {
                _xRotation -= 360f;
            }

            // Load look around speed sensitivity multiplier (defaults to the inspector-configured value if not saved yet)
            if (PlayerPrefs.HasKey("LookAroundSpeed"))
            {
                _sensitivityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("LookAroundSpeed"), 0.5f, 100f);
            }
        }

        // Update is called once per frame
        void Update()
        {
            float rotateX = 0f;
            float rotateY = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                // Touch Input (Mobile with New Input System)
                bool foundActiveTouch = false;

                foreach (var touch in Touchscreen.current.touches)
                {
                    var phase = touch.phase.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.None)
                        continue;

                    int fingerId = touch.touchId.ReadValue();
                    Vector2 touchPos = touch.position.ReadValue();
                    Vector2 deltaPos = touch.delta.ReadValue();
                    bool isPressed = touch.press.isPressed || touch.isInProgress;

                    // If no touch is actively driving camera rotation, check if this touch can claim it
                    if (_activeTouchId == -1)
                    {
                        if (isPressed && (phase == UnityEngine.InputSystem.TouchPhase.Began || phase == UnityEngine.InputSystem.TouchPhase.Moved))
                        {
                            if (!IsTouchOverBlockingUI(fingerId, touchPos))
                            {
                                _activeTouchId = fingerId;
                                _isDraggingRotation = true;
                                _lastTouchPosition = touchPos;
                                foundActiveTouch = true;

                                if (deltaPos.sqrMagnitude > 0.0001f)
                                {
                                    rotateX += deltaPos.x * _touchDragSensitivity;
                                    rotateY += deltaPos.y * _touchDragSensitivity;
                                }
                            }
                        }
                    }
                    else if (fingerId == _activeTouchId)
                    {
                        foundActiveTouch = true;

                        if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled || !isPressed)
                        {
                            _activeTouchId = -1;
                            _isDraggingRotation = false;
                        }
                        else
                        {
                            // Calculate rotation delta (prefer direct delta, fallback to position difference)
                            Vector2 effectiveDelta = deltaPos;
                            if (effectiveDelta.sqrMagnitude < 0.0001f && _lastTouchPosition != Vector2.zero)
                            {
                                effectiveDelta = touchPos - _lastTouchPosition;
                            }

                            rotateX += effectiveDelta.x * _touchDragSensitivity;
                            rotateY += effectiveDelta.y * _touchDragSensitivity;
                            _lastTouchPosition = touchPos;
                        }
                    }
                }

                // Safety: if the active touch vanished without sending Ended phase
                if (_activeTouchId != -1 && !foundActiveTouch)
                {
                    _activeTouchId = -1;
                    _isDraggingRotation = false;
                }
            }
            
            // Mouse Input (PC / Editor / WebGL)
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    {
                        _isDraggingRotation = false;
                    }
                    else
                    {
                        _isDraggingRotation = true;
                    }
                }

                if (_isDraggingRotation && Mouse.current.leftButton.isPressed)
                {
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    rotateX += delta.x * _mouseSensity * 0.1f;
                    rotateY += delta.y * _mouseSensity * 0.1f;
                }

                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    _isDraggingRotation = false;
                }
            }
#else
            if (Input.touchSupported && Input.touchCount > 0)
            {
                // Touch Input (Mobile with Legacy Input)
                bool foundActiveTouch = false;

                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    int fingerId = touch.fingerId;

                    if (_activeTouchId == -1)
                    {
                        if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved)
                        {
                            if (!IsTouchOverBlockingUI(fingerId, touch.position))
                            {
                                _activeTouchId = fingerId;
                                _isDraggingRotation = true;
                                _lastTouchPosition = touch.position;
                                foundActiveTouch = true;

                                if (touch.phase == TouchPhase.Moved)
                                {
                                    rotateX += touch.deltaPosition.x * _touchDragSensitivity;
                                    rotateY += touch.deltaPosition.y * _touchDragSensitivity;
                                }
                            }
                        }
                    }
                    else if (fingerId == _activeTouchId)
                    {
                        foundActiveTouch = true;

                        if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                        {
                            _activeTouchId = -1;
                            _isDraggingRotation = false;
                        }
                        else if (touch.phase == TouchPhase.Moved)
                        {
                            rotateX += touch.deltaPosition.x * _touchDragSensitivity;
                            rotateY += touch.deltaPosition.y * _touchDragSensitivity;
                            _lastTouchPosition = touch.position;
                        }
                    }
                }

                if (_activeTouchId != -1 && !foundActiveTouch)
                {
                    _activeTouchId = -1;
                    _isDraggingRotation = false;
                }
            }
            else
            {
                // Mouse Input (PC / Editor / WebGL)
                if (Input.GetMouseButtonDown(0))
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    {
                        _isDraggingRotation = false;
                    }
                    else
                    {
                        _isDraggingRotation = true;
                    }
                }

                if (_isDraggingRotation && Input.GetMouseButton(0))
                {
                    rotateX += Input.GetAxis("Mouse X") * _mouseSensity * 5f;
                    rotateY += Input.GetAxis("Mouse Y") * _mouseSensity * 5f;
                }

                if (Input.GetMouseButtonUp(0))
                {
                    _isDraggingRotation = false;
                }
            }
#endif

            // Apply sensitivity multiplier
            rotateX *= _sensitivityMultiplier;
            rotateY *= _sensitivityMultiplier;

            // Apply accumulated rotation
            _yRotation += rotateX;
            _xRotation -= rotateY;

            // Limits camera pitch angle (wider range in Inspector Mode for bird's-eye view)
            float minPitch = unlockFullPitch ? -89f : -90f;
            float maxPitch = unlockFullPitch ? 89f : 90f;
            _xRotation = Mathf.Clamp(_xRotation, minPitch, maxPitch);

            // Separate Pitch and Yaw cleanly across hierarchy:
            // 1. Controller parent rotates horizontally (Yaw) on the Y-axis
            // 2. Camera child rotates vertically (Pitch) on local X-axis
            if (_controller != null)
            {
                _controller.rotation = Quaternion.Euler(0f, _yRotation, 0f);
                transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
            }
            else
            {
                transform.rotation = Quaternion.Euler(_xRotation, _yRotation, 0f);
            }
        }

        /// <summary>
        /// Checks if a touch position/ID is over blocking UI (buttons, panels) or in the movement joystick area.
        /// </summary>
        private bool IsTouchOverBlockingUI(int fingerId, Vector2 screenPosition)
        {
            // The left side of the screen is dedicated to the movement joystick zone
            if (screenPosition.x < Screen.width * 0.35f)
            {
                return true;
            }

            // Check if touch is over any UI elements (buttons, menus, popups)
            if (EventSystem.current != null)
            {
                if (EventSystem.current.IsPointerOverGameObject(fingerId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

