using UnityEngine;
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

        private CharacterController characterController;
        private Vector3 _controllerVelocity;
        private AudioSource _footstepSource;
        private AudioSource _breathingSource;
        private float _footstepTimer;

        // Start is called before the first frame update
        void Start()
        {
            characterController = GetComponent<CharacterController>();
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
    }
}
