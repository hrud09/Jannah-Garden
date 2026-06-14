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

        Application.targetFrameRate = 60;
    }

    void Start()
    {
        if (isDebug)
        {
            if (NoorCoinManager.Instance != null)
            {
                NoorCoinManager.Instance.SetInitialCoinsFromFlutter(debugNoorCoinsAmount.ToString());
            }
        }
    }
}
