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

        private CharacterController characterController;
        Vector3 _controllerVelocity;

        // Start is called before the first frame update
        void Start()
        {
            characterController = GetComponent<CharacterController>();
            if (_joystick == null)
            {
                _joystick = FindObjectOfType<Joystick>();
            }
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
        }
    }
}
