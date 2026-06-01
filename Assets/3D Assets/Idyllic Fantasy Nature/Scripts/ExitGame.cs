using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IdyllicFantasyNature
{
    public class ExitGame : MonoBehaviour
    {
        void Update()
        {
            // If you press the ESC key in the game, the application will be closed
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Application.Quit();
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Application.Quit();
            }
#endif
        }
    }
}
