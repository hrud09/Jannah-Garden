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

        // ─── Auto Climb Settings ─────────────────────────────────────
        [Header("Auto Climb")]
        [Tooltip("Automatically pull the player onto low ledges they walk into. Turn this off to leave " +
                 "them with nothing but the CharacterController's own step offset.")]
        public bool autoClimbEnabled = true;

        [Tooltip("Ledges shorter than this are already handled by the CharacterController's step offset, " +
                 "so the climb ignores them and the player just walks up.")]
        [Range(0.05f, 1f)]
        public float minClimbHeight = 0.3f;

        [Tooltip("The tallest ledge the player can pull themselves onto. Anything higher stays a wall.")]
        [Range(0.3f, 3f)]
        public float maxClimbHeight = 1.5f;

        [Tooltip("How far past the player's body to look for a ledge to climb.")]
        [Range(0.05f, 1.5f)]
        public float climbCheckDistance = 0.35f;

        [Tooltip("How long a climb takes from start to finish. Short values feel snappy, long ones cinematic.")]
        [Range(0.1f, 1.5f)]
        public float climbDuration = 0.4f;

        [Tooltip("Surfaces the climb is allowed to see. Leave as Everything unless the garden has " +
                 "collider-only helpers the player should walk through.")]
        public LayerMask climbLayerMask = ~0;

        [Tooltip("Vertical progress over the climb (0→1). Front-loaded so the player rises first, " +
                 "then settles forward onto the ledge.")]
        public AnimationCurve climbRiseCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2.4f),
            new Keyframe(0.55f, 1f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f)
        );

        [Tooltip("Forward progress over the climb (0→1). Back-loaded so the player clears the edge " +
                 "before moving over it.")]
        public AnimationCurve climbForwardCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.4f, 0.15f, 0.7f, 0.7f),
            new Keyframe(1f, 1f, 1.2f, 0f)
        );

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

        // ─── Auto Climb Runtime State ────────────────────────────────
        private bool _isClimbing;
        private float _climbTimer;
        private Vector3 _climbStartPosition;
        private Vector3 _climbEndPosition;

        /// <summary>Whether the player is currently being carried over a ledge.</summary>
        public bool IsClimbing => _isClimbing;

        /// <summary>Fired when a climb starts (true) and when it finishes (false).</summary>
        public event Action<bool> OnClimbStateChanged;

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
            // A climb owns the transform until it finishes — gravity and input would fight it.
            if (_isClimbing)
            {
                UpdateClimb();
                return;
            }

            // Keeps the controller firmly grounded on terrain slopes and prevents air-state flickering
            if (characterController.isGrounded && _controllerVelocity.y < 0)
            {
                _controllerVelocity.y = -2f;
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

            // Walking into a low ledge lifts the player over it instead of stopping them dead.
            if (TryStartClimb(movement))
            {
                return;
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

            float currentSpeed = runPressed ? (_movementSpeed + _runMultiplier) : _movementSpeed;
            Vector3 totalVelocity = movement * currentSpeed;

            // gravity affects the controller on the y-axis
            _controllerVelocity.y += _gravity * Time.deltaTime;
            totalVelocity.y = _controllerVelocity.y;

            // Single move call per frame for optimal physics collision and terrain sliding
            characterController.Move(totalVelocity * Time.deltaTime);

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
        //  AUTO CLIMB
        //
        //  There is no jump button: walking into anything low enough to
        //  step onto carries the player over it. Detection is a two-part
        //  probe — find the face blocking us, then find the top of it —
        //  so gentle slopes (which the controller already walks up) never
        //  trigger a climb.
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Looks for a climbable ledge in <paramref name="movement"/>'s direction and begins the climb if
        /// one is there. Returns true when a climb started, in which case the caller must not move the
        /// controller this frame.
        /// </summary>
        private bool TryStartClimb(Vector3 movement)
        {
            if (!autoClimbEnabled || _isClimbing) return false;
            if (characterController == null || !characterController.enabled) return false;

            // Only climb from the ground, and only when actually walking into something.
            if (!characterController.isGrounded) return false;

            Vector3 direction = new Vector3(movement.x, 0f, movement.z);
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();

            float radius = characterController.radius;
            float height = characterController.height;
            Vector3 center = transform.position + characterController.center;
            float feetY = center.y - height * 0.5f;

            // ── 1. Is something solid right in front of the player's shins? ──
            Vector3 shinProbe = new Vector3(center.x, feetY + minClimbHeight * 0.5f, center.z);
            if (!Physics.Raycast(shinProbe, direction, out RaycastHit faceHit,
                                 radius + climbCheckDistance, climbLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // A walkable slope is not a ledge — the controller handles those on its own, and climbing
            // them would turn every hillside into a series of hops.
            if (Vector3.Angle(faceHit.normal, Vector3.up) < characterController.slopeLimit) return false;

            // ── 2. Where is the top of it? Drop a ray from above, just past the face. ──
            Vector3 justPastFace = faceHit.point + direction * 0.05f;
            Vector3 topProbe = new Vector3(justPastFace.x, feetY + maxClimbHeight + 0.05f, justPastFace.z);
            if (!Physics.Raycast(topProbe, Vector3.down, out RaycastHit topHit,
                                 maxClimbHeight + 0.05f, climbLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false; // Taller than maxClimbHeight, or no top at all — it stays a wall.
            }

            // The player has to be able to stand on what they land on.
            if (Vector3.Angle(topHit.normal, Vector3.up) > characterController.slopeLimit) return false;

            float rise = topHit.point.y - feetY;
            if (rise < minClimbHeight || rise > maxClimbHeight) return false;

            // ── 3. Is there room up there for the player's whole body? ──
            Vector3 landingFeet = new Vector3(
                faceHit.point.x + direction.x * (radius + characterController.skinWidth),
                topHit.point.y + characterController.skinWidth,
                faceHit.point.z + direction.z * (radius + characterController.skinWidth));

            float capsuleHalf = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 landingCenter = landingFeet + Vector3.up * (height * 0.5f);
            if (Physics.CheckCapsule(landingCenter - Vector3.up * capsuleHalf,
                                     landingCenter + Vector3.up * capsuleHalf,
                                     radius * 0.95f, climbLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false; // Low ceiling, another object on the ledge — nowhere to land.
            }

            BeginClimb(landingCenter - characterController.center);
            return true;
        }

        /// <summary>Starts carrying the player to <paramref name="destination"/> (a transform position).</summary>
        private void BeginClimb(Vector3 destination)
        {
            _isClimbing = true;
            _climbTimer = 0f;
            _climbStartPosition = transform.position;
            _climbEndPosition = destination;
            _controllerVelocity = Vector3.zero;

            // The controller would resolve the ledge as a collision and cancel the motion, so the climb
            // drives the transform directly and hands control back when it lands.
            characterController.enabled = false;

            PlayClimbSound();
            OnClimbStateChanged?.Invoke(true);
        }

        /// <summary>Advances the current climb, re-enabling normal movement once it lands.</summary>
        private void UpdateClimb()
        {
            _climbTimer += Time.deltaTime;
            float t = climbDuration <= 0f ? 1f : Mathf.Clamp01(_climbTimer / climbDuration);

            // Rise and forward travel are curved separately: up first, then over. Moving on both axes at
            // once would drag the player through the corner of the ledge.
            float rise = climbRiseCurve != null ? climbRiseCurve.Evaluate(t) : t;
            float forward = climbForwardCurve != null ? climbForwardCurve.Evaluate(t) : t;

            Vector3 position;
            position.x = Mathf.LerpUnclamped(_climbStartPosition.x, _climbEndPosition.x, forward);
            position.z = Mathf.LerpUnclamped(_climbStartPosition.z, _climbEndPosition.z, forward);
            position.y = Mathf.LerpUnclamped(_climbStartPosition.y, _climbEndPosition.y, rise);
            transform.position = position;

            if (t >= 1f)
            {
                EndClimb();
            }
        }

        /// <summary>Settles the player on the ledge and gives the CharacterController back control.</summary>
        private void EndClimb()
        {
            transform.position = _climbEndPosition;
            _isClimbing = false;
            _climbTimer = 0f;
            _controllerVelocity = Vector3.zero;

            // Inspector Mode may have been entered mid-climb; it owns the controller's enabled state.
            if (!_isInspectorMode && !_isTransitioning && characterController != null)
            {
                characterController.enabled = true;
            }

            OnClimbStateChanged?.Invoke(false);
        }

        /// <summary>
        /// Plays a single footstep as the player pushes off. There is no dedicated climb clip, and a
        /// step reads as effort well enough without shipping a new asset.
        /// </summary>
        private void PlayClimbSound()
        {
            if (_footstepSource == null || AudioManager.Instance == null || AudioManager.Instance.sounds == null) return;

            var walkSound = System.Array.Find(AudioManager.Instance.sounds, s => s.effect == SoundEffect.Walk);
            if (walkSound == null || walkSound.clip == null) return;

            _footstepSource.pitch = UnityEngine.Random.Range(0.75f, 0.85f); // Lower than a walking step.
            _footstepSource.PlayOneShot(walkSound.clip, _footstepVolume);
            _footstepTimer = 0f;
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

            // A climb in flight would keep driving the transform from Update; drop it where it is.
            if (_isClimbing)
            {
                _isClimbing = false;
                _climbTimer = 0f;
                OnClimbStateChanged?.Invoke(false);
            }

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

        // ═════════════════════════════════════════════════════════════
        //  MOVEMENT SPEED SETTINGS API (Normal + Inspector Mode)
        // ═════════════════════════════════════════════════════════════

        /// <summary>Current Normal Mode movement speed.</summary>
        public float MovementSpeed => _movementSpeed;

        /// <summary>Sets Normal Mode movement speed (clamped to the same range as the inspector slider, 1-20).</summary>
        public void SetMovementSpeed(float value)
        {
            _movementSpeed = Mathf.Clamp(value, 1f, 20f);
        }

        /// <summary>Sets Inspector Mode movement speed (clamped to the same range as the inspector slider, 5-50).</summary>
        public void SetInspectorModeSpeed(float value)
        {
            inspectorModeSpeed = Mathf.Clamp(value, 5f, 50f);
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
