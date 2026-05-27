using UnityEngine;
using UnityEngine.EventSystems;

namespace IdyllicFantasyNature
{
    public class CameraMovement : MonoBehaviour
    {
        [Range(1f, 10f)]
        [Tooltip("speed of the camera movement (Mouse)")]
        [SerializeField] private float _mouseSensity = 1;

        [Range(0.01f, 5f)]
        [Tooltip("sensitivity of the mobile touch drag rotation")]
        [SerializeField] private float _touchDragSensitivity = 0.1f;

        // mouse/touch rotation
        private float _xRotation;
        private float _yRotation;

        [Tooltip("the parent of this object")]
        [SerializeField] private Transform _controller;

        private bool _isDraggingRotation = false;
        private int _activeTouchId = -1;

        // Start is called before the first frame update
        private void Start()
        {
            // Unlocks cursor and makes it visible so player can interact with the joystick UI
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Initialize rotation values from the current transform rotation to prevent snapping at start
            Vector3 currentEuler = transform.eulerAngles;
            _yRotation = currentEuler.y;
            _xRotation = currentEuler.x;
            if (_xRotation > 180f)
            {
                _xRotation -= 360f;
            }
        }

        // Update is called once per frame
        void Update()
        {
            float rotateX = 0f;
            float rotateY = 0f;

            if (Input.touchSupported && Input.touchCount > 0)
            {
                // Touch Input (Mobile)
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began)
                    {
                        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                        {
                            // Touched UI (like joystick), ignore this finger for rotation
                            continue;
                        }
                        _activeTouchId = touch.fingerId;
                        _isDraggingRotation = true;
                    }
                    else if (touch.fingerId == _activeTouchId)
                    {
                        if (touch.phase == TouchPhase.Moved)
                        {
                            rotateX += touch.deltaPosition.x * _touchDragSensitivity;
                            rotateY += touch.deltaPosition.y * _touchDragSensitivity;
                        }
                        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                        {
                            _activeTouchId = -1;
                            _isDraggingRotation = false;
                        }
                    }
                }
            }
            else
            {
                // Mouse Input (PC / Editor / WebGL)
                if (Input.GetMouseButtonDown(0))
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    {
                        // Clicked UI (like joystick), ignore for rotation
                        _isDraggingRotation = false;
                    }
                    else
                    {
                        _isDraggingRotation = true;
                    }
                }

                if (_isDraggingRotation && Input.GetMouseButton(0))
                {
                    // Scale to match normal mouse looking behavior
                    rotateX += Input.GetAxis("Mouse X") * _mouseSensity * 5f;
                    rotateY += Input.GetAxis("Mouse Y") * _mouseSensity * 5f;
                }

                if (Input.GetMouseButtonUp(0))
                {
                    _isDraggingRotation = false;
                }
            }

            _yRotation += rotateX;
            _xRotation -= rotateY;

            // limits camera rotation
            _xRotation = Mathf.Clamp(_xRotation, -90f, 90f);

            // rotates camera on the y- and x-axis
            transform.rotation = Quaternion.Euler(_xRotation, _yRotation, 0);

            // rotates the controller on the y-axis so that it is on the same rotation as the camera
            if (_controller != null)
            {
                _controller.rotation = Quaternion.Euler(0, _yRotation, 0);
            }
        }
    }
}
