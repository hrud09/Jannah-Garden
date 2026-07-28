using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using FlutterIntegration;

public class JannahGardenManager : MonoBehaviour
{
    public static JannahGardenManager Instance { get; private set; }

    [Header("Exit System")]
    [Tooltip("Reference to the exit confirmation UI panel GameObject.")]
    [SerializeField] private GameObject exitConfirmationPanel;

    [Tooltip("Button in the confirmation panel that leaves the game and returns the player to the Amal app.")]
    [FormerlySerializedAs("quitButton")]
    [SerializeField] private Button exitToAppButton;

    [Tooltip("Button in the confirmation panel that loads the Outer Garden scene.")]
    [SerializeField] private Button outerGardenButton;

    [Tooltip("Optional button to dismiss/close the exit confirmation panel.")]
    [SerializeField] private Button cancelButton;

    [Tooltip("Name of the scene to load when navigating to Outer Garden.")]
    [SerializeField] private string outerGardenSceneName = "Outer Garden";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        // Scene-specific manager only inside JannahGarden scene; no DontDestroyOnLoad used
    }

    // Runs before the first scene loads, so the cap applies to every scene -
    // including when a scene is opened directly - without depending on this
    // manager's GameObject being present in it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureFrameRate()
    {
        // vSync takes precedence over targetFrameRate; leaving it on makes
        // Unity ignore the target and cap to the display's refresh rate.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    private void Start()
    {
        EnsureTutorialManager();
        SetupExitSystem();
    }

    /// <summary>
    /// Adds a TutorialManager only when the scene has none. TutorialManager is a
    /// scene-wide singleton, so checking this GameObject alone is not enough: with a
    /// Tutorial Manager already placed in the scene, AddComponent runs the new
    /// component's Awake, which hits the duplicate guard and rejects it.
    /// </summary>
    private void EnsureTutorialManager()
    {
        if (TutorialManager.Instance != null)
        {
            return;
        }

        // Instance is only assigned from Awake, so a manager sitting under a disabled
        // parent has not registered yet - include inactive objects in the search.
        if (FindFirstObjectByType<TutorialManager>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        gameObject.AddComponent<TutorialManager>();
    }

    private void SetupExitSystem()
    {
        if (exitToAppButton != null)
        {
            exitToAppButton.onClick.AddListener(ExitToApp);
        }

        if (outerGardenButton != null)
        {
            outerGardenButton.onClick.AddListener(LoadOuterGarden);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(CloseExitConfirmationPanel);
        }
    }

    /// <summary>
    /// Opens the exit confirmation panel with options to quit or go to Outer Garden.
    /// </summary>
    public void OpenExitConfirmationPanel()
    {
        if (exitConfirmationPanel != null)
        {
            exitConfirmationPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[JannahGardenManager] Exit confirmation panel reference is null.", this);
        }
    }

    /// <summary>
    /// Closes/hides the exit confirmation panel.
    /// </summary>
    public void CloseExitConfirmationPanel()
    {
        if (exitConfirmationPanel != null)
        {
            exitConfirmationPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Leaves the game and hands the player back to the Amal app.
    ///
    /// Deliberately NOT <c>Application.Quit()</c>: the game runs embedded in the Flutter host via
    /// flutter_embed_unity, so quitting the player tears down the whole process and closes the app with
    /// it. Only Flutter can dismiss the Unity widget, so we ask it to — see
    /// <see cref="FlutterCommands.RequestExitGame"/> and flutter_bridge_guide.md.
    ///
    /// Nothing is saved here on purpose: pausing Unity (which is what Flutter does when the widget goes
    /// away) fires OnApplicationPause on the managers that own persistent state, and they save there.
    /// </summary>
    public void ExitToApp()
    {
        Debug.Log("[JannahGardenManager] Asking Flutter to close the game and return to the app...");

        CloseExitConfirmationPanel();

        if (FlutterBridge.Instance != null)
        {
            FlutterBridge.Instance.SendMessageToFlutterApp(
                FlutterCommands.RequestExitGame,
                new ExitGameRequestPayload { source = "exit_panel" });
            return;
        }

        Debug.LogWarning("[JannahGardenManager] No FlutterBridge — cannot return to the app.");
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast("Use the back button to leave the garden.");
        }
    }

    /// <summary>
    /// Loads the Outer Garden scene via LoadingScreenManager if available, or directly.
    /// </summary>
    public void LoadOuterGarden()
    {
        Debug.Log($"[JannahGardenManager] Loading Outer Garden scene '{outerGardenSceneName}'...");
        if (LoadingScreenManager.Instance != null)
        {
            LoadingScreenManager.Instance.LoadScene(outerGardenSceneName);
        }
        else
        {
            SceneManager.LoadScene(outerGardenSceneName);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (exitToAppButton != null)
        {
            exitToAppButton.onClick.RemoveListener(ExitToApp);
        }

        if (outerGardenButton != null)
        {
            outerGardenButton.onClick.RemoveListener(LoadOuterGarden);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(CloseExitConfirmationPanel);
        }
    }
}
