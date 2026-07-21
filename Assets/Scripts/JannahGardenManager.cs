using UnityEngine;

public class JannahGardenManager : MonoBehaviour
{
    public static JannahGardenManager Instance { get; private set; }

    [Header("Debug Settings")]
    public bool isDebug = false;
    public int debugNoorCoinsAmount = 500;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
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

    void Start()
    {
        // Dynamically add TutorialManager to ensure it gets instantiated and runs
        if (GetComponent<TutorialManager>() == null)
        {
            gameObject.AddComponent<TutorialManager>();
        }

        if (isDebug)
        {
            if (NoorCoinManager.Instance != null)
            {
                NoorCoinManager.Instance.SetInitialCoinsFromFlutter(debugNoorCoinsAmount.ToString());
            }
        }
    }
}
