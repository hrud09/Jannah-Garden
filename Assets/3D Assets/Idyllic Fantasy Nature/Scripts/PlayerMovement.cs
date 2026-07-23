using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IdyllicFantasyNature
{
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private Joystick _joystick;
        [Range(1f, 20f)]
        [SerializeField] private float _movementSpeed;
        [Tooltip("run multiplier of the movement speed")]
        [Range(1f, 20f)]
        [SerializeField] private float _runMultiplier;
        [SerializeField] private float _gravity = -9.81f;
        [Range(1f, 20f)]
        [SerializeField] private float _jumpHeight;

        [Header("Audio Settings")]
        [SerializeField] private float _walkStepInterval = 0.5f;
        [Range(0f, 1f)]
        [SerializeField] private float _footstepVolume = 0.8f;
        [Range(0f, 1f)]
        [SerializeField] private float _breathingVolume = 0.4f;

        // ─── Inspector Mode Settings ─────────────────────────────────
        [Header("Inspector Mode")]
        [Tooltip("Target Y elevation above the starting ground level when Inspector Mode is active.")]
        public float inspectorModeHeight = 25f;

        [Tooltip("Movement speed while in Inspector Mode.")]
        [Range(5f, 50f)]
        public float inspectorModeSpeed = 15f;

        [Tooltip("Vertical (up/down) movement speed in Inspector Mode. On mobile, use the UI buttons.")]
        [Range(2f, 30f)]
        public float inspectorModeVerticalSpeed = 8f;

        [Tooltip("Smoothing time for Inspector Mode movement. Lower = snappier, higher = more cinematic.")]
        [Range(0.02f, 0.5f)]
        public float inspectorModeSmoothTime = 0.15f;

        [Tooltip("How fast the player rises/descends when entering/exiting Inspector Mode.")]
        [Range(1f, 10f)]
        public float inspectorModeTransitionSpeed = 3f;

        [Tooltip("Speed boost multiplier when holding shift in Inspector Mode.")]
        [Range(1f, 5f)]
        public float inspectorModeBoostMultiplier = 2.5f;

        // ─── Inspector Mode Runtime State ────────────────────────────
        private bool _isInspectorMode = false;
        private bool _isTransitioning = false;
        private Vector3 _inspectorSmoothVelocity;
        private Vector3 _savedGroundPosition;
        private float _targetInspectorY;
        private float _inspectorVerticalInput = 0f; // Set by UI buttons on mobile

        /// <summary>Whether Inspector Mode is currently active.</summary>
        public bool IsInspectorMode => _isInspectorMode;

        /// <summary>Fired when Inspector Mode is toggled. True = entering, False = exiting.</summary>
        public event Action<bool> OnInspectorModeChanged;

        // ─── Camera Reference (for Inspector Mode flight direction) ──
        private Camera _mainCamera;

        // ─── Normal Mode State ───────────────────────────────────────
        private CharacterController characterController;
        private Vector3 _controllerVelocity;
        private AudioSource _footstepSource;
        private AudioSource _breathingSource;
        private float _footstepTimer;


        private void Awake()
        {
            Application.targetFrameRate = 60; // Cap frame rate to 60 FPS for consistent movement and audio timing
        }

        // Start is called before the first frame update
        void Start()
        {
            characterController = GetComponent<CharacterController>();
            _mainCamera = Camera.main;

            if (_joystick == null)
            {
                _joystick = FindObjectOfType<Joystick>();
            }

            // Initialize Audio Sources
            _footstepSource = gameObject.AddComponent<AudioSource>();
            _footstepSource.playOnAwake = false;
            _footstepSource.spatialBlend = 0f; // 2D player audio

            _breathingSource = gameObject.AddComponent<AudioSource>();
            _breathingSource.playOnAwake = false;
            _breathingSource.loop = true;
            _breathingSource.spatialBlend = 0f; // 2D player audio
        }

        // Update is called once per frame
        void Update()
        {
            if (_isInspectorMode || _isTransitioning)
            {
                UpdateInspectorMode();
            }
            else
            {
                UpdateNormalMode();
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  NORMAL MODE (original movement logic)
        // ═════════════════════════════════════════════════════════════

        private void UpdateNormalMode()
        {
            // stops the y velocity when player is on the ground and the velocity has reached 0
            if (characterController.isGrounded && _controllerVelocity.y < 0)
            {
                _controllerVelocity.y = 0;
            }

            // get the movement input (joystick with keyboard fallback)
            float moveX = 0f;
            float moveZ = 0f;

            if (_joystick != null && _joystick.Direction != Vector2.zero)
            {
                moveX = _joystick.Horizontal;
                moveZ = _joystick.Vertical;
            }
            else
            {
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveX = -1f;
                    else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveX = 1f;

                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveZ = -1f;
                    else if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveZ = 1f;
                }
                if (Gamepad.current != null && moveX == 0f && moveZ == 0f)
                {
                    Vector2 stick = Gamepad.current.leftStick.ReadValue();
                    moveX = stick.x;
                    moveZ = stick.y;
                }
#else
                moveX = Input.GetAxis("Horizontal");
                moveZ = Input.GetAxis("Vertical");
#endif
            }

            // moves the controller in the desired direction on the x- and z-axis
            Vector3 movement = transform.right * moveX + transform.forward * moveZ;
            characterController.Move(movement * _movementSpeed * Time.deltaTime);

            // gravity affects the controller on the y-axis
            _controllerVelocity.y += _gravity * Time.deltaTime;

            // moves the controller on the y-axis
            characterController.Move(_controllerVelocity * Time.deltaTime);

            // the controller is able to jump when on the ground
            bool jumpPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
            {
                jumpPressed = true;
            }
            else if (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed)
            {
                jumpPressed = true;
            }
#else
            jumpPressed = Input.GetButton("Jump");
#endif

            if (jumpPressed && characterController.isGrounded)
            {
                _controllerVelocity.y = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
            }

            // the controller is able to run
            bool runPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
            {
                runPressed = true;
            }
#else
            runPressed = Input.GetKey(KeyCode.LeftShift);
#endif

            if (runPressed)
            {
                characterController.Move(movement * Time.deltaTime * _runMultiplier);
            }

            // Handle player audio state
            bool isMoving = (moveX != 0f || moveZ != 0f);
            bool isRunning = runPressed && isMoving;
            HandleMovementAudio(isMoving, isRunning);
        }

        // ═════════════════════════════════════════════════════════════
        //  INSPECTOR MODE (free-roam flight, no collision)
        // ═════════════════════════════════════════════════════════════

        private void UpdateInspectorMode()
        {
            // Ensure camera reference
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            // ── Handle height transition (Jumping up) ────────────────
            if (_isTransitioning)
            {
                Vector3 pos = transform.position;
                
                // Use SmoothDamp for a "jump-like" feel (fast start, decelerates at peak)
                float smoothTime = 1f / Mathf.Max(0.1f, inspectorModeTransitionSpeed);
                pos.y = Mathf.SmoothDamp(pos.y, _targetInspectorY, ref _inspectorSmoothVelocity.y, smoothTime);
                transform.position = pos;

                // Stop transition when close to target height
                if (Mathf.Abs(pos.y - _targetInspectorY) < 0.1f)
                {
                    pos.y = _targetInspectorY;
                    transform.position = pos;
                    _isTransitioning = false;
                }

                // Don't allow movement input during the initial jump up
                return;
            }

            // If not in inspector mode and not transitioning, bail (shouldn't happen)
            if (!_isInspectorMode) return;

            // ── Read movement input ──────────────────────────────────
            float moveX = 0f;
            float moveZ = 0f;

            if (_joystick != null && _joystick.Direction != Vector2.zero)
            {
                moveX = _joystick.Horizontal;
                moveZ = _joystick.Vertical;
            }
            else
            {
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveX = -1f;
                    else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveX = 1f;

                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveZ = -1f;
                    else if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveZ = 1f;
                }
                if (Gamepad.current != null && moveX == 0f && moveZ == 0f)
                {
                    Vector2 stick = Gamepad.current.leftStick.ReadValue();
                    moveX = stick.x;
                    moveZ = stick.y;
                }
#else
                moveX = Input.GetAxis("Horizontal");
                moveZ = Input.GetAxis("Vertical");
#endif
            }

            // ── Read vertical input (keyboard: Space=up, Ctrl=down) ──
            float verticalInput = _inspectorVerticalInput; // From UI buttons (mobile)
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.spaceKey.isPressed) verticalInput = 1f;
                else if (Keyboard.current.leftCtrlKey.isPressed) verticalInput = -1f;
            }
#else
            if (Input.GetKey(KeyCode.Space)) verticalInput = 1f;
            else if (Input.GetKey(KeyCode.LeftControl)) verticalInput = -1f;
#endif

            // ── Speed boost (Shift) ──────────────────────────────────
            float speedMul = 1f;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
            {
                speedMul = inspectorModeBoostMultiplier;
            }
#else
            if (Input.GetKey(KeyCode.LeftShift)) speedMul = inspectorModeBoostMultiplier;
#endif

            // ── Calculate desired movement in camera space ────────────
            // Use camera's forward/right projected onto the horizontal plane for XZ movement,
            // and world up for vertical movement
            Transform camTransform = _mainCamera.transform;
            Vector3 camForward = camTransform.forward;
            Vector3 camRight = camTransform.right;

            // For horizontal movement, project camera vectors onto XZ plane
            Vector3 flatForward = new Vector3(camForward.x, 0f, camForward.z).normalized;
            Vector3 flatRight = new Vector3(camRight.x, 0f, camRight.z).normalized;

            // Allow looking direction to also influence vertical movement when looking up/down
            float lookVerticalInfluence = camForward.y;

            Vector3 desiredMove = (flatRight * moveX + flatForward * moveZ);
            // Add vertical component from look direction when moving forward/backward
            desiredMove.y += lookVerticalInfluence * moveZ * 0.5f;
            // Add explicit vertical input
            desiredMove.y += verticalInput;

            desiredMove *= inspectorModeSpeed * speedMul;

            // ── Apply smooth damping ─────────────────────────────────
            Vector3 targetPosition = transform.position + desiredMove * Time.deltaTime;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref _inspectorSmoothVelocity,
                inspectorModeSmoothTime
            );
        }

        // ═════════════════════════════════════════════════════════════
        //  INSPECTOR MODE PUBLIC API
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Toggles Inspector Mode on or off.
        /// </summary>
        public void ToggleInspectorMode()
        {
            if (_isTransitioning) return; // Don't toggle during transition

            if (_isInspectorMode)
            {
                ExitInspectorMode();
            }
            else
            {
                EnterInspectorMode();
            }
        }

        /// <summary>
        /// Enters Inspector Mode: disables collision, elevates player, enables free-flight.
        /// </summary>
        public void EnterInspectorMode()
        {
            if (_isInspectorMode || _isTransitioning) return;

            _isInspectorMode = true;
            _isTransitioning = true;
            _inspectorSmoothVelocity = Vector3.zero;

            // Save current ground position to return to later
            _savedGroundPosition = transform.position;
            _targetInspectorY = transform.position.y + inspectorModeHeight;

            // Disable CharacterController for collision-free movement
            characterController.enabled = false;

            // Stop all movement audio
            StopAllMovementAudio();

            // Notify listeners
            OnInspectorModeChanged?.Invoke(true);

            Debug.Log($"[PlayerMovement] Inspector Mode ENABLED — rising to Y={_targetInspectorY:F1}");
        }

        /// <summary>
        /// Exits Inspector Mode: immediately re-enables collision to allow realistic falling via gravity.
        /// </summary>
        public void ExitInspectorMode()
        {
            if (!_isInspectorMode) return;

            _isInspectorMode = false;
            
            // No downward transition — we just let gravity pull them down realistically!
            _isTransitioning = false;
            _inspectorSmoothVelocity = Vector3.zero;

            // Re-enable CharacterController immediately so gravity in UpdateNormalMode takes over
            characterController.enabled = true;
            _controllerVelocity = Vector3.zero; // Start falling from 0 velocity

            // Stop all movement audio
            StopAllMovementAudio();

            // Notify listeners
            OnInspectorModeChanged?.Invoke(false);

            Debug.Log("[PlayerMovement] Inspector Mode DISABLED — falling to ground via physics.");
        }

        /// <summary>
        /// Sets vertical input for Inspector Mode from UI buttons (mobile).
        /// Pass 1 for up, -1 for down, 0 for neutral.
        /// </summary>
        public void SetInspectorVerticalInput(float value)
        {
            _inspectorVerticalInput = Mathf.Clamp(value, -1f, 1f);
        }

        private void StopAllMovementAudio()
        {
            if (_footstepSource != null && _footstepSource.isPlaying)
            {
                _footstepSource.Stop();
            }
            if (_breathingSource != null && _breathingSource.isPlaying)
            {
                _breathingSource.Stop();
            }
        }

        private void HandleMovementAudio(bool isMoving, bool isRunning)
        {
            if (AudioManager.Instance == null) return;

            AudioClip walkClip = null;
            AudioClip runClip = null;
            AudioClip breathingClip = null;

            if (AudioManager.Instance.sounds != null)
            {
                var walkSound = System.Array.Find(AudioManager.Instance.sounds, s => s.effect == SoundEffect.Walk);
                var runSound = System.Array.Find(AudioManager.Instance.sounds, s => s.effect == SoundEffect.Run);
                var breathingSound = System.Array.Find(AudioManager.Instance.sounds, s => s.effect == SoundEffect.Breathing);

                if (walkSound != null) walkClip = walkSound.clip;
                if (runSound != null) runClip = runSound.clip;
                if (breathingSound != null) breathingClip = breathingSound.clip;
            }

            if (isMoving)
            {
                // Stop breathing
                if (_breathingSource != null && _breathingSource.isPlaying)
                {
                    _breathingSource.Stop();
                }

                // Handle footsteps timer
                _footstepTimer += Time.deltaTime;
                float interval = isRunning ? (_walkStepInterval / 2f) : _walkStepInterval;
                if (_footstepTimer >= interval)
                {
                    _footstepTimer = 0f;
                    AudioClip clipToPlay = walkClip;
                    if (clipToPlay != null && _footstepSource != null)
                    {
                        _footstepSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f); // slight pitch variation
                        _footstepSource.PlayOneShot(clipToPlay, _footstepVolume);
                    }
                }
            }
            else
            {
                // Idle - stop footsteps, play breathing
                _footstepTimer = _walkStepInterval; // Reset so next step triggers immediately when walking starts

                if (breathingClip != null && _breathingSource != null)
                {
                    if (!_breathingSource.isPlaying)
                    {
                        _breathingSource.clip = breathingClip;
                        _breathingSource.volume = _breathingVolume;
                        _breathingSource.Play();
                    }
                }
                else if (_breathingSource != null && _breathingSource.isPlaying)
                {
                    _breathingSource.Stop();
                }
            }
        }

        private float _lastBoundaryToastTime = -10f;

        private void ShowBoundaryToast()
        {
            if (Time.time - _lastBoundaryToastTime > 3f)
            {
                _lastBoundaryToastTime = Time.time;
                if (ToastMessageManager.Instance != null)
                {
                    ToastMessageManager.Instance.ShowToast("Unlocked area ends here. Progress to higher level to unlock new areas.", Color.white);
                }
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // No boundary checks in Inspector Mode
            if (_isInspectorMode) return;

            if (hit.gameObject.CompareTag("Boundary"))
            {
                ShowBoundaryToast();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isInspectorMode) return;

            if (other.CompareTag("Boundary"))
            {
                ShowBoundaryToast();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_isInspectorMode) return;

            if (collision.gameObject.CompareTag("Boundary"))
            {
                ShowBoundaryToast();
            }
        }
    }
}
